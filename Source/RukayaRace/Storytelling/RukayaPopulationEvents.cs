using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace RukayaRace.Storytelling
{
    [DefOf]
    public static class RukayaPopulationDefOf
    {
        public static PawnKindDef Rukaya_Refugee;
        public static ThingDef Rukaya_WoundedArrivalFlyer;
        public static HediffDef Bruise;

        static RukayaPopulationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaPopulationDefOf));
        }
    }

    // Keep vanilla's choice letter, accept/reject signals, timeout and walk-in arrival.
    // Generate the explicit kind locally: HAR patches vanilla's generation method to
    // substitute the player's race, which would undo this independent Rukaya event.
    public class QuestNode_Root_RukayaWandererJoin : QuestNode_Root_WandererJoin_WalkIn
    {
        public override Pawn GeneratePawn_NewTemp(Map map)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(RukayaPopulationUtility.GenerationRequest(map));
            if (!pawn.IsWorldPawn())
                Find.WorldPawns.PassToWorld(pawn);
            return pawn;
        }
    }

    internal static class RukayaPopulationUtility
    {
        public static PawnGenerationRequest GenerationRequest(Map map)
        {
            return new PawnGenerationRequest(RukayaPopulationDefOf.Rukaya_Refugee,
                faction: null, context: PawnGenerationContext.NonPlayer, tile: map?.Tile,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: true, colonistRelationChanceFactor: 20f,
                allowPregnant: false, developmentalStages: DevelopmentalStage.Adult,
                forceRecruitable: true);
        }

        public static bool HasFlightWings(Pawn pawn)
        {
            return pawn.GetComp<Flight.CompRukayaFlightControl>()?.BothWings == true;
        }

        public static bool SafeLandingCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map)
                && !cell.Fogged(map) && cell.GetFirstPawn(map) == null
                && !cell.ContainsStaticFire(map) && map.reachability.CanReachColony(cell);
        }

        public static bool TryFindLandingCell(Map map, IntVec3 center, out IntVec3 cell)
        {
            return CellFinder.TryFindRandomCellNear(center, map, 25,
                c => SafeLandingCell(c, map), out cell);
        }

        public static void ApplyLandingInjuries(Pawn pawn)
        {
            // Match Milira's fallen-angel handling with vanilla injuries and abasia.
            // Apply everything at touchdown so the descent animation can finish.
            HealthUtility.DamageUntilDowned(pawn);
            HealthUtility.AdjustSeverity(pawn, HediffDefOf.Abasia, Rand.Range(0.1f, 0.2f));

            // Randomly leave each wing at 10-30% health, without destroying it.
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.defName == "Rukaya_Wing").ToList())
            {
                float maxHealth = part.def.GetMaxHealth(pawn);
                float damage = pawn.health.hediffSet.GetPartHealth(part) - maxHealth * Rand.Range(0.1f, 0.3f);
                if (damage <= 0f)
                    continue;
                Hediff injury = HediffMaker.MakeHediff(RukayaPopulationDefOf.Bruise, pawn, part);
                injury.Severity = damage;
                pawn.health.AddHediff(injury);
            }
        }
    }

    public class IncidentWorker_RukayaWoundedArrival : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return base.CanFireNowSub(parms) && parms.target is Map map && !map.Tile.LayerDef.isSpace
                && RukayaPopulationUtility.TryFindLandingCell(map, DropCellFinder.TradeDropSpot(map), out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map) || map.Tile.LayerDef.isSpace
                || !RukayaPopulationUtility.TryFindLandingCell(map, DropCellFinder.TradeDropSpot(map), out IntVec3 cell))
                return false;

            PawnGenerationRequest request = RukayaPopulationUtility.GenerationRequest(map);
            request.ValidatorPreGear = RukayaPopulationUtility.HasFlightWings;
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            pawn.guest.Recruitable = true;
            pawn.Rotation = Rot4.South;
            Skyfaller_RukayaWoundedArrival flyer = (Skyfaller_RukayaWoundedArrival)
                SkyfallerMaker.MakeSkyfaller(RukayaPopulationDefOf.Rukaya_WoundedArrivalFlyer, pawn);
            GenSpawn.Spawn(flyer, cell, map);
            // Track the pawn so the letter still locates her after the carrier vanishes.
            SendStandardLetter(parms, pawn, pawn.Named("PAWN"));
            return true;
        }
    }

    // A flying pawn is the payload AND the visible graphic. There is no pod shell,
    // explosion, shrapnel or roof impact. Skyfaller owns/saves the pawn during descent.
    public class Skyfaller_RukayaWoundedArrival : Skyfaller_FlyingPawn
    {
        protected override void HitRoof() { }

        protected override void Impact()
        {
            // A roof/building/pawn may have appeared after the event selected its cell.
            if (!RukayaPopulationUtility.SafeLandingCell(Position, Map))
            {
                if (!RukayaPopulationUtility.TryFindLandingCell(Map, Position, out IntVec3 cell))
                {
                    ticksToImpact = 60;
                    return;
                }
                Position = cell;
            }
            base.Impact();
        }

        protected override void SpawnThings()
        {
            Pawn pawn = Pawn;
            innerContainer.Remove(pawn);
            pawn.Drawer.renderer.SetAnimation(null);
            GenSpawn.Spawn(pawn, Position, Map, Rot4.South);
            RukayaPopulationUtility.ApplyLandingInjuries(pawn);
        }
    }
}
