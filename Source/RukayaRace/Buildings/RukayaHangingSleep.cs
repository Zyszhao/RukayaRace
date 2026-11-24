using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Buildings
{
    public static class RukayaHangingSleep
    {
        public const string FrameDefName = "Rukaya_HangingBed";
        public const string SpotDefName = "Rukaya_HangingSleepingSpot";

        private static bool defsResolved;
        private static ThingDef frameDef;
        private static ThingDef spotDef;
        private static ThingDef rukayaDef;
        private static ThoughtDef sleptHangingDef;

        private static void EnsureDefs()
        {
            if (defsResolved) return;
            defsResolved = true;
            frameDef = DefDatabase<ThingDef>.GetNamedSilentFail(FrameDefName);
            spotDef = DefDatabase<ThingDef>.GetNamedSilentFail(SpotDefName);
            rukayaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Alien_Rukaya");
            sleptHangingDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Rukaya_SleptHanging");
        }

        internal static ThoughtDef SleptHangingThought
        {
            get { EnsureDefs(); return sleptHangingDef; }
        }

        internal static ThingDef SpotDef
        {
            get { EnsureDefs(); return spotDef; }
        }

        public static bool IsHangingBed(ThingDef def)
        {
            if (def == null) return false;
            EnsureDefs();
            return (frameDef != null && def == frameDef) || (spotDef != null && def == spotDef);
        }

        public static bool IsCurtainedBed(ThingDef def)
        {
            if (def == null) return false;
            EnsureDefs();
            return frameDef != null && def == frameDef;
        }

        public static bool IsRukaya(Pawn pawn)
        {
            if (pawn == null) return false;
            EnsureDefs();
            return rukayaDef != null && pawn.def == rukayaDef;
        }

        public static bool HasRoof(Building_Bed bed)
        {
            if (bed?.Map == null) return false;
            foreach (IntVec3 cell in bed.OccupiedRect())
                if (bed.Map.roofGrid.RoofAt(cell) == null) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedEver))]
    internal static class Patch_HangingBed_CanUseBedEver
    {
        private static void Postfix(Pawn p, ThingDef bedDef, ref bool __result)
        {
            if (__result && RukayaHangingSleep.IsHangingBed(bedDef))
                __result = RukayaHangingSleep.IsRukaya(p);
        }
    }

    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedNow))]
    internal static class Patch_HangingBed_CanUseBedNow
    {
        private static void Postfix(Thing bedThing, ref bool __result)
        {
            if (__result && bedThing is Building_Bed bed && bed.def == RukayaHangingSleep.SpotDef)
                __result = RukayaHangingSleep.HasRoof(bed);
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo))]
    internal static class Patch_HangingBed_CanAssignTo
    {
        private static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted && RukayaHangingSleep.IsHangingBed(__instance.parent.def)
                && !RukayaHangingSleep.IsRukaya(pawn))
                __result = "RukayaHangingSleepNeedsRukaya".Translate();
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    internal static class Patch_HangingBed_GetBodyPos
    {
        private static void Postfix(Pawn ___pawn, PawnPosture posture, ref UnityEngine.Vector3 __result)
        {
            if (!posture.InBed()) return;
            Building_Bed bed = ___pawn?.CurrentBed();
            if (!RukayaHangingSleep.IsHangingBed(bed?.def)) return;
            float northOffset = bed.def.building.bed_pawnDrawOffset;
            __result.z = bed.Position.ToVector3Shifted().z + northOffset;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.LayingFacing))]
    internal static class Patch_HangingBed_LayingFacing
    {
        private static void Postfix(Pawn ___pawn, ref Rot4 __result)
        {
            if (RukayaHangingSleep.IsHangingBed(___pawn?.CurrentBed()?.def)) __result = Rot4.South;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker_Head), nameof(PawnRenderNodeWorker_Head.OffsetFor))]
    internal static class Patch_HangingBed_HeadOffset
    {
        private const float HeadOffsetTowardFrame = -0.20f;

        private static void Postfix(PawnDrawParms parms, ref UnityEngine.Vector3 __result)
        {
            // Portrait/cache passes draw an upright pawn even while its owner is in bed.
            // Move only the head drawn inside the map's curtained sleep frame.
            if (!parms.Portrait && !parms.Cache && !parms.Statue && parms.posture.InBed()
                && RukayaHangingSleep.IsCurtainedBed(parms.bed?.def))
                __result.z += HeadOffsetTowardFrame;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    internal static class Patch_HangingBed_DisableRenderCache
    {
        private static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            // A standing cached sprite includes wings. In-bed map draws must evaluate
            // the wing worker with the actual bed/posture rather than reuse that sprite.
            if (RukayaHangingSleep.IsRukaya(___pawn) && ___pawn.GetPosture().InBed()) disableCache = true;
        }
    }

    public class CompProperties_RukayaHangingSleep : CompProperties
    {
        public CompProperties_RukayaHangingSleep() => compClass = typeof(CompRukayaHangingSleep);
    }

    public class CompRukayaHangingSleep : ThingComp
    {
        private Pawn trackedPawn;
        private int sleepTicks;
        private bool awarded;

        public override void CompTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0 || !(parent is Building_Bed bed)) return;
            Pawn sleeper = null;
            foreach (Pawn pawn in bed.CurOccupants)
                if (pawn != null && pawn.GetPosture().InBed() && !pawn.Awake()
                    && RukayaHangingSleep.IsRukaya(pawn)) { sleeper = pawn; break; }

            if (sleeper != trackedPawn)
            {
                trackedPawn = sleeper;
                sleepTicks = 0;
                awarded = false;
            }
            if (sleeper == null || awarded) return;
            sleepTicks += 60;
            if (sleepTicks >= 2500 && sleeper.needs?.mood != null)
            {
                ThoughtDef thought = RukayaHangingSleep.SleptHangingThought ?? DefDatabase<ThoughtDef>.GetNamed("Rukaya_SleptHanging");
                sleeper.needs.mood.thoughts.memories.TryGainMemory(thought);
                awarded = true;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref trackedPawn, "trackedPawn");
            Scribe_Values.Look(ref sleepTicks, "sleepTicks");
            Scribe_Values.Look(ref awarded, "awarded");
        }
    }

    public class PlaceWorker_RukayaHangingRoof : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
                if (!cell.Roofed(map)) return "RukayaHangingSleepNeedsRoof".Translate();
            return true;
        }
    }
}
