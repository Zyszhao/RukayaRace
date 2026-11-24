using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    /// <summary>
    /// 卢卡娅机械机炮弹丸。继承 <see cref="Bullet"/> 走常规直射弹丸直击结算，
    /// 命中时按 <see cref="RukayaAirburstProjectileExtension"/> 释放定向破片；
    /// <see cref="DrawAt"/> 仅额外给曳光弹的绘制位置叠加纯视觉横向摆动
    /// （参数见弹丸 Def 上的 <see cref="RukayaBarrageSwayExtension"/>）。
    ///
    /// 为什么只改 DrawAt：
    /// * 命中/拦截走的是 <c>Projectile.TickInterval</c> 里的 <c>ExactPosition</c> 与 <c>Position</c>，不受绘制偏移影响；
    /// * <c>DrawAt</c> 的 <paramref name="drawLoc"/> 只交给渲染，不影响任何判定；
    /// * 炮口处无偏移、接近目标时逐渐展开，呈现目标周围的弹幕扫动。
    /// </summary>
    public class Projectile_RukayaMechCannonRound : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            IntVec3 impactCell = Position;
            Map impactMap = Map;

            if (!blockedByShield && impactMap != null)
            {
                RukayaAirburstProjectileExtension extension = def.GetModExtension<RukayaAirburstProjectileExtension>();
                if (extension != null)
                {
                    Projectile_RukayaAirburstRocket.LaunchFragments(
                        impactCell,
                        impactMap,
                        extension,
                        launcher,
                        preventFriendlyFire,
                        equipment);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (def.GetModExtension<RukayaBarrageSwayExtension>() == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            // lifetime 与 ticksToImpact 都会递减，不能拿两者相减计算已飞时间。
            // 原始飞行时长由固定的起终点与速度恢复，保证每发相位稳定。
            int initialFlightTicks = Mathf.Max(1, Mathf.CeilToInt(StartingTicksToImpact));
            int launchTick = Find.TickManager.TicksGame - (initialFlightTicks - ticksToImpact);
            Vector3 offset = RukayaBarrageSway.TracerDrawOffset(
                def, launcher, origin, destination, DistanceCoveredFraction, launchTick, thingIDNumber);
            base.DrawAt(drawLoc + offset, flip);
        }
    }
}
