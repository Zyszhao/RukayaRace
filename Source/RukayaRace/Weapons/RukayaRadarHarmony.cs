using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public static class RukayaRadarUtility
    {
        private static Dictionary<HediffDef, RukayaRadarTargetingExtension> cachedTargetingDefs;
        [ThreadStatic]
        private static List<IntVec3> reusableDestCells;
        [ThreadStatic]
        private static List<IntVec3> reusableSourceCells;

        private static Dictionary<HediffDef, RukayaRadarTargetingExtension> TargetingDefs
        {
            get
            {
                if (cachedTargetingDefs != null)
                {
                    return cachedTargetingDefs;
                }

                Dictionary<HediffDef, RukayaRadarTargetingExtension> map = new Dictionary<HediffDef, RukayaRadarTargetingExtension>();
                List<HediffDef> allDefs = DefDatabase<HediffDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs.Count; i++)
                {
                    HediffDef def = allDefs[i];
                    RukayaRadarTargetingExtension ext = def.GetModExtension<RukayaRadarTargetingExtension>();
                    if (ext != null)
                    {
                        map[def] = ext;
                    }
                }

                cachedTargetingDefs = map;
                return map;
            }
        }

        public static bool TryGetTargetingData(Pawn pawn, out float aimOnTargetChanceFactor, out bool ignoreBlindSmoke)
        {
            aimOnTargetChanceFactor = 1f;
            ignoreBlindSmoke = false;
            if (pawn == null || pawn.health?.hediffSet == null)
            {
                return false;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs.Count == 0)
            {
                return false;
            }

            Dictionary<HediffDef, RukayaRadarTargetingExtension> map = TargetingDefs;
            if (map.Count == 0)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (!map.TryGetValue(hediffs[i].def, out RukayaRadarTargetingExtension extension))
                {
                    continue;
                }

                found = true;
                aimOnTargetChanceFactor = Mathf.Max(aimOnTargetChanceFactor, extension.aimOnTargetChanceFactor);
                ignoreBlindSmoke |= extension.ignoreBlindSmoke;
            }

            return found;
        }

        public static bool CanSeeIgnoringBlindSmoke(Thing seer, Thing target)
        {
            if (seer == null || target == null || seer.Map != target.Map || seer.Map == null)
            {
                return false;
            }

            List<IntVec3> destCells = reusableDestCells ?? (reusableDestCells = new List<IntVec3>());
            destCells.Clear();
            ShootLeanUtility.CalcShootableCellsOf(destCells, target, seer.Position);
            for (int i = 0; i < destCells.Count; i++)
            {
                if (GenSight.LineOfSight(seer.Position, destCells[i], seer.Map, true, null, 0, 0))
                {
                    return true;
                }
            }

            List<IntVec3> sourceCells = reusableSourceCells ?? (reusableSourceCells = new List<IntVec3>());
            sourceCells.Clear();
            ShootLeanUtility.LeanShootingSourcesFromTo(seer.Position, target.Position, seer.Map, sourceCells);
            for (int i = 0; i < sourceCells.Count; i++)
            {
                if (!sourceCells[i].CanBeSeenOver(seer.Map))
                {
                    continue;
                }

                for (int j = 0; j < destCells.Count; j++)
                {
                    if (GenSight.LineOfSight(sourceCells[i], destCells[j], seer.Map, true, null, 0, 0))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimOnTargetChance_StandardTarget
    {
        public static void Postfix(TargetInfo ___target, float ___factorFromCoveringGas, ref float __result)
        {
            Pawn targetPawn = ___target.Thing as Pawn;
            if (targetPawn == null || !RukayaRadarUtility.TryGetTargetingData(targetPawn, out float aimFactor, out bool ignoreBlindSmoke))
            {
                return;
            }

            if (ignoreBlindSmoke && ___factorFromCoveringGas > 0f && ___factorFromCoveringGas < 0.999f)
            {
                __result /= ___factorFromCoveringGas;
            }

            if (aimFactor > 1f)
            {
                __result *= aimFactor;
            }

            __result = Mathf.Clamp(__result, 0.0201f, 1f);
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee))]
    public static class Patch_AttackTargetFinder_CanSee
    {
        public static bool Prefix(Thing seer, Thing target, Func<IntVec3, bool> validator, ref bool __result)
        {
            if (validator == null)
            {
                return true;
            }

            Pawn targetPawn = target as Pawn;
            if (!RukayaRadarUtility.TryGetTargetingData(targetPawn, out _, out bool ignoreBlindSmoke) || !ignoreBlindSmoke)
            {
                return true;
            }

            if (RukayaRadarUtility.CanSeeIgnoringBlindSmoke(seer, target))
            {
                __result = true;
                return false;
            }

            return true;
        }
    }
}
