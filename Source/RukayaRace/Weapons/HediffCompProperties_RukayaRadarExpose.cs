using Verse;

namespace RukayaRace.Weapons
{
    public class HediffCompProperties_RukayaRadarExpose : HediffCompProperties
    {
        public int disruptIntervalTicks = 30;

        public HediffCompProperties_RukayaRadarExpose()
        {
            this.compClass = typeof(HediffComp_RukayaRadarExpose);
        }
    }
}
