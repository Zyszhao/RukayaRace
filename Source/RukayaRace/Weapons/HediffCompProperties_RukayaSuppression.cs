using Verse;

namespace RukayaRace.Weapons
{
    public class HediffCompProperties_RukayaSuppression : HediffCompProperties
    {
        public int decayDelayTicks = 300;
        public float severityDecayPerTick = 0.00083333335f;

        public HediffCompProperties_RukayaSuppression()
        {
            this.compClass = typeof(HediffComp_RukayaSuppression);
        }
    }
}
