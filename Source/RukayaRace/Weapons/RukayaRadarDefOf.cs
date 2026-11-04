using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    [DefOf]
    public static class RukayaRadarDefOf
    {
        public static HediffDef Rukaya_RadarLockOn;
        public static HediffDef Rukaya_RadarExposed;
        public static HediffDef Rukaya_ATGMGuiding;

        static RukayaRadarDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaRadarDefOf));
        }
    }
}
