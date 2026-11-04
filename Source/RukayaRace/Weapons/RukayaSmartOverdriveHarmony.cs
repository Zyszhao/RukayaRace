using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    // The two remaining Harmony patches for the Rukaya overdrive sidearms
    // (Rukaya_SmartRevolver, Rukaya_Pistol, Rukaya_SubMachinegun). Both scale values that are
    // computed deep inside VerbProperties (non-virtual methods on the shared def-level props object),
    // so the engine exposes no subclass seam for them:
    // - Head-lock lives in DamageWorker_RukayaSmartShot (native DamageWorker override, no patch).
    // - Warmup and range boosts live in Verb_RukayaOverchargeShoot (native verb overrides, no patch).
    // 1) Accuracy: while active matter mode is on with charge available, the equipment accuracy factor
    //    is boosted by activeAccuracyFactor (clamped to 1).
    // 2) Cooldown: while active matter mode is on with charge available, the cycle cooldown is scaled
    //    by activeCooldownMultiplier (values below 1 speed the firing cycle up).

    /// <summary>
    /// Postfix on VerbProperties.GetHitChanceFactor: while the equipped weapon is an active-matter weapon
    /// in active mode with charge available and an activeAccuracyFactor configured, scale the equipment
    /// accuracy factor up (clamped to 1). This is the charge-mode accuracy part of the overdrive sidearms.
    /// </summary>
    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.GetHitChanceFactor))]
    public static class Patch_VerbProperties_GetHitChanceFactor_ActiveAccuracy
    {
        public static void Postfix(Thing equipment, ref float __result)
        {
            if (equipment is not ThingWithComps thingWithComps)
            {
                return;
            }

            CompRukayaActiveMatterWeapon weaponComp =
                thingWithComps.GetComp<CompRukayaActiveMatterWeapon>();
            if (weaponComp == null || !weaponComp.ActiveMode || !weaponComp.CanUseActiveModeNow())
            {
                return;
            }

            float factor = weaponComp.Props.activeAccuracyFactor;
            if (factor <= 1f)
            {
                return;
            }

            __result = Mathf.Clamp01(__result * factor);
        }
    }

    /// <summary>
    /// Postfix on VerbProperties.AdjustedCooldown (verb/pawn overload): while the equipped weapon is an
    /// active-matter weapon in active mode with charge available and an activeCooldownMultiplier configured,
    /// scale the per-cycle cooldown down. This is the charge-mode rate-of-fire part of the overdrive sidearms.
    /// </summary>
    [HarmonyPatch(typeof(VerbProperties), "AdjustedCooldown",
        new Type[] { typeof(Verb), typeof(Pawn) })]
    public static class Patch_VerbProperties_AdjustedCooldown_ActiveOvercharge
    {
        public static void Postfix(Verb ownerVerb, ref float __result)
        {
            ThingWithComps equipment = ownerVerb?.EquipmentSource;
            if (equipment == null)
            {
                return;
            }

            CompRukayaActiveMatterWeapon weaponComp =
                equipment.GetComp<CompRukayaActiveMatterWeapon>();
            if (weaponComp == null || !weaponComp.ActiveMode || !weaponComp.CanUseActiveModeNow())
            {
                return;
            }

            float multiplier = weaponComp.Props.activeCooldownMultiplier;
            if (multiplier <= 0f || multiplier == 1f)
            {
                return;
            }

            __result *= multiplier;
        }
    }
}
