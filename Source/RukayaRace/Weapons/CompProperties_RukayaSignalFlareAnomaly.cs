using Verse;

namespace RukayaRace.Weapons
{
    public class CompProperties_RukayaSignalFlareAnomaly : CompProperties
    {
        public float radius = 8.9f;

        public CompProperties_RukayaSignalFlareAnomaly()
        {
            this.compClass = typeof(CompRukayaSignalFlareAnomaly);
        }
    }
}
