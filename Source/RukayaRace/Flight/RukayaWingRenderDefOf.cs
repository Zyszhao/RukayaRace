using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Typed handles for the defs the wing render tree and its animation are built from.
    ///
    /// These exist so the render worker and the animation patch never compare def names as strings at
    /// runtime: a mismatch between a render node's <c>tagDef</c> and an <c>AnimationDef</c> key is a silent
    /// failure (the animation worker resolves to null and simply never plays), so the whole chain is kept
    /// in one place instead of being spread across the worker and the animation patch.
    ///
    /// The tag defs are declared in 1.7/Defs/PawnRenderTreeDefs/Rukaya_RenderNodeTagDefs.xml; the animations
    /// in 1.7/Defs/AnimationDefs/Rukaya_WingFly.xml.
    ///
    /// 现状（2026-10-16）：扑翼动画工作正常 —— 每帧 4 tick、一轮 32 tick（≈15 fps），
    /// 8 张图严格等长（第 8 帧不再停两拍），并由
    /// <see cref="RukayaWingAnimationValidator"/> 在启动期逐 tick 校验（日志关键字「扑翼校验」）。
    /// **下面这些 defName 一个字都不要改**：渲染树的 <c>tagDef</c> 与动画关键帧的 <c>key</c>
    /// 都按字符串互相匹配，改名不会有编译错误，只会让扑翼在游戏里静默失效（解不到动画、翅膀不拍）。
    /// 时序的不变量与改速率的正确步骤写在 Rukaya_WingFly.xml 文件头。
    /// </summary>
    [DefOf]
    public static class RukayaWingRenderDefOf
    {
        /// <summary>Back-layer flap anchor; the keyframe key must read exactly like this defName.</summary>
        public static PawnRenderNodeTagDef Rukaya_WingFlapBack;

        /// <summary>Front-layer flap anchor; the keyframe key must read exactly like this defName.</summary>
        public static PawnRenderNodeTagDef Rukaya_WingFlapFront;

        /// <summary>Static wings that sit behind the body.</summary>
        public static PawnRenderNodeTagDef Rukaya_WingBack;

        /// <summary>Static wings that sit in front of the body.</summary>
        public static PawnRenderNodeTagDef Rukaya_WingFront;

        public static AnimationDef Rukaya_WingFlap_South;

        public static AnimationDef Rukaya_WingFlap_North;

        public static AnimationDef Rukaya_WingFlap_East;

        /// <summary>Uses its own <c>{1..8}_west</c> frame set; the west nodes cancel the mesh set's
        /// pre-mirrored West slot through <c>drawData/dataWest/flip</c>.</summary>
        public static AnimationDef Rukaya_WingFlap_West;

        public static PawnRenderTreeDef Rukaya_Humanlike;
    }
}
