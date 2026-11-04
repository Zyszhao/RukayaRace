using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    /// <summary>
    /// Instant laser pulse for the Rukaya laser rifle (both normal and active-matter modes).
    /// The beam is drawn on the firing tick as one whole line from the launch origin to the
    /// projectile destination, which already carries the standard small per-shot random scatter,
    /// so successive pulses hit slightly different spots on the target. Projectile defs that also
    /// carry <see cref="RukayaBarrageSwayExtension"/> (the mech turret lasers) get an extra
    /// visual-only muzzle offset and endpoint sway; the shot still resolves at the real
    /// destination. No flying projectile or trailing streak is ever shown.
    /// </summary>
    public class Projectile_RukayaLaserPulse : Bullet
    {
        public override Vector3 ExactPosition
        {
            get
            {
                return this.destination + Vector3.up * this.def.Altitude;
            }
        }

        public override void Launch(
            Thing launcher,
            Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire = false,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            this.ScheduleLaserBeam();

            // Resolve the shot immediately at the destination so the impact matches the beam end.
            IntVec3 impactCell = this.destination.ToIntVec3();
            if (this.Map != null && impactCell.InBounds(this.Map))
            {
                base.Position = impactCell;
            }
            this.ImpactSomething();
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && hitThing != null && hitThing.CanEverAttachFire())
            {
                RukayaLaserBeamExtension extension = this.def.GetModExtension<RukayaLaserBeamExtension>();
                if (extension != null && extension.igniteChance > 0f && Rand.Chance(extension.igniteChance))
                {
                    hitThing.TryAttachFire(extension.fireSize, this.launcher);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ScheduleLaserBeam()
        {
            Map map = this.Map ?? this.launcher?.MapHeld;
            if (map == null)
            {
                return;
            }

            RukayaLaserBeamExtension extension = this.def.GetModExtension<RukayaLaserBeamExtension>();
            if (extension == null)
            {
                return;
            }

            // 纯视觉摆动：只改画出来的光束端点（起点前移到炮口并左右摆，终点反向摆），
            // 命中仍然用 this.destination 当场结算，真实落点/伤害不受影响。
            RukayaBarrageSway.BeamSway(this.def, this.launcher, this.origin, this.destination,
                Find.TickManager.TicksGame, out Vector3 beamOrigin, out Vector3 beamDestination);

            map.GetComponent<MapComponent_RukayaLaserLines>()?.ScheduleLaserLine(
                beamOrigin,
                beamDestination,
                extension.beamDurationTicks,
                extension.beamWidth,
                extension.beamGlowWidth,
                extension.beamColor,
                extension.impactGlowSize,
                extension.beamFadeFraction);
        }
    }
}
