using RimWorld;
using RukayaRace.AirDefense;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Buildings
{
    /// <summary>
    /// 舰炮炮塔：在 <see cref="Building_TurretGun"/> 的"炮塔顶 + 底座"之间再插一层**炮管**，
    /// 并按需求方口径（2026-10-09）让炮管在开火时做**沿炮管轴线的直线后坐**：
    /// 只平移、不摇摆、不旋转；炮塔顶没有任何后坐动画。
    ///
    /// 三层贴图共用同一张 512² 画布、同一个旋转中心（画布中心），绘制顺序自上而下 =
    /// **炮塔顶 / 炮管 / 底座**：
    /// * 底座    —— 建筑本体 <c>graphicData</c>，走 MapMesh，原版逻辑，本类不碰；
    /// * 炮管    —— 本类在 <see cref="DrawAt"/> 里画，高度 = 底座 + 半个 <see cref="Altitudes.AltIncVect"/>；
    /// * 炮塔顶  —— 原版 <see cref="TurretTop"/> 画，高度 = 底座 + 一个 <see cref="Altitudes.AltIncVect"/>。
    /// 因为炮管与炮塔顶用**同一个 aim 角度和同一个旋转矩阵**，两层天然同步旋转 / 同步索敌；
    /// 炮管只是额外叠了一个沿自身轴线的位移，所以后坐永远顺着炮管方向，不会甩到侧面。
    ///
    /// 为什么用 thingClass 子类而不是 Harmony 补丁：
    /// 原版 <c>Building_TurretGun.DrawAt</c> 只画炮塔顶，没有给"第二层炮塔贴图"留钩子；
    /// 覆写 <see cref="DrawAt"/> 是最小侵入的做法——炮塔的 verb、burst 状态机、索敌、存档
    /// 全部走基类，一行都没改（对空系统依赖的那套载体模式判定用的是 <c>is Building_TurretGun</c>，
    /// 子类同样满足）。
    /// </summary>
    public class Building_RukayaNavalArtillery : Building_TurretGun
    {
        public override void ExposeData()
        {
            base.ExposeData();
            // Older saves wrote the air gun before the main gun under the same XML key.
            // The host may therefore have loaded the air weapon as its ground weapon.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && gun?.def != def.building.turretGunDef)
                HarmonyLib.AccessTools.Method(typeof(Building_TurretGun), "MakeGun").Invoke(this, null);
        }

        /// <summary>
        /// 炮管层相对底座的高度增量（单位 = <see cref="Altitudes.AltIncVect"/> 的倍数）。
        /// 取 0.5 是刻意的：底座 &lt; 炮管 &lt; 炮塔顶，各差半个 AltInc（≈0.018 世界单位），
        /// 既保证深度排序稳定（炮塔顶永远压住炮管根部），又不会产生肉眼可见的高度错位。
        /// </summary>
        private const float BarrelAltitudeSteps = 0.5f;

        private RukayaNavalArtilleryExtension extension;
        private bool extensionResolved;
        private Material barrelMaterial;
        private bool barrelMaterialResolved;

        /// <summary>
        /// 先画炮管层，再走原版（炮塔顶 + 建筑本体）。
        /// 提交顺序与高度差**双双**保证炮管被炮塔顶压住：视觉上从上到下就是
        /// 炮塔顶 / 炮管 / 底座（Cutout 是不透明队列，深度测试与提交顺序一致）。
        /// </summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            DrawBarrelLayer(drawLoc);
            base.DrawAt(drawLoc, flip);
        }

        /// <summary>
        /// 炮管层：位置 / 旋转 / 缩放全部照抄 <see cref="TurretTop.DrawTurret"/>，只多一个后坐位移。
        /// 因为贴图路径、画布尺寸、旋转原点都与炮塔顶一致，两层画出来永远是同一套坐标。
        /// </summary>
        private void DrawBarrelLayer(Vector3 drawLoc)
        {
            Material material = BarrelMaterial;
            BuildingProperties properties = def.building;
            if (material == null || properties == null || Top == null)
                return;
            float aimAngle = AimAngle;
            Vector3 topOffset = new Vector3(properties.turretTopOffset.x, 0f, properties.turretTopOffset.y);
            Vector3 position = drawLoc + Altitudes.AltIncVect * BarrelAltitudeSteps + topOffset + RecoilOffset(aimAngle);
            Quaternion rotation = (TurretTop.ArtworkRotation + aimAngle).ToQuat();
            float size = properties.turretTopDrawSize;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, rotation, new Vector3(size, 1f, size)),
                material, 0);
        }

        /// <summary>
        /// 炮塔当前朝向（度）。口径与 <see cref="TurretTop.DrawTurret"/> 完全一致：
        /// 有效 verb 给了 <c>AimAngleOverride</c> 就用它（例如激光类 verb），否则用 <see cref="TurretTop.CurRotation"/>。
        /// </summary>
        private float AimAngle
        {
            get
            {
                Verb verb = CurrentEffectiveVerb;
                float? aimAngleOverride = verb?.AimAngleOverride;
                return aimAngleOverride ?? Top.CurRotation;
            }
        }

        /// <summary>
        /// 开火后坐位移（世界坐标）。
        ///
        /// 需求方口径：**直线的、不摇摆的**后坐。实现上就是"沿炮管轴线向后的一段纯平移"：
        /// * 方向：炮管贴图在画布里朝 +x 伸出，所以后退 = 画布局部 -x（<see cref="Vector3.left"/>）；
        ///   用与网格完全相同的旋转矩阵把它转到世界坐标，因此无论炮塔朝哪，位移永远顺着炮管轴线。
        /// * 幅度：开火那一帧退到 <c>barrelRecoilDistance</c>，然后按 tick **线性**衰减回 0。
        ///   没有横向分量、没有角度偏移、没有往复振荡——这就是"不摇摆"。
        /// * 计时：只看 verb 的 <c>LastShotTick</c>，不引入任何额外状态，也不影响射击逻辑。
        /// </summary>
        private Vector3 RecoilOffset(float aimAngle)
        {
            RukayaNavalArtilleryExtension ext = Extension;
            if (ext == null || ext.barrelRecoilDistance <= 0f || ext.barrelRecoilTicks <= 0)
                return Vector3.zero;
            int lastShotTick = LastBarrelShotTick;
            if (lastShotTick < 0)
                return Vector3.zero;
            int elapsed = Find.TickManager.TicksGame - lastShotTick;
            if (elapsed < 0 || elapsed >= ext.barrelRecoilTicks)
                return Vector3.zero;
            float remaining = 1f - (float)elapsed / ext.barrelRecoilTicks;
            Vector3 backward = Vector3.left.RotatedBy(TurretTop.ArtworkRotation + aimAngle);
            return backward * (ext.barrelRecoilDistance * remaining);
        }

        /// <summary>
        /// 最近一次"炮管里真的打出去了一发"的 tick，-1 = 从没打过。
        /// 两门炮都要看，因为它们共用这两根炮管：
        /// * 对地 —— 宿主自己的 <see cref="Building_TurretGun.AttackVerb"/>（双联齐射，每发各算一次）；
        /// * 对空 —— 对空组件那把**独立的** AA 炮身的 verb（载体模式下它不占用宿主 verb，
        ///   所以必须单独查，否则对空射击时炮管不会后坐）。
        /// </summary>
        private int LastBarrelShotTick
        {
            get
            {
                int last = -1;
                Verb ground = AttackVerb;
                if (ground != null)
                    last = ground.LastShotTick;
                Verb air = GetComp<CompRukayaSkyInterceptor>()?.WeaponVerb;
                if (air != null && air.LastShotTick > last)
                    last = air.LastShotTick;
                return last;
            }
        }

        private RukayaNavalArtilleryExtension Extension
        {
            get
            {
                if (!extensionResolved)
                {
                    extension = def.GetModExtension<RukayaNavalArtilleryExtension>();
                    extensionResolved = true;
                }
                return extension;
            }
        }

        /// <summary>
        /// 炮管层材质。按需加载（<see cref="MaterialPool.MatFrom(Texture2D)"/> 用的就是
        /// 与炮塔顶一样的 Cutout 着色器），缓存到实例字段；读档后实例字段为空，自然重新取一次。
        /// 贴图没配或找不到时返回 null = 不画炮管层（炮塔退回两层，不会抛异常）。
        /// </summary>
        private Material BarrelMaterial
        {
            get
            {
                if (barrelMaterialResolved)
                    return barrelMaterial;
                barrelMaterialResolved = true;
                string texPath = Extension?.barrelTexPath;
                if (!texPath.NullOrEmpty())
                {
                    Texture2D texture = ContentFinder<Texture2D>.Get(texPath, false);
                    if (texture == null)
                        Log.ErrorOnce($"[Rukaya] {def.defName}: barrel texture '{texPath}' not found; drawing the turret without its barrel layer.",
                            def.shortHash ^ 20261009);
                    else
                        barrelMaterial = MaterialPool.MatFrom(texture);
                }
                return barrelMaterial;
            }
        }
    }
}
