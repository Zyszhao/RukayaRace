using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaExplosivePlasma : Projectile_Explosive
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
                if (hitThing != null)
                {
                    this.ApplyDirectDamage(hitThing);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplyDirectDamage(Thing hitThing)
        {
            RukayaPlasmaProjectileExtension extension = this.Extension;
            if (extension.directDamage <= 0f)
            {
                return;
            }

            DamageInfo damageInfo = new DamageInfo(
                extension.directDamageDef ?? this.DamageDef,
                extension.directDamage,
                extension.directArmorPenetration,
                this.ExactRotation.eulerAngles.y,
                this.launcher,
                null,
                this.equipmentDef ?? this.def,
                DamageInfo.SourceCategory.ThingOrUnknown,
                this.intendedTarget.Thing);
            hitThing.TakeDamage(damageInfo);
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
