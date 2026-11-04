using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaArcShockProjectileExtension : DefModExtension
    {
        public float searchRadius = 5.9f;
        public int maxTargets = 10;
        public int directDamage = 20;
        public float directArmorPenetration = 0.18f;
        public float empDamage = 14f;
        public float lightningGlowSize = 1.1f;
        public int arcSegmentsPerTarget = 4;
        public float arcJitterRadius = 0.45f;
        public int arcGlowPointsPerSegment = 3;
    }
}
