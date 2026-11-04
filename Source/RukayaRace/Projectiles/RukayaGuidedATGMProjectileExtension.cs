using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaGuidedATGMProjectileExtension : DefModExtension
    {
        public HediffDef guidingHediff;
        public HediffDef radarLockHediff;
        public float guidanceRetargetIntervalTicks = 1f;
        public float guidedHitRadius = 0.42f;
        public float missedImpactRadius = 2.4f;
        public float missDeviationRadius = 2.8f;
        public float radarLockArmorPenetration = 1.35f;
        public int radarLockDamageAmount = -1;
        public bool smokeBreaksGuidance = true;
        public bool requireLineOfSight = true;
        public float guidanceGlowSize = 0.5f;
        public Color guidanceGlowColor = new Color(1f, 0.42f, 0.08f, 0.32f);
        public int guidanceGlowIntervalTicks = 3;
        public int guidanceSparkIntervalTicks = 4;
        /// <summary>
        /// 高抛抬升高度（格）。0 = 平飞（人形肩扛 ATGM 的默认口径）。
        /// 机械体导弹型走的是"flyOverhead + 制导"的混合弹道：制导负责水平方向咬住目标，
        /// 这一项负责让弹体**抬起来再压下去**。高度只作用于绘制（见
        /// <see cref="Projectile_RukayaGuidedATGM.DrawAt"/>），逻辑坐标始终贴着地面走。
        /// </summary>
        public float loftHeight;
    }
}
