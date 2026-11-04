using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaPlasma : Bullet
    {
        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (!this.Spawned || this.landed || this.Map == null)
            {
                return;
            }

            RukayaPlasmaProjectileExtension extension = this.Extension;
            if (extension.glowIntervalTicks > 0 && this.HashOffsetTicks() % extension.glowIntervalTicks < delta)
            {
                this.ThrowCyanGlow(extension);
            }

        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield)
            {
                RukayaPlasmaProjectileUtility.ThrowImpactFlash(this.ExactPosition, this.Map, this.Extension.impactGlowSize);
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ThrowCyanGlow(RukayaPlasmaProjectileExtension extension)
        {
            FleckDef glowDef = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
            if (glowDef == null)
            {
                FleckMaker.ThrowLightningGlow(this.ExactPosition, this.Map, extension.glowSize);
                return;
            }

            FleckCreationData data = FleckMaker.GetDataStatic(this.ExactPosition, this.Map, glowDef, extension.glowSize);
            data.instanceColor = extension.glowColor;
            this.Map.flecks.CreateFleck(data);
        }

        private RukayaPlasmaProjectileExtension Extension =>
            this.def.GetModExtension<RukayaPlasmaProjectileExtension>() ?? new RukayaPlasmaProjectileExtension();
    }
}
