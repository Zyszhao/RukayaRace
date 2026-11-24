using System.Collections.Generic;
using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaSuppressionIncendiaryBullet : Bullet
    {
        private static readonly RukayaSuppressionProjectileExtension DefaultExtension = new RukayaSuppressionProjectileExtension();
        private RukayaSuppressionProjectileExtension cachedExtension;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Pawn launcherPawn = this.launcher as Pawn;
            IntVec3 impactCell = this.Position;
            Map map = this.Map;

            if (!blockedByShield)
            {
                RukayaSuppressionProjectileExtension ext = this.Extension;
                if (hitThing != null && hitThing.CanEverAttachFire() && Rand.Chance(ext.igniteChance))
                {
                    hitThing.TryAttachFire(ext.fireSize, this.launcher);
                }

                if (map != null)
                {
                    this.ApplySuppression(impactCell, map, launcherPawn, ext);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplySuppression(IntVec3 center, Map map, Pawn launcherPawn, RukayaSuppressionProjectileExtension ext)
        {
            HediffDef hediffDef = ext.suppressionHediff;
            if (hediffDef == null || ext.suppressionRadius <= 0f)
            {
                return;
            }

            int numCells = GenRadial.NumCellsInRadius(ext.suppressionRadius);
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

                    existing.Severity = Mathf.Min(1f, existing.Severity + ext.suppressionSeverityPerHit);

                    HediffComp_RukayaSuppression suppressionComp = existing.TryGetComp<HediffComp_RukayaSuppression>();
                    suppressionComp?.NotifyRefreshed();
                }
            }
        }

        private RukayaSuppressionProjectileExtension Extension =>
            cachedExtension ?? (cachedExtension = this.def.GetModExtension<RukayaSuppressionProjectileExtension>() ?? DefaultExtension);
    }
}
