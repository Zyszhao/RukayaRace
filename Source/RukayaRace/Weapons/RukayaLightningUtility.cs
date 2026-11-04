using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public static class RukayaLightningUtility
    {
        public static bool ShouldUseLightningForElectricWeapons =>
            RukayaRaceMod.Settings?.useLightningForElectricWeapons ?? false;

        public static void Strike(IntVec3 cell, Map map)
        {
            if (map == null || !cell.IsValid)
            {
                return;
            }

            Mesh boltMesh = null;
            WeatherEvent_LightningStrike.DoStrike(cell, map, ref boltMesh);
        }
    }
}
