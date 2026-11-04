using RimWorld;
using Verse;

namespace RukayaRace.Replication
{
    [DefOf]
    public static class RukayaReplicationDefOf
    {
        public static ThingDef Rukaya_ActiveMatterContainer;
        public static ThingDef Rukaya_TriplexScanner;
        public static ThingDef Rukaya_ActiveMatterPrinter;
        public static JobDef Rukaya_LoadActiveMatterReservoir;

        static RukayaReplicationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaReplicationDefOf));
        }
    }
}
