// Ported from RukayaRace airburst rockets; standalone, without a parent-mod dependency.
using Verse;

namespace RukayaAviation
{
    public class ClusterProjectileExtension : DefModExtension
    {
        public float directDamage;
        public DamageDef directDamageDef;
        public float directArmorPenetration;
        public ThingDef fragmentProjectile;
        public int fragmentCount = 5;
        public float fragmentSearchRadius = 5f;
        public bool preferPawnCells = true;
        public int fragmentVisualSegments = 3;
        public float fragmentVisualArcHeight;
        public float fragmentLaunchDelayMaxSeconds;
    }
}
