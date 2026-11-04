using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class MapComponent_RukayaAirburstFragments : MapComponent
    {
        private List<PendingAirburstFragment> pendingFragments = new List<PendingAirburstFragment>();

        public MapComponent_RukayaAirburstFragments(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int currentTick = Find.TickManager.TicksGame;
            for (int i = this.pendingFragments.Count - 1; i >= 0; i--)
            {
                PendingAirburstFragment pendingFragment = this.pendingFragments[i];
                if (pendingFragment.launchTick > currentTick)
                {
                    continue;
                }

                this.pendingFragments.RemoveAt(i);
                pendingFragment.Launch(this.map);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref this.pendingFragments, "pendingFragments", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && this.pendingFragments == null)
            {
                this.pendingFragments = new List<PendingAirburstFragment>();
            }
        }

        public void Schedule(
            IntVec3 originCell,
            IntVec3 targetCell,
            LocalTargetInfo target,
            ThingDef projectileDef,
            Thing launcher,
            bool preventFriendlyFire,
            Thing equipment,
            int visualSegments,
            float visualArcHeight,
            int delayTicks)
        {
            this.pendingFragments.Add(new PendingAirburstFragment
            {
                launchTick = Find.TickManager.TicksGame + delayTicks,
                originCell = originCell,
                targetCell = targetCell,
                target = target,
                projectileDef = projectileDef,
                launcher = launcher,
                preventFriendlyFire = preventFriendlyFire,
                equipment = equipment,
                visualSegments = visualSegments,
                visualArcHeight = visualArcHeight
            });
        }
    }

    public class PendingAirburstFragment : IExposable
    {
        public int launchTick;
        public IntVec3 originCell;
        public IntVec3 targetCell;
        public LocalTargetInfo target;
        public ThingDef projectileDef;
        public Thing launcher;
        public bool preventFriendlyFire;
        public Thing equipment;
        public int visualSegments;
        public float visualArcHeight;

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.launchTick, "launchTick");
            Scribe_Values.Look(ref this.originCell, "originCell");
            Scribe_Values.Look(ref this.targetCell, "targetCell");
            Scribe_TargetInfo.Look(ref this.target, "target");
            Scribe_Defs.Look(ref this.projectileDef, "projectileDef");
            Scribe_References.Look(ref this.launcher, "launcher");
            Scribe_Values.Look(ref this.preventFriendlyFire, "preventFriendlyFire", false);
            Scribe_References.Look(ref this.equipment, "equipment");
            Scribe_Values.Look(ref this.visualSegments, "visualSegments");
            Scribe_Values.Look(ref this.visualArcHeight, "visualArcHeight");
        }

        public void Launch(Map map)
        {
            if (map == null || this.projectileDef == null || !this.originCell.InBounds(map))
            {
                return;
            }

            LocalTargetInfo launchTarget = this.target;
            Thing targetThing = launchTarget.Thing;
            if (!launchTarget.IsValid || (targetThing != null && (!targetThing.Spawned || targetThing.Destroyed || targetThing.Map != map)))
            {
                launchTarget = this.targetCell;
            }

            Projectile_RukayaAirburstRocket.ThrowFragmentVisuals(
                this.originCell.ToVector3Shifted(),
                this.targetCell.ToVector3Shifted(),
                map,
                this.visualSegments,
                this.visualArcHeight);
            Projectile_RukayaAirburstRocket.LaunchFragmentProjectile(
                this.originCell,
                launchTarget,
                map,
                this.projectileDef,
                this.launcher,
                this.preventFriendlyFire,
                this.equipment);
        }
    }
}
