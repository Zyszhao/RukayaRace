using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    /// <summary>
    /// 把**宿主炮塔顶**扳向空中目标（仅限本组件确实在对空交战时）。
    ///
    /// 两个阶段、两种口径：
    /// * 预热/锁定期 —— 指向空中目标的格子（原版 <c>ForceFaceTarget</c> 语义）；
    /// * 拦截弹在途 —— 指向**发射弹道角**（<see cref="CompRukayaSkyInterceptor.AirAimAngle"/>），
    ///   不再瞄目标的落点投影（需求方 2026-10-08 反馈）。
    ///
    /// 为什么必须用补丁，而不是组件在自己的 CompTick 里写 <c>Top.CurRotation</c>：
    /// <c>Building_TurretGun.Tick()</c> 的调用顺序是
    ///     Building_TurretGun.Tick → base.Tick()（内含 ThingWithComps.Tick → 所有 comp 的 CompTick）
    ///     → 之后才是炮塔自己的 <c>top.TurretTopTick()</c>
    /// 所以组件在 CompTick 里写的朝向会在同一 tick 内被随后执行的 TurretTopTick 覆盖。
    /// 在 TurretTopTick 的 postfix 上改写，是唯一"在原版判定之后、绘制之前"生效的位置。
    ///
    /// 本补丁**不碰宿主的 verb、不碰它的 burst 状态**：只在 AirAimTarget 非空时改写朝向，
    /// 其余时间完全不干预——与设计口径「不占用它的 verb，与它的 burst 状态互不干扰」一致
    /// （设计文档/分层防空系统_实现交接.md §7.2）。
    /// </summary>
    [HarmonyPatch(typeof(TurretTop), nameof(TurretTop.TurretTopTick))]
    public static class Patch_TurretTop_TurretTopTick_RukayaAirAim
    {
        [HarmonyPostfix]
        public static void Postfix(TurretTop __instance)
        {
            if (__instance == null || ParentTurretField == null)
                return;
            if (!(ParentTurretField.GetValue(__instance) is Building_TurretGun turret))
                return;
            CompRukayaSkyInterceptor interceptor = turret.GetComp<CompRukayaSkyInterceptor>();
            if (interceptor == null)
                return;
            // 拦截弹还在飞：炮塔顶对准**发射弹道角**。
            // 口径修正（需求方 2026-10-08 反馈）：原版 ForceFaceTarget 用的是目标 Cell（空投仓的地面投影），
            // 看起来像"炮口瞄着落点"；这里改成开火那一刻炮口自己算出的空中弹道角。
            float? shotAngle = interceptor.AirAimAngle;
            if (shotAngle.HasValue)
            {
                __instance.CurRotation = shotAngle.Value;
                return;
            }
            // 预热/锁定阶段：照旧指向空中目标（原版语义）。
            Thing target = interceptor.AirAimTarget;
            if (target == null)
                return;
            __instance.ForceFaceTarget(new LocalTargetInfo(target));
        }

        /// <summary>TurretTop 只把宿主记在私有字段里，取回它才能拿到组件。</summary>
        private static readonly FieldInfo ParentTurretField =
            typeof(TurretTop).GetField("parentTurret", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// 把**机械体的炮塔贴图**扳向空中目标（仅限本组件确实在对空交战时）。
    ///
    /// 为什么机械体需要另一个补丁：机械体没有 <see cref="TurretTop"/>，炮塔是渲染树上的
    /// <c>PawnRenderNode_TurretGun</c>，它的朝向来自
    /// <c>base.RotationFor(node, parms) * turretComp.curRotation.ToQuat()</c>——
    /// 而那个 comp 是**地面那门炮**的 comp（对空吊舱自己不提供渲染节点，
    /// 否则同一个底盘上会画出两层炮塔）。
    ///
    /// 做法：**不写** <c>turretComp.curRotation</c>（那是玩法状态，只在
    /// <c>CompTurretGun.CompTick</c> 里由原版维护，且 <c>currentTarget</c> 无效时不会被刷新，
    /// 写进去就会一直卡着对空角度 → 状态污染）。改为在原版算完结果之后，
    /// 把结果里"绕世界 Y 轴的地面偏航"替换成对空角度：
    /// <c>result * Inverse(groundYaw) * airYaw</c>，其中 groundYaw 是原版炮塔偏航
    /// 与 <c>ToQuat()</c>（= <c>AngleAxis(y, up)</c>）都是绕同一根轴的有向旋转，可以消掉。
    /// 没在对空交战时 <see cref="CompRukayaSkyInterceptor.AirAimRotation"/> 返回 null，这里完全不动。
    ///
    /// 口径与建筑炮塔顶完全一致：预热/锁定期指向空中目标，拦截弹在途指向发射弹道角。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker_TurretGun), nameof(PawnRenderNodeWorker_TurretGun.RotationFor))]
    public static class Patch_PawnTurretGun_RotationFor_RukayaAirAim
    {
        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode node, ref Quaternion __result)
        {
            if (!(node is PawnRenderNode_TurretGun turretNode))
                return;
            CompTurretGun turretComp = turretNode.turretComp;
            if (turretComp?.parent == null)
                return;
            CompRukayaSkyInterceptor interceptor = turretComp.parent.GetComp<CompRukayaSkyInterceptor>();
            if (interceptor == null)
                return;
            // 结果里的地面偏航来自**地面那门炮**的 comp（见上面注释）；它自己的
            // <c>CompProperties_TurretGun.angleOffset</c> 只在它的 CompTick 里加，这里要补上，
            // 否则对空角度与地面开火角度差一个 angleOffset。
            float? airRotation = interceptor.AirAimRotation;
            if (!airRotation.HasValue)
                return;
            // AirAimRotation 已经包含对空炮座 angleOffset；先去掉它再套用地面贴图偏移，避免重复加 -90°。
            float airAngle = airRotation.Value - interceptor.Props.angleOffset + turretComp.Props.angleOffset;
            Quaternion groundYaw = turretComp.curRotation.ToQuat();
            __result = __result * Quaternion.Inverse(groundYaw) * Quaternion.AngleAxis(airAngle, Vector3.up);
        }
    }
}
