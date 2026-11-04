using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaSuppressionIncendiaryBullet : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Pawn launcherPawn = this.launcher as Pawn;
            IntVec3 impactCell = this.Position;
            Map map = this.Map;

            if (!blockedByShield)
            {
                if (hitThing != null && hitThing.CanEverAttachFire() && Rand.Chance(this.Extension.igniteChance))
                {
                    hitThing.TryAttachFire(this.Extension.fireSize, this.launcher);
                }

                if (map != null)
                {
                    this.ApplySuppression(impactCell, map, launcherPawn);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplySuppression(IntVec3 center, Map map, Pawn launcherPawn)
        {
            HediffDef hediffDef = this.Extension.suppressionHediff;
            if (hediffDef == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || !pawn.Position.InHorDistOf(center, this.Extension.suppressionRadius))
                {
                    continue;
                }

                if (launcherPawn != null && !launcherPawn.HostileTo(pawn))
                {
                    continue;
                }

                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
                if (existing == null)
                {
                    existing = HediffMaker.MakeHediff(hediffDef, pawn);
                    pawn.health.AddHediff(existing);
                }

                existing.Severity = Mathf.Min(1f, existing.Severity + this.Extension.suppressionSeverityPerHit);

                HediffComp_RukayaSuppression suppressionComp = existing.TryGetComp<HediffComp_RukayaSuppression>();
                suppressionComp?.NotifyRefreshed();
            }
        }

        private RukayaSuppressionProjectileExtension Extension =>
            this.def.GetModExtension<RukayaSuppressionProjectileExtension>() ?? new RukayaSuppressionProjectileExtension();
    }
}
