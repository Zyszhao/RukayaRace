using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class CompProperties_RukayaSAMLauncher : CompProperties
    {
        public CompProperties_RukayaSAMLauncher()
        {
            compClass = typeof(CompRukayaSAMLauncher);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;
            if (missileDef == null || missileDef.thingClass != typeof(Thing_RukayaSAMMissile))
                yield return $"{parentDef.defName}: missileDef must use Thing_RukayaSAMMissile.";
            if (fuelPerMissile <= 0f)
                yield return $"{parentDef.defName}: fuelPerMissile must be positive.";
            CompProperties_Refuelable fuel = null;
            if (parentDef.comps != null)
            {
                foreach (CompProperties comp in parentDef.comps)
                {
                    if (comp is CompProperties_Refuelable refuelable)
                    {
                        fuel = refuelable;
                        break;
                    }
                }
            }
            if (fuel == null || fuel.fuelCapacity < fuelPerMissile)
                yield return $"{parentDef.defName}: refuelable fuel capacity must cover one missile.";
        }

        public ThingDef missileDef;
        public SoundDef launchSound;
        /// <summary>
        /// 发射一枚拦截弹消耗的燃料量。单位是宿主 CompRefuelable 的内部单位：
        /// 按当前口径（2026-10-08 需求方确认）1 个 Rukaya_ActiveMatterContainer = 1 点，
        /// 所以默认值 30 = 一枚导弹 = 30 个容器。
        /// </summary>
        public float fuelPerMissile = 30f;

        /// <summary>
        /// 架上「待发弹」贴图中心相对**建筑贴图画布中心**的额外偏移（游戏格）。
        /// 语义与 <c>graphicData.drawOffset</c> 完全一致：(x = 东西, y = 高度, z = 南北)，
        /// 而 z 就是屏幕上的上下（+z 向上）。
        ///
        /// 数据来源：需求方 2026-10-09 提供的画板坐标。1024px 画布 ⇒ 1024 / 8.48 ≈ 120.75 px/格。
        /// 以画板中心 (512, 1556) 为基准，导弹贴图（1024² 画布）中心应落在：
        ///   北 (512, 1403.51)     → 上移 152.49px ⇒ z = +152.49 / 120.75 ≈ +1.263
        ///   西 (463.92, 1351.57)  → 左移  48.08px、上移 204.43px ⇒ x = -0.398, z = +1.693
        ///   东 = 西的水平镜像     → x = +0.398, z = +1.693
        ///   南 = **复用北向**（需求方 2026-10-09 指定：南向贴图 / 摆放位置 / 发射朝向与北向完全一致），
        ///        故没有独立的南向字段；旧数据"下移 34.02px ⇒ z = -0.282"已废止。
        /// 说明：这里量的是**画布中心**；实测 Rukaya_SAM.png 的 alpha 内容中心在 (511.5, 505)，
        /// 比画布中心高 6.5px（≈0.054 格），远小于对齐精度，故不再补偿。
        /// </summary>
        public Vector3 missileOffsetNorth = new Vector3(0f, 0f, 1.263f);
        public Vector3 missileOffsetWest = new Vector3(-0.398f, 0f, 1.693f);
        public Vector3 missileOffsetEast = new Vector3(0.398f, 0f, 1.693f);

        /// <summary>
        /// 架上「待发弹」的摆放角（度）：0 = 弹头朝北（画布上方），正值 = 屏幕顺时针。
        /// 数据来源同上（需求方 2026-10-09）：西 = 逆时针 75° ⇒ -75；东 = 西的镜像 ⇒ +75。
        /// **南北两向固定 0**（弹体落在垂直轴线上，且南向已与北向同贴图），故没有对应字段。
        ///
        /// 注意这是**架上的摆放角**（表现导轨朝向），与发射后的飞行方向无关：
        /// 飞行贴图的角度由 <c>CompRukayaSAMLauncher.FlightAngle()</c> 从 LaunchDirection 直接算出
        /// （西 -65.2° / 东 +65.2°），两者相差约 10°，属于"导轨仰角≠发射方位"的正常表现。
        /// </summary>
        public float missileAngleWest = -75f;
        public float missileAngleEast = 75f;
    }
}
