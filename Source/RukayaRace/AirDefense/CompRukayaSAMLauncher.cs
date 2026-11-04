using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RukayaRace.AirDefense
{
    public class CompRukayaSAMLauncher : ThingComp
    {
        public CompProperties_RukayaSAMLauncher Props => (CompProperties_RukayaSAMLauncher)props;

        public bool Ready
        {
            get
            {
                if (!parent.Spawned || parent.Faction != Faction.OfPlayer || parent.Position.Roofed(parent.Map)
                    || Props.missileDef?.thingClass != typeof(Thing_RukayaSAMMissile))
                    return false;
                CompRefuelable fuel = parent.GetComp<CompRefuelable>();
                return fuel != null && fuel.Fuel >= Props.fuelPerMissile;
            }
        }

        public static CompRukayaSAMLauncher FindReady(Map map)
        {
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is ThingWithComps building)
                {
                    CompRukayaSAMLauncher launcher = building.GetComp<CompRukayaSAMLauncher>();
                    if (launcher?.Ready == true)
                        return launcher;
                }
            }
            return null;
        }

        public bool TryLaunch()
        {
            return TryLaunch(out _);
        }

        public bool TryLaunch(out Thing_RukayaSAMMissile launchedMissile)
        {
            launchedMissile = null;
            if (!Ready)
                return false;
            Thing spawned = GenSpawn.Spawn(Props.missileDef, parent.Position, parent.Map);
            if (!(spawned is Thing_RukayaSAMMissile missile))
            {
                Log.Error($"[Rukaya] {parent.def.defName}: {Props.missileDef.defName} did not spawn as Thing_RukayaSAMMissile.");
                spawned?.Destroy(DestroyMode.Vanish);
                return false;
            }
            missile.Initialize(MissileOrigin(), LaunchDirection(), FlightAngle());
            launchedMissile = missile;
            CompRefuelable fuel = parent.GetComp<CompRefuelable>();
            fuel.ConsumeFuel(Props.fuelPerMissile);
            Props.launchSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 2f);
            return true;
        }

        public override void PostDraw()
        {
            CompRefuelable fuel = parent.GetComp<CompRefuelable>();
            if (fuel == null || fuel.Fuel < Props.fuelPerMissile)
                return;
            Props.missileDef.graphicData.Graphic.DrawWorker(MissileOrigin(), Rot4.North,
                Props.missileDef, parent, MountedMissileAngle());
        }

        private Vector3 MissileOrigin()
        {
            return parent.DrawPos + parent.def.graphicData.DrawOffsetForRot(parent.Rotation)
                + MountedMissileOffset() + Altitudes.AltIncVect;
        }

        /// <summary>
        /// 架上待发弹贴图中心相对建筑贴图画布中心的额外偏移（数据与换算见
        /// <see cref="CompProperties_RukayaSAMLauncher.missileOffsetNorth"/>）。
        /// 没有这段偏移时弹体中心正好落在底座画布中心，与数据给定的位置最多差 1.7 格（≈205px）——
        /// 这就是需求方 2026-10-08 反馈的"待发弹未对齐"。
        ///
        /// **南向与北向共用同一份偏移**：南向贴图已停用（Graphic_Multi 缺 _south 时回退到 north），
        /// 发射方向对南北两向也本来就都是正北（见 <see cref="LaunchDirection"/>），
        /// 所以南北两向的贴图 / 摆放位置 / 发射行为必须完全同参（需求方 2026-10-09 指定）。
        /// </summary>
        private Vector3 MountedMissileOffset()
        {
            if (parent.Rotation == Rot4.North || parent.Rotation == Rot4.South)
                return Props.missileOffsetNorth;
            if (parent.Rotation == Rot4.West)
                return Props.missileOffsetWest;
            return Props.missileOffsetEast;
        }

        /// <summary>
        /// 架上待发弹的摆放角（度）：0 = 弹头朝北 = 画布上方，正值 = 屏幕顺时针。
        /// 东西向由美术数据给定（西 -75 / 东 +75）；**南北都返回 0**（弹体落在垂直轴线上，
        /// 且南向与北向共用同一张贴图，见 <see cref="MountedMissileOffset"/>）。
        /// </summary>
        private float MountedMissileAngle()
        {
            if (parent.Rotation == Rot4.West)
                return Props.missileAngleWest;
            if (parent.Rotation == Rot4.East)
                return Props.missileAngleEast;
            return 0f;
        }

        /// <summary>
        /// 飞出去那枚的贴图朝向角：由 <see cref="LaunchDirection"/> 直接算出
        /// （<c>Atan2(x, z)</c>，0 = 弹头朝北，正值 = 屏幕顺时针），与飞行方向严格一致。
        /// **不要**把它和架上摆放角（美术数据 ±75°）混用：导轨仰角不等于发射方位。
        /// </summary>
        private float FlightAngle()
        {
            Vector3 direction = LaunchDirection();
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private Vector3 LaunchDirection()
        {
            if (parent.Rotation == Rot4.West)
                return new Vector3(-0.91f, 0f, 0.42f);
            if (parent.Rotation == Rot4.East)
                return new Vector3(0.91f, 0f, 0.42f);
            return Vector3.forward;
        }
    }
}
