using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaSuppressionProjectileExtension : RukayaPlasmaProjectileExtension
    {
        public float fireSize = 0.75f;
        public float igniteChance = 0.3f;
        public float suppressionRadius = 2.5f;
        public HediffDef suppressionHediff;
        public float suppressionSeverityPerHit = 0.06666667f;
    }
}
