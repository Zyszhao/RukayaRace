using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    /// <summary>
    /// 纯视觉的"弹幕摆动"参数，挂在**弹丸 Def**（机炮弹 / 激光脉冲 / 对空模板）上。
    ///
    /// 所有字段只影响绘制：弹丸的 <c>ExactPosition</c>、命中判定、伤害、真实弹道一律不动
    /// （见 <see cref="Projectile_RukayaMechCannonRound.DrawAt"/> 与
    /// <see cref="Projectile_RukayaLaserPulse.ScheduleLaserBeam"/>）。
    /// 噪声全部来自 <see cref="RukayaBarrageSway"/> 的确定性哈希与正弦，**不消费 <c>Rand</c>**，
    /// 因此不会改变任何随机判定序列。
    /// </summary>
    public class RukayaBarrageSwayExtension : DefModExtension
    {
        /// <summary>机炮接近目标时的最大横向绘制偏移（格）。0 = 不摆。</summary>
        public float tracerSwayRadius = 0.9f;
        /// <summary>叠加在机炮弹道上的高频抖动幅度（格），用来让同一轮连发里的每发弹道不完全一样。</summary>
        public float tracerWobbleRadius = 0.16f;
        /// <summary>机炮整轮连发的摆动周期（tick）。约等于一轮 25 发 × 4 tick 的长度。</summary>
        public int tracerSwayPeriodTicks = 100;
        /// <summary>单发弹道在一次飞行内的高频抖动周期数。</summary>
        public float tracerWobbleCycles = 1.6f;

        /// <summary>激光束**起点**的横向摆动幅度（格）。</summary>
        public float beamOriginSwayRadius = 0.22f;
        /// <summary>激光束**终点**的横向摆动幅度（格）。终点偏移只影响画出来的光束，命中点不变。</summary>
        public float beamEndSwayRadius = 1.1f;
        /// <summary>把光束起点沿射向前移到炮口（格）。只改绘制起点，不消耗 Rand。</summary>
        public float beamMuzzleForward = 0.75f;
        /// <summary>激光整轮连发的摆动周期（tick）。约等于一轮 24 发 × 5 tick 的长度。</summary>
        public int beamSwayPeriodTicks = 110;

        /// <summary>对空视觉连发（不结算的那几发曳光弹）落点的横向摆动幅度（格）。</summary>
        public float airAimSwayRadius = 1f;
        /// <summary>对空视觉连发的摆动周期（tick）。</summary>
        public int airAimSwayPeriodTicks = 60;
    }

    /// <summary>
    /// 确定性噪声 + 摆动采样。刻意不使用 <c>Rand</c>：
    /// 弹道/光束是"每帧都在画"的表现层，若在这里抽 <c>Rand</c>，同一场战斗的命中随机序列
    /// 会随"玩家是否看着它"而漂移，等于用画面改变判定。这里只用：
    /// * 整数哈希（种子 = 发射者 thingID + 弹丸 thingID）；
    /// * 发射 tick 的正弦。
    /// 两者都是只读输入，重复调用结果完全一致。
    /// </summary>
    public static class RukayaBarrageSway
    {
        /// <summary>把任意整数散列成 [0,1] 的确定性小数（整数哈希，不碰 Rand）。</summary>
        public static float Hash01(int value)
        {
            uint x = (uint)value;
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }

        /// <summary>每台发射装置自己的相位，让同一 tick 开火的不同机械体不会完全同步摆动。</summary>
        public static float PhaseFor(Thing launcher)
        {
            return Hash01(launcher?.thingIDNumber ?? 0) * Mathf.PI * 2f;
        }

        /// <summary>射向的水平左/右法线（单位向量）；两点重合时返回零向量。</summary>
        public static Vector3 Lateral(Vector3 from, Vector3 to)
        {
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            if (flat.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            flat.Normalize();
            return new Vector3(flat.z, 0f, -flat.x);
        }

        /// <summary>周期摆动采样，返回 [-1,1]。</summary>
        public static float Sweep(int tick, int periodTicks, float phase)
        {
            int period = Mathf.Max(8, periodTicks);
            // 先按周期取模再算相位：长局里 TicksGame 很大，直接乘会损失 float 精度。
            int wrapped = ((tick % period) + period) % period;
            return Mathf.Sin(wrapped * (Mathf.PI * 2f / period) + phase);
        }

        /// <summary>
        /// 机炮弹丸的**绘制**偏移：炮口处为 0，朝目标平滑展开，让弹幕在目标周围扫动。
        /// 实际命中仍由真实弹丸的 Position 决定，不受绘制位置影响。
        /// </summary>
        public static Vector3 TracerDrawOffset(ThingDef def, Thing launcher, Vector3 origin, Vector3 destination,
            float progress, int launchTick, int shotSeed)
        {
            RukayaBarrageSwayExtension extension = def?.GetModExtension<RukayaBarrageSwayExtension>();
            if (extension == null
                || (extension.tracerSwayRadius <= 0f && extension.tracerWobbleRadius <= 0f))
                return Vector3.zero;

            float clampedProgress = Mathf.Clamp01(progress);
            float envelope = clampedProgress * clampedProgress * (3f - 2f * clampedProgress);
            if (envelope <= 0.0001f)
                return Vector3.zero;

            Vector3 lateral = Lateral(origin, destination);
            if (lateral == Vector3.zero)
                return Vector3.zero;

            float phase = PhaseFor(launcher);
            float sweep = Sweep(launchTick, extension.tracerSwayPeriodTicks, phase);
            float wobble = Mathf.Sin(clampedProgress * Mathf.PI * 2f * extension.tracerWobbleCycles
                + phase * 1.7f + Hash01(shotSeed) * Mathf.PI * 2f);
            float amount = sweep * extension.tracerSwayRadius + wobble * extension.tracerWobbleRadius;
            return lateral * (amount * envelope);
        }

        /// <summary>
        /// 激光束的**绘制**端点：起点沿射向前移到炮口并按整轮节拍左右摆，终点反向摆，
        /// 于是连续脉冲看起来是一道扫来扫去的长矛。命中点仍是原来的 <paramref name="destination"/>。
        /// </summary>
        public static void BeamSway(ThingDef def, Thing launcher, Vector3 origin, Vector3 destination,
            int launchTick, out Vector3 swayedOrigin, out Vector3 swayedDestination)
        {
            swayedOrigin = origin;
            swayedDestination = destination;

            RukayaBarrageSwayExtension extension = def?.GetModExtension<RukayaBarrageSwayExtension>();
            if (extension == null)
                return;

            Vector3 flat = new Vector3(destination.x - origin.x, 0f, destination.z - origin.z);
            float distance = flat.magnitude;
            if (distance < 0.05f)
                return;

            Vector3 direction = flat / distance;
            Vector3 lateral = new Vector3(direction.z, 0f, -direction.x);
            float sweep = Sweep(launchTick, extension.beamSwayPeriodTicks, PhaseFor(launcher));
            float forward = Mathf.Min(extension.beamMuzzleForward, distance * 0.45f);
            swayedOrigin = origin + direction * forward + lateral * (sweep * extension.beamOriginSwayRadius);
            swayedDestination = destination - lateral * (sweep * extension.beamEndSwayRadius);
        }

        /// <summary>
        /// 对空视觉连发的落点偏移：只用于"第一发已经定论、后面几发只画不打"的曳光弹，
        /// 让一轮连发在目标周围扇开；真实拦截弹与拦截结算完全不经过这里。
        /// </summary>
        public static Vector3 AirAimOffset(ThingDef def, Thing launcher, Vector3 origin, Vector3 aim, int launchTick)
        {
            RukayaBarrageSwayExtension extension = def?.GetModExtension<RukayaBarrageSwayExtension>();
            if (extension == null || extension.airAimSwayRadius <= 0f)
                return Vector3.zero;

            Vector3 lateral = Lateral(origin, aim);
            if (lateral == Vector3.zero)
                return Vector3.zero;

            return lateral * (Sweep(launchTick, extension.airAimSwayPeriodTicks, PhaseFor(launcher))
                * extension.airAimSwayRadius);
        }
    }
}
