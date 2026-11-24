using System.Collections.Generic;
using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaSuppressivePlasma : Projectile_RukayaExplosivePlasma
    {
        private static readonly RukayaSuppressionProjectileExtension DefaultSuppressionExtension = new RukayaSuppressionProjectileExtension();
        private RukayaSuppressionProjectileExtension cachedSuppressionExtension;

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
            RukayaSuppressionProjectileExtension extension = this.SuppressionExtension;
            HediffDef hediffDef = extension.suppressionHediff;
            if (hediffDef == null || extension.suppressionRadius <= 0f)
            {
                return;
            }

            int numCells = GenRadial.NumCellsInRadius(extension.suppressionRadius);
            for (int c = 0; c < numCells; c++)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[c];
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (!(things[i] is Pawn pawn) || pawn.Dead)
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
        }

        private RukayaSuppressionProjectileExtension SuppressionExtension =>
            cachedSuppressionExtension ?? (cachedSuppressionExtension = this.def.GetModExtension<RukayaSuppressionProjectileExtension>() ?? DefaultSuppressionExtension);
    }
}
