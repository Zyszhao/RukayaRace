using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    // Charge-mode (active matter) warhead tuning for the ATGM launcher missile.
    // On impact the overcharged missile keeps the normal guided explosive behavior
    // (configured via RukayaGuidedATGMProjectileExtension) and additionally:
    // 1. Ignition: attaches fire to the struck thing and ignites a fuel zone around
    //    the impact cell, using the same fuel-and-ignite path as the mod's incendiary
    //    canisters (RukayaIncendiaryUtility).
    // 2. Chain electrocution: strikes up to maxTargets hostile pawns around the impact
    //    point with Flame/Burn + EMP damage (or a real lightning strike when the mod's
    //    electric-lightning option is on) and draws lightning arcs through
    //    MapComponent_RukayaLightningArcs, mirroring the grenade launcher's arc-shock
    //    warhead.
    public class RukayaChargedATGMProjectileExtension : DefModExtension
    {
        // Fire attached to the directly struck thing before the warhead blows. 0 disables.
        // 直击目标附加火焰的尺寸；0 表示关闭。
        public float directFireSize = 0.85f;

        // Radius of the fuel-and-ignite zone around the impact cell. 0 disables.
        // 落点周围泼洒燃料并点燃的半径；0 表示关闭。
        public float igniteRadius = 2.6f;

        // Fire size range per ignited cell in the impact zone.
        // 冲击区内每个格子的火焰尺寸范围。
        public FloatRange fireSizeRange = new FloatRange(0.5f, 0.9f);

        // Fuel filth placed before ignition (keeps fires burning on floor).
        // 点燃前铺下的燃料污物。
        // 注意：不要用 ThingDefOf 做字段默认值 —— DefOf 在 def 加载完成之后才初始化，
        // 加载期读取会拿到 null 并触发 "Tried to use an uninitialized DefOf" 警告。
        // 由 XML 提供，缺失时在 ResolveReferences 中兜底。
        public ThingDef fuelDef;

        // Hostile-pawn search radius around the impact cell for the chain arcs.
        // 电弧链对落点周围敌对单位的搜索半径。
        public float searchRadius = 6.9f;

        // Max hostile pawns hit by the chain (sorted by distance).
        // 电弧链最多命中的敌对单位数量（按距离排序）。
        public int maxTargets = 8;

        // Burn damage and armor penetration applied to each chained pawn.
        // 每个被连锁单位受到的灼伤伤害与穿甲。
        public int directDamage = 22;

        public float directArmorPenetration = 0.24f;

        // EMP damage applied to each chained pawn.
        // 每个被连锁单位受到的 EMP 伤害。
        public float empDamage = 18f;

        // Visual knobs for the lightning arcs.
        // 电弧链的视觉效果参数。
        public float lightningGlowSize = 1.35f;

        public int arcSegmentsPerTarget = 6;

        public float arcJitterRadius = 0.6f;

        public override void ResolveReferences(Def parentDef)
        {
            base.ResolveReferences(parentDef);
            if (this.fuelDef == null)
            {
                this.fuelDef = ThingDefOf.Filth_Fuel;
            }
        }
    }
}
