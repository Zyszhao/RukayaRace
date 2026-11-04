using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaSuppressivePlasma : Projectile_RukayaExplosivePlasma
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && this.Map != null)
            {
                this.ApplySuppression(this.Position, this.Map, this.launcher as Pawn);
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplySuppression(IntVec3 center, Map map, Pawn launcherPawn)
        {
            RukayaSuppressionProjectileExtension extension = this.Extension;
            HediffDef hediffDef = extension.suppressionHediff;
            if (hediffDef == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || !pawn.Position.InHorDistOf(center, extension.suppressionRadius))
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

                existing.Severity = Mathf.Min(1f, existing.Severity + extension.suppressionSeverityPerHit);
                HediffComp_RukayaSuppression suppressionComp = existing.TryGetComp<HediffComp_RukayaSuppression>();
                suppressionComp?.NotifyRefreshed();
            }
        }

        private RukayaSuppressionProjectileExtension Extension =>
            this.def.GetModExtension<RukayaSuppressionProjectileExtension>() ?? new RukayaSuppressionProjectileExtension();
    }
}
