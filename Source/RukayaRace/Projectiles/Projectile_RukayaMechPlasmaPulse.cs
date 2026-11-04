using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    // Keep the sniper's direct-hit damage separate from its impact explosion visual.
    public class Projectile_RukayaMechPlasmaPulse : Projectile_RukayaLaserPulse
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && Map != null && Position.InBounds(Map))
            {
                Vector3 impact = ExactPosition;
                FleckCreationData flash = FleckMaker.GetDataStatic(impact, Map, FleckDefOf.ExplosionFlash, 3f);
                flash.instanceColor = new Color(0.55f, 0.9f, 1f, 1f);
                Map.flecks.CreateFleck(flash);
                RukayaPlasmaProjectileUtility.ThrowImpactFlash(impact, Map, 3f);
                FleckMaker.ThrowMicroSparks(impact, Map);
            }
            base.Impact(hitThing, blockedByShield);
        }
    }
}
