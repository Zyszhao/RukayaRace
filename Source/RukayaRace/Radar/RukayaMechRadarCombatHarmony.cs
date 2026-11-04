using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Radar
{
    internal static class RukayaMechRadarCombatUtility
    {
        internal static bool IsNonCombatRadar(Pawn pawn)
        {
            return pawn?.kindDef?.canMeleeAttack == false
                && pawn.def.HasComp(typeof(CompRukayaMechRadar));
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "ShouldUseMeleeAttackGizmo")]
    public static class Patch_PawnAttackGizmoUtility_ShouldUseMeleeAttackGizmo_RukayaRadar
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            // 原版仅检查征召与所选武器，不检查 PawnKindDef.canMeleeAttack。
            if (__result && RukayaMechRadarCombatUtility.IsNonCombatRadar(pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetMeleeAttackAction))]
    public static class Patch_FloatMenuUtility_GetMeleeAttackAction_RukayaRadar
    {
        public static bool Prefix(Pawn pawn, ref string failStr, ref Action __result)
        {
            if (!RukayaMechRadarCombatUtility.IsNonCombatRadar(pawn))
            {
                return true;
            }

            // 混合编队的攻击按钮会遍历所有征召单位；必须在查询空近战列表前拒绝。
            failStr = "Incapable".Translate();
            __result = null;
            return false;
        }
    }
}
