using Verse;

namespace RukayaRace.Weapons
{
    /// <summary>
    /// Salvo parameters of the Rukaya naval artillery turret.
    /// <c>shellsPerShot</c> is how many shells leave the twin barrels on a single shot;
    /// the turret def owns this value, the gun's verb reads it through the caster def.
    /// </summary>
    public class RukayaNavalArtilleryExtension : DefModExtension
    {
        public int shellsPerShot = 2;

        /// <summary>
        /// 炮管层贴图路径（相对 Content/Textures，不带扩展名）。留空 = 不画炮管层，
        /// 炮塔就退回"炮塔顶 + 底座"两层（用于没有拆出炮管的美术资产）。
        /// 炮管贴图必须与炮塔顶**同一张画布、同一比例**：两层共用同一个旋转与偏移，
        /// 只有这样才能保证它们永远对齐（见 <see cref="RukayaRace.Buildings.Building_RukayaNavalArtillery"/>）。
        /// </summary>
        public string barrelTexPath;

        /// <summary>
        /// 开火时炮管沿自己轴线后退的最大距离（格）。0 = 关闭后坐动画。
        /// 参照：炮管贴图在 512px 画布上长 131px，按 turretTopDrawSize 7.6 格换算约 1.94 格，
        /// 所以 0.28 格 ≈ 19px——看得见"退了一下"，又不会把炮管整根缩进炮塔顶里。
        /// </summary>
        public float barrelRecoilDistance = 0.28f;

        /// <summary>
        /// 一个后坐周期的 tick 数：开火那一帧退到最后，之后**线性**回到原位（不摇摆、不旋转）。
        /// 22 tick ≈ 0.37 秒，比一次齐射的间隔（ticksBetweenBurstShots 180）短得多，
        /// 因此相邻两发之间炮管一定已经归位。
        /// </summary>
        public int barrelRecoilTicks = 22;

        /// <summary>Fallback used when a def carries no extension (pair, as built).</summary>
        public static readonly RukayaNavalArtilleryExtension Default = new RukayaNavalArtilleryExtension();
    }
}
