using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RukayaRace.Replication
{
    public static class RukayaReservoirLoadingUtility
    {
        public static ThingDef ContainerDef => RukayaReplicationDefOf.Rukaya_ActiveMatterContainer;

        /// <summary>
        /// A container counts as a valid load source when it is an intact active matter container
        /// that this pawn may pick up. Storage is deliberately NOT required: freshly refined
        /// containers are dumped on the floor next to the refiner and must still be loadable.
        /// </summary>
        public static bool IsLoadableContainer(Thing thing, Pawn pawn)
        {
            return thing != null
                && !thing.Destroyed
                && thing.def == ContainerDef
                && thing.stackCount > 0
                && !thing.IsForbidden(pawn)
                && pawn.CanReserve(thing, 1, -1, null, false);
        }

        public static Thing CarriedContainer(Pawn pawn)
        {
            Thing carried = pawn?.carryTracker?.CarriedThing;
            return carried != null && !carried.Destroyed && carried.def == ContainerDef && carried.stackCount > 0
                ? carried
                : null;
        }

        /// <summary>
        /// Mirrors vanilla RefuelWorkGiverUtility.FindBestFuel: only reachability, ownership and
        /// forbiddance matter. The previous implementation additionally required
        /// Thing.IsInValidStorage(), which is only true inside a stockpile zone / shelf / storage
        /// building, so loose containers were invisible to the hauling work giver and pawns never
        /// fetched them.
        /// </summary>
        public static Thing FindSource(Pawn pawn, CompRukayaActiveMatterReservoir reservoir)
        {
            if (pawn?.Map == null
                || reservoir?.parent == null
                || !reservoir.parent.Spawned
                || reservoir.IsFull
                || ContainerDef == null)
            {
                return null;
            }

            return GenClosest.ClosestThingReachable(
                reservoir.parent.Position,
                pawn.Map,
                ThingRequest.ForDef(ContainerDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false, false, false, true),
                9999f,
                thing => IsLoadableContainer(thing, pawn),
                null,
                0,
                -1);
        }

        public static bool CanLoad(Pawn pawn, Thing building, bool forced = false)
        {
            if (pawn == null || building == null || building.Destroyed || !building.Spawned)
            {
                return false;
            }

            CompRukayaActiveMatterReservoir reservoir = building.TryGetComp<CompRukayaActiveMatterReservoir>();
            if (reservoir == null || reservoir.IsFull)
            {
                return false;
            }

            // A building without a faction is treated as player owned: dev mode spawned terminals and
            // minified/reinstalled ones do not always carry a faction, and refusing those would make
            // the whole feature look dead in test setups.
            if (building.Faction != null && building.Faction != pawn.Faction)
            {
                return false;
            }

            if (building.IsForbidden(pawn))
            {
                return false;
            }

            // Danger.Some was hard coded here before, which permanently blocked the job whenever a
            // fire or a hostile sat anywhere on the route; vanilla building givers use the most
            // permissive value they can. 
            if (!pawn.CanReserveAndReach(building, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
            {
                return false;
            }

            // Containers already in hand are used directly. Anything else in the carry tracker would
            // make the pick-up toil fail (no remaining stack space), so such a pawn is skipped until
            // its hands are free again.
            Thing hands = pawn.carryTracker?.CarriedThing;
            if (hands != null)
            {
                return hands.def == ContainerDef && !hands.Destroyed && hands.stackCount > 0;
            }

            if (FindSource(pawn, reservoir) != null)
            {
                return true;
            }

            if (forced)
            {
                JobFailReason.Is("Rukaya_ReservoirNoStoredMatter".Translate());
            }

            return false;
        }

        public static Job MakeLoadJob(Pawn pawn, Thing building, bool forced = false)
        {
            if (!CanLoad(pawn, building, forced))
            {
                return null;
            }

            CompRukayaActiveMatterReservoir reservoir = building.TryGetComp<CompRukayaActiveMatterReservoir>();
            Thing source = CarriedContainer(pawn) ?? FindSource(pawn, reservoir);
            if (source == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(RukayaReplicationDefOf.Rukaya_LoadActiveMatterReservoir, source, building);
            job.count = Mathf.Max(1, Mathf.Min(source.stackCount, reservoir.FreeCapacity));
            job.playerForced = forced;
            return job;
        }

        public static int GlobalCapacity(CompRukayaActiveMatterReservoir reservoir)
        {
            return reservoir?.GlobalCapacity ?? RukayaReplicationUtility.DefaultReservoirCapacity;
        }

        /// <summary>
        /// Dev mode diagnostic: logs every gate of the automatic loading path to Player.log so a
        /// "pawns do not haul" report can be traced to the exact failing condition.
        /// </summary>
        public static void LogDiagnostics(Thing building)
        {
            if (building == null)
            {
                return;
            }

            Map map = building.MapHeld;
            CompRukayaActiveMatterReservoir reservoir = building.TryGetComp<CompRukayaActiveMatterReservoir>();
            WorkGiverDef giverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Rukaya_LoadReplicationReservoir");
            WorkTypeDef hauling = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Hauling");

            Log.Message("[Rukaya] reservoir load diagnostics for " + building);
            Log.Message("  building: def=" + building.def.defName
                + " spawned=" + building.Spawned
                + " destroyed=" + building.Destroyed
                + " fogged=" + building.Fogged()
                + " faction=" + (building.Faction?.Name ?? "null")
                + " passability=" + building.def.passability
                + " hasInteractionCell=" + building.def.hasInteractionCell);
            Log.Message("  reservoir: comp=" + (reservoir != null ? "OK" : "MISSING")
                + " stored=" + (reservoir?.StoredContainers ?? -1)
                + " capacity=" + (reservoir?.Capacity ?? -1)
                + " customLimit=" + (reservoir?.StorageLimit ?? -999)
                + " free=" + (reservoir?.FreeCapacity ?? -1)
                + " isFull=" + (reservoir?.IsFull ?? true));
            Log.Message("  containerDef=" + (RukayaReplicationDefOf.Rukaya_ActiveMatterContainer?.defName ?? "NULL"));
            Log.Message("  containersOnMap=" + (map?.listerThings.ThingsOfDef(RukayaReplicationDefOf.Rukaya_ActiveMatterContainer)?.Count ?? -1));
            Log.Message("  workGiverDef=" + (giverDef != null ? giverDef.defName + " class=" + giverDef.giverClass : "MISSING")
                + " workType=" + (giverDef?.workType?.defName ?? "MISSING")
                + " listedUnderHauling=" + (hauling != null && giverDef != null && hauling.workGiversByPriority.Contains(giverDef)));

            if (map == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                Log.Message("  pawn " + pawn.LabelShort
                    + ": haulingPriority=" + (hauling != null && pawn.workSettings != null ? pawn.workSettings.GetPriority(hauling) : -1)
                    + " manipulation=" + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation).ToString("0.##")
                    + " carrying=" + (pawn.carryTracker?.CarriedThing?.LabelShort ?? "nothing")
                    + " canReserveReach=" + (building.Spawned && pawn.CanReserveAndReach(building, PathEndMode.Touch, Danger.Deadly, 1, -1, null, false))
                    + " source=" + (FindSource(pawn, reservoir)?.LabelShort ?? "NONE")
                    + " canLoad=" + CanLoad(pawn, building, false));
            }
        }
    }

    public class WorkGiver_RukayaLoadReservoir : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return RukayaReservoirLoadingUtility.CanLoad(pawn, thing, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return RukayaReservoirLoadingUtility.MakeLoadJob(pawn, thing, forced);
        }
    }

    public class JobDriver_LoadActiveMatterReservoir : JobDriver
    {
        private const TargetIndex SourceIndex = TargetIndex.A;
        private const TargetIndex BuildingIndex = TargetIndex.B;

        /// <summary>
        /// True when the job was created for a container the pawn is already holding, in which case
        /// the walk-to-source and pick-up toils are skipped entirely.
        /// </summary>
        private bool LoadingFromHand
        {
            get
            {
                Thing source = job.GetTarget(SourceIndex).Thing;
                return source != null && source == pawn.carryTracker?.CarriedThing;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // A container in the pawn's own hands needs no reservation; reserving it would fail
            // because the thing is currently despawned inside the carry tracker.
            if (!LoadingFromHand)
            {
                Thing source = job.GetTarget(SourceIndex).Thing;
                // Reserve at most what is still on the ground: ReservationManager rejects a requested
                // stack count larger than the thing's current stackCount, which would otherwise end
                // the job before its first toil.
                int reserveCount = source == null ? -1 : Mathf.Min(Mathf.Max(1, job.count), source.stackCount);
                if (!pawn.Reserve(job.GetTarget(SourceIndex), job, 1, reserveCount, null, errorOnFailed))
                {
                    return false;
                }
            }

            return pawn.Reserve(job.GetTarget(BuildingIndex), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Driver-wide fail condition: the terminal only. The hauled container must NOT be a
            // driver-wide condition, because Toils_Haul.StartCarryThing despawns it into the pawn's
            // carry tracker; a driver-wide "despawned" check then fails the job the instant the pawn
            // picks the stack up, and the work giver immediately re-issues the identical job
            // (endless loop -> "started 10 jobs in one tick"). Vanilla JobDriver_Refuel scopes the
            // very same checks this way: driver-wide for the building, toil-scoped for the item.
            this.FailOnDespawnedNullOrForbidden(BuildingIndex);

            if (!LoadingFromHand)
            {
                yield return Toils_Goto.GotoThing(SourceIndex, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(SourceIndex)
                    .FailOnSomeonePhysicallyInteracting(SourceIndex);
                // subtractNumTakenFromJobCount keeps job.count in sync with what is actually picked
                // up, so the pawn never hauls more than the reservoir can accept.
                yield return Toils_Haul.StartCarryThing(SourceIndex, false, true, false, true)
                    .FailOnDestroyedNullOrForbidden(SourceIndex);
            }

            yield return Toils_Goto.GotoThing(BuildingIndex, PathEndMode.Touch);
            yield return MakeDepositToil();
        }

        private Toil MakeDepositToil()
        {
            Toil toil = ToilMaker.MakeToil("LoadActiveMatterReservoir");
            toil.initAction = delegate
            {
                Thing building = job.GetTarget(BuildingIndex).Thing;
                CompRukayaActiveMatterReservoir reservoir = building?.TryGetComp<CompRukayaActiveMatterReservoir>();
                // Only ever load what the pawn is physically holding: this keeps the job honest even
                // if the carried stack was dropped or destroyed while walking over.
                Thing carried = RukayaReservoirLoadingUtility.CarriedContainer(pawn);
                if (reservoir == null || carried == null)
                {
                    return;
                }

                if (reservoir.TryLoadFromCarried(carried) > 0)
                {
                    return;
                }

                // Nothing could be stored (for example the reservoir filled up from another pawn
                // while this one walked over). Put the stack back on the map so the pawn is not left
                // holding it and the next job cannot become an instant no-op that restarts forever.
                Thing dropped;
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
