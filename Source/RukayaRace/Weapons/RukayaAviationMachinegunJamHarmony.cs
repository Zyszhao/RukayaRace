using HarmonyLib;
using Verse;

namespace RukayaRace.Weapons
{
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot_AviationMachinegunJam
    {
        public static void Postfix(Verb __instance)
        {
            if (__instance is Verb_RukayaAviationMachinegunShoot aviationMachinegunVerb)
            {
                aviationMachinegunVerb.ApplyPendingJamCooldown();
            }
        }
    }
}
