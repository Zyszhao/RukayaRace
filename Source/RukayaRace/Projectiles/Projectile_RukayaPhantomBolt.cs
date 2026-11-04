using RimWorld;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaPhantomBolt : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && hitThing is Pawn pawn && pawn.RaceProps.IsFlesh)
            {
                RukayaPhantomProjectileExtension extension = this.Extension;
                if (extension.hediffDef != null)
                {
                    Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(extension.hediffDef);
                    if (existing != null)
                    {
                        existing.Severity += extension.severity;
                    }
                    else
                    {
                        Hediff hediff = pawn.health.AddHediff(extension.hediffDef);
                        if (hediff != null)
                        {
                            hediff.Severity = extension.severity;
                        }
                    }
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        private RukayaPhantomProjectileExtension Extension =>
            this.def.GetModExtension<RukayaPhantomProjectileExtension>() ?? new RukayaPhantomProjectileExtension();
    }
}
