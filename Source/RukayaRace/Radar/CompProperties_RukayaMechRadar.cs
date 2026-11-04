using RimWorld;
using Verse;

namespace RukayaRace.Radar
{
    /// <summary>
    /// 机械雷达（Rukaya_MechRadar）三个接口的数值配置。挂在 ThingDef.comps 中，改 Def 即可调参。
    ///
    /// - 光环：沿用原版 MarksmanCommand 的"范围内刷 buff"机制，数值在 XML 中弱化，同名光环不叠加。
    /// - 标记：复用 Weapons 目录里的 Rukaya_RadarLockOn / 强化版 Rukaya_MechRadarExposed 与其 Expose comp，
    ///   并同时刷新 RadarLockOn（ATGM 读取的锁定 Hediff）。
    /// - 中继：由 Patch_Pawn_MechanitorTracker_CanCommandTo 读取 commandRelayRangeCells 等字段。
    /// </summary>
    public class CompProperties_RukayaMechRadar : CompProperties
    {
        // ---- 运行状态 / 门禁 ----
        public bool requireAwake = true;
        public bool requireEnergy = true;
        public float minEnergyPercent = 0f;

        // ---- 1. 友军广域射击光环 ----
        public float auraRange = 14.9f;
        public int auraRefreshIntervalTicks = 60;
        public int auraDurationTicks = 120;
        public HediffDef auraHediff;

        // ---- 2. 敌方强化雷达标记 ----
        public float markRange = 24.9f;
        public int markRefreshIntervalTicks = 60;
        public int markDurationTicks = 600;
        public HediffDef lockOnHediff;
        public HediffDef exposedHediff;

        // ---- 3. 雷达中继指挥范围 ----
        // 以雷达为圆心的额外指挥半径（格），原版机械师自身指挥半径是 24.9（DistanceToSquared < 620.01）
        public float commandRelayRangeCells = 24.9f;
        // 只中继给"同一机械师"控制的机械（GetOverseer() 相同）
        public bool commandRelayRequireSameOverseer = true;
        // 中继只在同一张地图上生效
        public bool commandRelaySameMapOnly = true;

        public CompProperties_RukayaMechRadar()
        {
            this.compClass = typeof(CompRukayaMechRadar);
        }
    }
}
