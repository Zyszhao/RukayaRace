using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Radar
{
    /// <summary>
    /// 机械雷达的核心 comp：负责 1. 广域弱化射击指挥光环、2. 强化雷达标记，
    /// 并为 3. 雷达中继指挥范围（Harmony 补丁）提供判定接口。
    ///
    /// 设计要点：
    /// - 按 auraRefreshIntervalTicks / markRefreshIntervalTicks 周期刷新；已有 buff 只把剩余时间抬回时长，
    ///   离开范围后最多 auraDurationTicks / markDurationTicks 自然结束，不存在无限中继。
    /// - 只在雷达自身正常工作时生效：已刷出、未死、未倒地、非精神错乱、未休眠、未眩晕、（可选）有余能。
    /// - 光环作用于同阵营和盟友中可正常行动的单位；标记只作用于敌对单位，不发 EMP、不消耗能量。
    /// - 不做二次中继：不读取其它雷达的状态，被标记单位也不会继续向外扩散标记。
    /// - 只有直接命中的单位受影响，不借助 HediffComp_Link 等原版机制延长范围。
    /// </summary>
    public class CompRukayaMechRadar : ThingComp
    {
        public CompProperties_RukayaMechRadar Props => (CompProperties_RukayaMechRadar)this.props;

        /// <summary>雷达自身是否处于可工作状态。</summary>
        public bool CanOperateRadar()
        {
            Pawn radar = this.parent as Pawn;
            if (radar == null || !radar.Spawned || radar.Dead || radar.Destroyed || radar.Downed || radar.Faction == null)
            {
                return false;
            }

            if (this.Props.requireAwake && !radar.Awake())
            {
                return false;
            }

            if (radar.InMentalState || radar.MentalStateDef != null)
            {
                return false;
            }

            // 休眠：带 CompCanBeDormant 的单位由该 comp 判定
            CompCanBeDormant dormancy = radar.TryGetComp<CompCanBeDormant>();
            if (dormancy != null && !dormancy.Awake)
            {
                return false;
            }

            // EMP 等眩晕由 stance tracker 的 StunHandler 维护，并非名为 Stun 的 Hediff。
            if (radar.stances?.stunner?.Stunned == true)
            {
                return false;
            }

            if (this.Props.requireEnergy && radar.needs != null && radar.needs.energy != null
                && radar.needs.energy.CurLevelPercentage <= this.Props.minEnergyPercent)
            {
                return false;
            }

            return true;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            Pawn radar = this.parent as Pawn;
            if (radar == null || !radar.Spawned)
            {
                return;
            }

            if (!this.CanOperateRadar())
            {
                return;
            }

            Map map = radar.MapHeld;
            if (map == null)
            {
                return;
            }

            int auraInterval = Mathf.Max(1, this.Props.auraRefreshIntervalTicks);
            if (radar.IsHashIntervalTick(auraInterval, delta))
            {
                this.RefreshAura(radar, map);
            }

            int markInterval = Mathf.Max(1, this.Props.markRefreshIntervalTicks);
            if (radar.IsHashIntervalTick(markInterval, delta))
            {
                this.RefreshMarks(radar, map);
            }
        }

        // -----------------------------------------------------------------
        // 1. 广域弱化射击指挥光环
        // -----------------------------------------------------------------
        private static bool IsAuraTarget(Pawn radar, Pawn pawn)
        {
            if (pawn == null || pawn == radar || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.health == null)
            {
                return false;
            }

            // 同阵营与盟友均可获益，中立访客及无阵营野生动物不算友军。
            if (pawn.Faction == null || (pawn.Faction != radar.Faction
                && pawn.Faction.RelationKindWith(radar.Faction) != FactionRelationKind.Ally))
            {
                return false;
            }

            if (pawn.InMentalState || pawn.MentalStateDef != null)
            {
                return false;
            }

            return true;
        }

        private void RefreshAura(Pawn radar, Map map)
        {
            HediffDef auraDef = this.Props.auraHediff;
            if (auraDef == null)
            {
                return;
            }

            float range = Mathf.Max(0f, this.Props.auraRange);
            float rangeSquared = range * range;
            IntVec3 radarPos = radar.PositionHeld;
            IReadOnlyList<Pawn> friendlies = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < friendlies.Count; i++)
            {
                Pawn pawn = friendlies[i];
                if (!IsAuraTarget(radar, pawn))
                {
                    continue;
                }

                if (radarPos.DistanceToSquared(pawn.PositionHeld) > rangeSquared)
                {
                    continue;
                }

                ApplyOrRefresh(pawn, auraDef, this.Props.auraDurationTicks);
            }
        }

        // -----------------------------------------------------------------
        // 2. 强化雷达标记（敌方）
        // -----------------------------------------------------------------
        private void RefreshMarks(Pawn radar, Map map)
        {
            float range = Mathf.Max(0f, this.Props.markRange);
            float rangeSquared = range * range;
            IntVec3 radarPos = radar.PositionHeld;

            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (pawn == null || pawn == radar || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.health == null)
                {
                    continue;
                }

                // 敌对过滤
                if (!radar.HostileTo(pawn))
                {
                    continue;
                }

                if (radarPos.DistanceToSquared(pawn.PositionHeld) > rangeSquared)
                {
                    continue;
                }

                // Rukaya_RadarLockOn：RukayaRadarTargetingExtension（命中/烟幕）+ ATGM 读取的锁定。
                // 强化暴露：更强瞄准倍率、更频繁隐身扰乱，复用原有 targeting/expose 接口。
                ApplyOrRefresh(pawn, this.Props.lockOnHediff, this.Props.markDurationTicks);
                ApplyOrRefresh(pawn, this.Props.exposedHediff, this.Props.markDurationTicks);
            }
        }

        /// <summary>
        /// 添加或刷新一个由 HediffComp_Disappears 计时的 buff/标记。
        /// 已存在时只把剩余时间抬到 durationTicks（不叠加、不重复添加）。
        /// 目标身上已有的同名 Hediff 若没有 Disappears（例如被原版同名 def 覆盖）则直接跳过，避免误删。
        /// </summary>
        public static void ApplyOrRefresh(Pawn target, HediffDef def, int durationTicks)
        {
            if (target == null || def == null || target.health == null || durationTicks <= 0)
            {
                return;
            }

            Hediff hediff = target.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff == null)
            {
                bool hasDisappears = false;
                if (def.comps != null)
                {
                    for (int i = 0; i < def.comps.Count; i++)
                    {
                        if (def.comps[i] is HediffCompProperties_Disappears)
                        {
                            hasDisappears = true;
                            break;
                        }
                    }
                }

                if (!hasDisappears)
                {
                    Log.WarningOnce("[Rukaya] Radar effect " + def.defName + " needs a HediffCompProperties_Disappears to expire; skipped.", def.GetHashCode());
                    return;
                }

                hediff = HediffMaker.MakeHediff(def, target);
                target.health.AddHediff(hediff);
            }

            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null && disappears.ticksToDisappear < durationTicks)
            {
                disappears.ticksToDisappear = durationTicks;
            }
        }

        // -----------------------------------------------------------------
        // 3. 雷达中继指挥范围（供 Patch_Pawn_MechanitorTracker_CanCommandTo 调用）
        // -----------------------------------------------------------------

        /// <summary>
        /// 机械师 <paramref name="overseer"/> 控制的机械 <paramref name="mech"/>，
        /// 能否借本雷达把指挥权限延伸到 <paramref name="targetCell"/>。
        /// 只做判定，不修改任何关系或机械师绑定，也不递归调用原版 CanCommandTo。
        /// </summary>
        public bool CanRelayCommand(Pawn mech, Pawn overseer, IntVec3 targetCell)
        {
            if (mech == null || overseer == null)
            {
                return false;
            }

            if (!this.CanOperateRadar())
            {
                return false;
            }

            Map map = this.parent.MapHeld;
            if (map == null || !targetCell.InBounds(map))
            {
                return false;
            }

            if (this.Props.commandRelaySameMapOnly && (mech.MapHeld != map || overseer.MapHeld != map))
            {
                return false;
            }

            // 同机械师过滤：机械的 overseer 必须就是发起指挥的机械师
            if (this.Props.commandRelayRequireSameOverseer && mech.GetOverseer() != overseer)
            {
                return false;
            }

            float range = Mathf.Max(0f, this.Props.commandRelayRangeCells);
            return this.parent.PositionHeld.DistanceToSquared(targetCell) < range * range;
        }

        // -----------------------------------------------------------------
        // 可选的支援范围圈（只在选中电磁雷达时绘制）
        // -----------------------------------------------------------------
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            Pawn radar = this.parent as Pawn;
            if (radar == null || !radar.Spawned || !this.CanOperateRadar())
            {
                return;
            }

            if (this.Props.auraRange > 0f && this.Props.auraHediff != null)
            {
                GenDraw.DrawRadiusRing(radar.Position, this.Props.auraRange, new Color(0.52f, 1f, 0.95f, 0.85f));
            }

            if (this.Props.markRange > 0f && (this.Props.lockOnHediff != null || this.Props.exposedHediff != null))
            {
                GenDraw.DrawRadiusRing(radar.Position, this.Props.markRange, new Color(1f, 0.35f, 0.25f, 0.85f));
            }

            if (this.Props.commandRelayRangeCells > 0f)
            {
                GenDraw.DrawRadiusRing(radar.Position, this.Props.commandRelayRangeCells, new Color(1f, 1f, 1f, 0.7f));
            }
        }
    }
}
