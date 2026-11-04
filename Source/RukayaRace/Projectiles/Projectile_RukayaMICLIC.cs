using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaMICLIC : Projectile_Explosive
    {
        private int launchTick;
        private int flightTicks;
        private int flightTicksTotal;
        private int firstRelease;
        private int releaseCount;
        private int released;
        private float releaseInterval;
        private int detonationTick;
        private List<Projectile_RukayaMICLICCharge> charges = new List<Projectile_RukayaMICLICCharge>();

        public override int UpdateRateTicks => 1;
        private RukayaMICLICProjectileExtension Extension => def.GetModExtension<RukayaMICLICProjectileExtension>();

        public Vector3 RopePosition => base.ExactPosition + Vector3.forward * CurrentHeight;
        private float CurrentHeight => landed ? 0f
            : Extension.mainArcHeight * GenMath.InverseParabola(Mathf.Clamp01((float)flightTicks / flightTicksTotal));

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false,
            Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            launchTick = Find.TickManager.TicksGame;
            flightTicksTotal = Mathf.Max(1, Mathf.CeilToInt(StartingTicksToImpact));
            float distance = (destination - this.origin).MagnitudeHorizontal();
            firstRelease = Mathf.Max(1, Mathf.CeilToInt(flightTicksTotal
                * (Mathf.Max(6f, Extension.firstChargeDistance) + 0.3f) / Mathf.Max(0.1f, distance)) + 1);
            int lastRelease = flightTicksTotal - Mathf.Max(1, Extension.tailTicks);
            int span = lastRelease - firstRelease;
            releaseCount = span < 0 ? 0 : Mathf.Min(Mathf.Max(1, Extension.maxCharges),
                span / Mathf.Max(1, Extension.minChargeIntervalTicks) + 1);
            releaseInterval = releaseCount <= 1 ? 0f : span / (float)(releaseCount - 1);
            // Every charge carries a common absolute barrier, independent of a live
            // head or a shared map component. Concurrent shots cannot share a chain.
            detonationTick = launchTick + flightTicksTotal + Mathf.Max(1, Extension.chargeFallTicks) + 2
                + releaseCount * Mathf.Max(1, Extension.detonationIntervalTicks);
        }

        protected override void TickInterval(int delta)
        {
            if (landed)
            {
                if (Find.TickManager.TicksGame >= detonationTick)
                    Explode();
                return;
            }

            flightTicks += delta;
            while (released < releaseCount
                && flightTicks >= firstRelease + Mathf.RoundToInt(releaseInterval * released))
            {
                ReleaseCharge();
                released++;
            }
            base.TickInterval(delta);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (blockedByShield)
            {
                Destroy();
                return;
            }
            // Wait on the ground; the head must not start the chain at the far end.
            landed = true;
        }

        private void ReleaseCharge()
        {
            Vector3 forward = (destination - origin).Yto0().normalized;
            Vector3 start = base.ExactPosition.Yto0() - forward * 0.3f;
            IntVec3 cell = start.ToIntVec3();
            if (!cell.InBounds(Map) || Extension.chargeProjectile == null)
                return;

            Projectile_RukayaMICLICCharge charge = (Projectile_RukayaMICLICCharge)
                GenSpawn.Spawn(Extension.chargeProjectile, cell, Map);
            LocalTargetInfo target = new LocalTargetInfo(cell);
            charge.Launch(launcher, start, target, target, ProjectileHitFlags.None, preventFriendlyFire, equipment);
            int chargeDetonationTick = launchTick + flightTicksTotal + Mathf.Max(1, Extension.chargeFallTicks) + 2
                + released * Mathf.Max(1, Extension.detonationIntervalTicks);
            charge.Initialize(start, start + forward * Mathf.Max(0f, Extension.chargeForwardDrift),
                CurrentHeight, Extension.chargeFallTicks, chargeDetonationTick,
                forward.AngleFlat(), charges.Count > 0 ? charges[charges.Count - 1] : null);
            charges.Add(charge);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector3 head = RopePosition;
            if (!landed)
                RukayaRocketExhaustUtility.Draw(head, ExactRotation, def.graphicData.drawSize.y);
            for (int i = charges.Count - 1; i >= 0; i--)
            {
                Projectile_RukayaMICLICCharge charge = charges[i];
                if (charge != null && charge.Spawned)
                {
                    Projectile_RukayaMICLICCharge.DrawRope(head, charge.RopePosition);
                    break;
                }
            }
            base.DrawAt(head, flip);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref launchTick, "miclicLaunchTick");
            Scribe_Values.Look(ref flightTicks, "miclicFlightTicks");
            Scribe_Values.Look(ref flightTicksTotal, "miclicFlightTicksTotal");
            Scribe_Values.Look(ref firstRelease, "miclicFirstRelease");
            Scribe_Values.Look(ref releaseCount, "miclicReleaseCount");
            Scribe_Values.Look(ref released, "miclicReleased");
            Scribe_Values.Look(ref releaseInterval, "miclicReleaseInterval");
            Scribe_Values.Look(ref detonationTick, "miclicDetonationTick");
            Scribe_Collections.Look(ref charges, "miclicCharges", LookMode.Reference);
        }
    }
}
