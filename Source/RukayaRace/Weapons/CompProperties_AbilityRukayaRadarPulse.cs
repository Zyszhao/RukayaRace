using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompProperties_AbilityRukayaRadarPulse : CompProperties_AbilityEffect
    {
        public HediffDef lockOnHediff;
        public HediffDef exposedHediff;
        public int lockOnDurationTicks = 900;
        public int exposedDurationTicks = 600;
        public float exposeRadius = 5.9f;
        public float empDamageAmount = 8f;

        public CompProperties_AbilityRukayaRadarPulse()
        {
            this.compClass = typeof(CompAbilityEffect_RukayaRadarPulse);
        }
    }
}
