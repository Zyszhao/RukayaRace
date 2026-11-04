using RimWorld;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaIgnitingBullet : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && hitThing != null && hitThing.CanEverAttachFire())
            {
                hitThing.TryAttachFire(this.Extension.fireSize, this.launcher);
            }

            base.Impact(hitThing, blockedByShield);
        }

        private RukayaIgnitingProjectileExtension Extension =>
            this.def.GetModExtension<RukayaIgnitingProjectileExtension>() ?? new RukayaIgnitingProjectileExtension();
    }
}
