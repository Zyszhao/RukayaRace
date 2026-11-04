using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Replication
{
    public class CompProperties_RukayaActiveMatterReservoir : CompProperties
    {
        public int capacity = RukayaReplicationUtility.DefaultReservoirCapacity;

        public CompProperties_RukayaActiveMatterReservoir()
        {
            compClass = typeof(CompRukayaActiveMatterReservoir);
        }
    }

    public class CompRukayaActiveMatterReservoir : ThingComp, IThingHolder
    {
        public const int FollowGlobalLimit = -1;

        private ThingOwner<Thing> contents;

        // Per-building storage limit chosen with the limit slider gizmo. FollowGlobalLimit (-1) means
        // "use the mod setting", any positive value is this building's own cap.
        private int storageLimit = FollowGlobalLimit;

        public CompProperties_RukayaActiveMatterReservoir Props => (CompProperties_RukayaActiveMatterReservoir)props;
        public int StoredContainers => contents?.TotalStackCount ?? 0;
        public int StorageLimit => storageLimit;
        public bool UsesCustomStorageLimit => storageLimit > 0;
        public int Capacity => UsesCustomStorageLimit
            ? storageLimit
            : RukayaRace.RukayaRaceSettings.GetReplicationReservoirCapacity(Props.capacity);
        public int FreeCapacity => UnityEngine.Mathf.Max(0, Capacity - StoredContainers);
        public bool IsFull => FreeCapacity <= 0;

        /// <summary>
        /// Global (mod setting) capacity shared by every terminal, used for display and as the
        /// default when this building follows the global setting.
        /// </summary>
        public int GlobalCapacity => RukayaRace.RukayaRaceSettings.GetReplicationReservoirCapacity(Props.capacity);

        public void SetStorageLimit(int value)
        {
            storageLimit = value <= 0
                ? FollowGlobalLimit
                : UnityEngine.Mathf.Clamp(value, 1, RukayaReplicationUtility.MaxReservoirCapacity);
        }

        public void ResetStorageLimit()
        {
            storageLimit = FollowGlobalLimit;
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            contents = new ThingOwner<Thing>(this, false);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            contents ??= new ThingOwner<Thing>(this, false);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (contents != null && contents.Count > 0)
            {
                // Reservoir contents are physical containers; dismantling, minifying, or destroying
                // the building must leave them recoverable on the map whenever a map still exists.
                if (previousMap != null)
                {
                    contents.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
                }
                else
                {
                    contents.ClearAndDestroyContents();
                }
            }

            base.PostDestroy(mode, previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storageLimit, "storageLimit", FollowGlobalLimit);
            Scribe_Deep.Look(ref contents, "contents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (contents == null)
                {
                    contents = new ThingOwner<Thing>(this, false);
                }

                // Saves written before the limit ceiling was lowered can hold a per-building limit far
                // above the current maximum. Nothing is destroyed here (stored containers stay put),
                // but the value is brought back into the range the slider can actually express.
                if (storageLimit > RukayaReplicationUtility.MaxReservoirCapacity)
                {
                    storageLimit = RukayaReplicationUtility.MaxReservoirCapacity;
                }
            }
        }

        public bool TryLoadFrom(Thing source, int requestedCount)
        {
            if (source == null || source.Destroyed || source.def != RukayaReplicationDefOf.Rukaya_ActiveMatterContainer || FreeCapacity <= 0)
            {
                return false;
            }

            int count = UnityEngine.Mathf.Min(UnityEngine.Mathf.Min(requestedCount, source.stackCount), FreeCapacity);
            if (count <= 0)
            {
                return false;
            }

            Thing moved = source.SplitOff(count);
            if (moved.Spawned)
            {
                moved.DeSpawn();
            }

            if (!contents.TryAdd(moved, true))
            {
                moved.Destroy();
                return false;
            }

            return true;
        }

        public bool TryLoadFromDetached(Thing moved)
        {
            if (moved == null || moved.Destroyed || moved.def != RukayaReplicationDefOf.Rukaya_ActiveMatterContainer || FreeCapacity <= 0)
            {
                return false;
            }

            if (moved.stackCount > FreeCapacity)
            {
                moved.stackCount = FreeCapacity;
            }

            if (!contents.TryAdd(moved, true))
            {
                moved.Destroy();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Moves as much of a container stack the pawn is currently carrying into the reservoir and
        /// returns the number of containers accepted. Thing.SplitOff already detaches the moved part
        /// (and removes the whole stack from the carry tracker when the entire stack fits), so the
        /// result can always be added directly.
        /// </summary>
        public int TryLoadFromCarried(Thing carried)
        {
            if (carried == null || carried.Destroyed || carried.def != RukayaReplicationDefOf.Rukaya_ActiveMatterContainer)
            {
                return 0;
            }

            int count = UnityEngine.Mathf.Min(carried.stackCount, FreeCapacity);
            if (count <= 0)
            {
                return 0;
            }

            Thing moved = carried.SplitOff(count);
            if (moved == null)
            {
                return 0;
            }

            if (moved.holdingOwner != null)
            {
                moved.holdingOwner.Remove(moved);
            }

            if (!contents.TryAdd(moved, true))
            {
                if (!moved.Destroyed)
                {
                    moved.Destroy();
                }

                return 0;
            }

            return count;
        }

        public bool TryConsume(int count)
        {
            if (count <= 0 || StoredContainers < count)
            {
                return false;
            }

            int remaining = count;
            for (int i = contents.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing stack = contents[i];
                int take = UnityEngine.Mathf.Min(remaining, stack.stackCount);
                if (take == stack.stackCount)
                {
                    contents.Remove(stack);
                    stack.Destroy();
                }
                else
                {
                    Thing spent = stack.SplitOff(take);
                    spent.Destroy();
                }

                remaining -= take;
            }

            return remaining == 0;
        }

        public bool TryRefund(int count)
        {
            if (count <= 0 || FreeCapacity < count)
            {
                return false;
            }

            int remaining = count;
            int stackLimit = UnityEngine.Mathf.Max(1, RukayaReplicationDefOf.Rukaya_ActiveMatterContainer.stackLimit);
            List<Thing> added = new List<Thing>();
            while (remaining > 0)
            {
                Thing refund = ThingMaker.MakeThing(RukayaReplicationDefOf.Rukaya_ActiveMatterContainer);
                int chunk = UnityEngine.Mathf.Min(stackLimit, remaining);
                refund.stackCount = chunk;
                if (!contents.TryAdd(refund, true))
                {
                    refund.Destroy();
                    for (int i = 0; i < added.Count; i++)
                    {
                        contents.Remove(added[i]);
                        added[i].Destroy();
                    }
                    return false;
                }

                added.Add(refund);
                remaining -= chunk;
            }

            return true;
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return contents;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
        }
    }
}
