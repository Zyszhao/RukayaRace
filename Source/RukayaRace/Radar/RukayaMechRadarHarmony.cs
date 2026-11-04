using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Radar
{
    /// <summary>
    /// 接口 3：以雷达为中心扩展"同一机械师所属机械"的指挥范围。
    ///
    /// 原版 Pawn_MechanitorTracker.CanCommandTo(LocalTargetInfo) 只允许距离机械师
    /// DistanceToSquared &lt; 620.01（≈24.9 格）的目标。本补丁在原版判定失败后，
    /// 再在机械师控制组（ControlGroup/AssignedMechs）中寻找"同属该机械师的雷达车"，
    /// 若目标落在某辆正常工作的雷达的 commandRelayRangeCells 内，则允许指挥。
    ///
    /// 约束：
    /// - 只读取原版方法的结果（__result），不递归调用 CanCommandTo。
    /// - 不修改机械师绑定、不修改控制组、不改写原版距离。
    /// - 只对"同一机械师"控制的雷达生效，因此不会借助别人的雷达扩展范围，
    ///   也不会出现"雷达给雷达中继"的无限扩散（雷达命令范围仍以自身为中心）。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanCommandTo))]
    public static class Patch_Pawn_MechanitorTracker_CanCommandTo
    {
        private static List<Pawn> tmpMechs = new List<Pawn>();

        public static void Postfix(Pawn_MechanitorTracker __instance, LocalTargetInfo target, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            Pawn overseer = __instance.Pawn;
            if (overseer == null || !overseer.Spawned || overseer.mechanitor != __instance || !target.IsValid)
            {
                return;
            }

            IntVec3 targetCell = target.Cell;
            if (!targetCell.InBounds(overseer.MapHeld)
                || (target.HasThing && target.Thing.MapHeld != overseer.MapHeld))
            {
                return;
            }

            tmpMechs.Clear();
            MechanitorUtility.GetMechsInAssignedOrder(overseer, ref tmpMechs);
            bool relayed = false;

            for (int i = 0; i < tmpMechs.Count; i++)
            {
                Pawn mech = tmpMechs[i];
                if (mech == null || mech.Dead || mech.Destroyed || !mech.Spawned || mech.MapHeld == null)
                {
                    continue;
                }

                if (!mech.def.HasComp(typeof(CompRukayaMechRadar)))
                {
                    continue;
                }

                if (mech.GetOverseer() != overseer)
                {
                    continue;
                }

                CompRukayaMechRadar radar = mech.TryGetComp<CompRukayaMechRadar>();
                if (radar == null)
                {
                    continue;
                }

                if (radar.CanRelayCommand(mech, overseer, targetCell))
                {
                    relayed = true;
                    break;
                }
            }

            tmpMechs.Clear();

            if (relayed)
            {
                __result = true;
            }
        }
    }
}
