using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    [DefOf]
    public static class RukayaActiveMatterDefOf
    {
        public static JobDef Rukaya_ReloadActiveMatterWeapon;

        static RukayaActiveMatterDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaActiveMatterDefOf));
        }
    }
}
