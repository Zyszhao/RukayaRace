using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    /// <summary>
    /// 本模组已有的**导引律**，抽出来给"真飞出去的对空拦截弹"复用。
    ///
    /// 口径与肩扛 ATGM（<c>Source/RukayaRace/Projectiles/Projectile_RukayaGuidedATGM.cs</c>）
    /// 完全一致，就是一条**纯追踪（pure pursuit）**：
    /// * 每 tick 朝目标**当前**位置重新取一次方向（不是开火时算一次的提前量）；
    /// * 每 tick 只迈固定的一步（= 弹体速度），所以航迹会随目标移动而弯过去；
    /// * 一旦落在命中半径以内就当场收尾，不会冲过头再回头修正。
    ///
    /// 这里刻意做成纯函数（不碰 Thing / 不碰 Projectile），
    /// 好让对空拦截弹（<see cref="Projectile_RukayaSkyInterceptor"/>）在"开火即定论"的框架下
    /// 只借用这条**飞行动作**，而把"能不能打、打中之后怎么办"留在防空系统自己手里。
    /// </summary>
    public static class RukayaGuidance
    {
        /// <summary>
        /// 朝 <paramref name="target"/> 走一步（<paramref name="speedPerTick"/> 格）。
        /// 返回 true 表示这一步已经落到命中半径以内（调用方应当就此收尾）。
        /// <paramref name="angle"/> 输出的是这一步的实际航向角（0 = 正北，顺时针），
        /// 用于让贴图跟着弹道转。
        /// </summary>
        public static bool StepToward(ref Vector3 position, Vector3 target, float speedPerTick, float hitRadius,
            out float angle)
        {
            Vector3 delta = target - position;
            float distance = delta.magnitude;
            angle = distance > 0.0001f ? delta.AngleFlat() : 0f;
            if (distance <= hitRadius)
            {
                position = target;
                return true;
            }
            if (distance > speedPerTick)
            {
                position += delta * (speedPerTick / distance);
                return distance - speedPerTick <= hitRadius;
            }
            position = target;
            return true;
        }
    }
}
