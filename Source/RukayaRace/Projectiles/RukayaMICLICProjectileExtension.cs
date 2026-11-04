using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaMICLICProjectileExtension : DefModExtension
    {
        public ThingDef chargeProjectile;
        public float mainArcHeight = 2.2f;
        public int chargeFallTicks = 9;
        public float chargeForwardDrift = 1.2f;
        public float firstChargeDistance = 6f;
        public int maxCharges = 9;
        public int minChargeIntervalTicks = 3;
        public int tailTicks = 2;
        public int detonationIntervalTicks = 3;
    }
}
