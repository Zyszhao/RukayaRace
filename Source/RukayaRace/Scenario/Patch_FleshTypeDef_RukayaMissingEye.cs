using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    // Keep the painted eyes on Rukaya faces; missing eyes still affect health and sight normally.
    [HarmonyPatch(typeof(FleshTypeDef), nameof(FleshTypeDef.ChooseWoundOverlay))]
    public static class Patch_FleshTypeDef_RukayaMissingEye
    {
        public static bool Prefix(Hediff hediff, ref FleshTypeDef.ResolvedWound __result)
        {
            if (hediff is Hediff_MissingPart
                && hediff.pawn?.def?.defName == "Alien_Rukaya"
                && hediff.Part?.def == BodyPartDefOf.Eye)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }
}
