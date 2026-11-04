using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public static class RukayaRadarUtility
    {
        private static readonly FieldInfo ShotReportTargetField = AccessTools.Field(typeof(ShotReport), "target");
        private static readonly FieldInfo ShotReportGasField = AccessTools.Field(typeof(ShotReport), "factorFromCoveringGas");

        public static bool TryGetTargetingData(Pawn pawn, out float aimOnTargetChanceFactor, out bool ignoreBlindSmoke)
        {
            aimOnTargetChanceFactor = 1f;
            ignoreBlindSmoke = false;
            if (pawn == null || pawn.health?.hediffSet == null)
            {
                return false;
            }

            bool found = false;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                RukayaRadarTargetingExtension extension = hediffs[i].def.GetModExtension<RukayaRadarTargetingExtension>();
                if (extension == null)
                {
                    continue;
                }

                found = true;
                aimOnTargetChanceFactor = Mathf.Max(aimOnTargetChanceFactor, extension.aimOnTargetChanceFactor);
                ignoreBlindSmoke |= extension.ignoreBlindSmoke;
            }

            return found;
        }

        public static Pawn GetShotReportPawn(ShotReport report)
        {
            if (ShotReportTargetField == null)
            {
                return null;
            }

            object boxed = report;
            TargetInfo targetInfo = (TargetInfo)ShotReportTargetField.GetValue(boxed);
            return targetInfo.Thing as Pawn;
        }

        public static float GetCoveringGasFactor(ShotReport report)
        {
            if (ShotReportGasField == null)
            {
                return 1f;
            }

            object boxed = report;
            return (float)ShotReportGasField.GetValue(boxed);
        }

        public static bool CanSeeIgnoringBlindSmoke(Thing seer, Thing target)
        {
            if (seer == null || target == null || seer.Map != target.Map || seer.Map == null)
            {
                return false;
            }

            List<IntVec3> destCells = new List<IntVec3>();
            ShootLeanUtility.CalcShootableCellsOf(destCells, target, seer.Position);
            for (int i = 0; i < destCells.Count; i++)
            {
                if (GenSight.LineOfSight(seer.Position, destCells[i], seer.Map, true, null, 0, 0))
                {
                    return true;
                }
            }

            List<IntVec3> sourceCells = new List<IntVec3>();
            ShootLeanUtility.LeanShootingSourcesFromTo(seer.Position, target.Position, seer.Map, sourceCells);
            for (int i = 0; i < sourceCells.Count; i++)
            {
                if (!sourceCells[i].CanBeSeenOver(seer.Map))
                {
                    continue;
                }

                for (int j = 0; j < destCells.Count; j++)
                {
                    if (GenSight.LineOfSight(sourceCells[i], destCells[j], seer.Map, true, null, 0, 0))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimOnTargetChance_StandardTarget
    {
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            Pawn targetPawn = RukayaRadarUtility.GetShotReportPawn(__instance);
            if (!RukayaRadarUtility.TryGetTargetingData(targetPawn, out float aimFactor, out bool ignoreBlindSmoke))
            {
                return;
            }

            if (ignoreBlindSmoke)
            {
                float gasFactor = RukayaRadarUtility.GetCoveringGasFactor(__instance);
                if (gasFactor > 0f && gasFactor < 0.999f)
                {
                    __result /= gasFactor;
                }
            }

            if (aimFactor > 1f)
            {
                __result *= aimFactor;
            }

            __result = Mathf.Clamp(__result, 0.0201f, 1f);
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee))]
    public static class Patch_AttackTargetFinder_CanSee
    {
        public static bool Prefix(Thing seer, Thing target, Func<IntVec3, bool> validator, ref bool __result)
        {
            if (validator == null)
            {
                return true;
            }

            Pawn targetPawn = target as Pawn;
            if (!RukayaRadarUtility.TryGetTargetingData(targetPawn, out _, out bool ignoreBlindSmoke) || !ignoreBlindSmoke)
            {
                return true;
            }

            if (RukayaRadarUtility.CanSeeIgnoringBlindSmoke(seer, target))
            {
                __result = true;
                return false;
            }

            return true;
        }
    }
}
