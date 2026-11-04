using HarmonyLib;
using Verse;

namespace RukayaRace.Weapons
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryStartAttack))]
    public static class Patch_Pawn_TryStartAttack_RukayaActiveMatterAI
    {
        public static void Prefix(Pawn __instance, LocalTargetInfo targ)
        {
            if (!RukayaActiveMatterAIUtility.IsAutonomousPawn(__instance)
                || __instance.stances?.FullBodyBusy == true
                || !RukayaActiveMatterAIUtility.IsValidHostileTarget(__instance, targ.Thing))
            {
                return;
            }

            CompRukayaActiveMatterWeapon weapon =
                __instance.equipment?.Primary?.GetComp<CompRukayaActiveMatterWeapon>();
            if (weapon == null || !weapon.HasRangedProjectile
                || weapon.Props.aiModePolicy == RukayaActiveMatterAIPolicy.Never)
            {
                return;
            }

            weapon.InitializeAISupplyIfNeeded(__instance);
            bool shouldUse = RukayaActiveMatterAIUtility.ShouldUseActiveMode(__instance, targ.Thing, weapon);
            string reason = shouldUse
                ? "Rukaya_ActiveMatterAI_Active"
                : !weapon.CanUseActiveModeNow()
                    ? "Rukaya_ActiveMatterAI_Insufficient"
                    : !RukayaActiveMatterAIUtility.HasSafeImpactArea(__instance, targ.Thing, weapon)
                        ? "Rukaya_ActiveMatterAI_FriendlyRisk"
                        : "Rukaya_ActiveMatterAI_PolicyNotMet";
            weapon.SetAIMode(shouldUse, reason);
        }
    }
}
