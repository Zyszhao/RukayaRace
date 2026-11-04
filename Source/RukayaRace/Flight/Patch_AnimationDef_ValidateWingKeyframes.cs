using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// 扑翼动画的**启动期硬校验**：专门盯住我们踩过两次的那个坑。
    ///
    /// ==========================================================================================
    /// ⚠ 现状：**四个扑翼 def 全部通过校验，动画工作正常。**
    ///   不要为了"简化"把下面的逐 tick 推演改回公式判断，也不要按报错提示去改 XML 里扑翼
    ///   两个分部的 tick / durationTicks —— 现值（每帧 4 tick、一轮 32 tick）与 XML 是配套的。
    /// ==========================================================================================
    ///
    /// 这里曾经用过一条公式判断：<c>durationTicks − 末关键帧 tick == 帧间隔</c>。
    /// 它漏算了末帧自己那一拍（末帧实际占「自己那一拍」+「钳位尾巴」两段），
    /// 于是对 4/32、6/48、30/240、15/120 四套参数**一律放行**，包括真出问题的 15/120——
    /// 校验存在却抓不到 bug。这条弯路不要再走：**判断时序只能按引擎规则推演，不能套公式**。
    ///
    /// 校验失败时该怎么处理：改 XML 让结果相等（补 / 挪收尾关键帧，或按新速率整体重铺），
    /// 而不是放宽这里的判定。真要改速率，四条不变量见
    /// <c>1.7/Defs/AnimationDefs/Rukaya_WingFly.xml</c> 文件头。
    ///
    /// ------------------------------------------------------------------------------------
    /// 校验分两档（照 <see cref="Validate"/> 的说明）
    /// ------------------------------------------------------------------------------------
    /// · **通用档**（所有 keyframePart，含将来新增的 <c>Root</c> / <c>Head</c> 等分部）：
    ///   ① <c>durationTicks &gt; 0</c>；② 关键帧 tick 严格递增；
    ///   ③ 末关键帧 tick == <c>durationTicks</c>（否则末段被钳位成一段又长又静止的尾巴）。
    ///   **例外**：只有一条关键帧的**恒定分部**免检 ③ —— 它不是循环，整轮都取这一条，
    ///   没有尾巴可钳位（我们的 <c>Head</c> 反向角补偿就是这么写的，见 Rukaya_WingFly.xml）。
    /// · **扑翼档**（只对 <c>Rukaya_WingFlapBack</c> / <c>Rukaya_WingFlapFront</c>）：
    ///   ④ 各段 tick 间隔相等；⑤ 两个 tag 的关键帧 tick 逐条相同（同拍换图）；
    ///   ⑥ 按引擎规则逐 tick 推演，8 张图各占的 tick 数必须完全相等。
    ///
    /// 之所以分两档：<c>Root</c> 之类的分部**可以有自己的节奏**（间隔不必与翅膀对齐，
    /// 引擎对每个 AnimationPart 单独插值），拿扑翼的等长 / 同拍规则去卡它们会误报。
    /// 新增分部只需要满足通用档三条，扑翼两个分部继续按扑翼档全查。
    ///
    /// ------------------------------------------------------------------------------------
    /// 引擎到底怎么取帧
    /// ------------------------------------------------------------------------------------
    /// <c>Verse/AnimationWorker_Keyframes.GetKeyframeForTick</c>：
    ///
    /// <code>
    /// if (tick &lt;= keyframes[0].tick) return keyframes[0];                   // 首帧
    /// if (tick &gt;= keyframes[Count - 1].tick) return keyframes[Count - 1];    // 末帧被钳位到循环末尾
    /// ... 返回第一条 tick &gt;= 当前 tick 的关键帧;
    /// </code>
    ///
    /// tick 来自 <c>PawnRenderTree.AnimationTick = PositiveMod(now − start, durationTicks)</c>，
    /// 所以 tick ∈ [0, durationTicks − 1]，**永远不会等于 durationTicks**。
    ///
    /// 关键在第三条：它取的是**下一条**关键帧，不是上一条。于是一条关键帧负责的是
    /// 「它之前的那一段」，即第 i 张图占 (上一帧.tick, 本帧.tick]。由此：
    ///
    ///   · 首帧只占 tick 0 那一拍（1 tick）；
    ///   · 末帧占「自己那一拍」+「末关键帧 → durationTicks−1 的钳位尾巴」两段，即
    ///     <c>帧间隔 + (durationTicks − 1 − 末关键帧.tick)</c>。
    ///
    /// 这就是「第 8 帧停两拍」的成因。要让每帧等长，末关键帧之后必须不再有尾巴，
    /// 也就是 **末关键帧.tick 必须等于 durationTicks**；循环动画按原版写法补一条
    /// 「收尾关键帧」回到第 1 张图即可（见 <c>1.7/Defs/AnimationDefs/Rukaya_WingFly.xml</c> 文件头）。
    ///
    /// 原版 XML 的约定与此一致：<c>ShamblerRise</c>、<c>ShamblerSway</c>、
    /// <c>DevourerDigesting</c>、<c>UnnaturalCorpseAwokenKilling</c> 的最后一条 keyframe
    /// 的 tick 都等于 durationTicks。原版自动生成的飞行动画
    /// （<c>Source/RimWorld/AnimationDefGenerator_Flying.cs</c>）把 keyframe 放在
    /// <c>i * ticksPerFrame</c>、总长写成 <c>(帧数 + 1) * ticksPerFrame</c>，末帧同样会长出一截——
    /// 那是原版自己的偏差，不是我们要照抄的写法。
    ///
    /// 所以扑翼档的校验**不套公式，直接按上面的规则逐 tick 推演整整一轮**，数出每张图各占多少
    /// tick，再要求它们全部相等。凡「末帧段算错」「少了收尾帧」「间隔不匀」「两层不同拍」
    /// 都会在这里现形；而"新加了一个 Root 分部"这种正常演进不会被误报。
    ///
    /// ------------------------------------------------------------------------------------
    /// 为什么有两道出口
    /// ------------------------------------------------------------------------------------
    /// 只挂 <c>AnimationDef.ConfigErrors</c> 是不够的：原版把 Def 错误检查包在
    /// <c>PlayDataLoader.cs</c> 的 <c>if (Prefs.DevMode)</c> 里，非开发者模式下一次都不会跑，
    /// 校验就形同虚设（我们实测确认过这一点）。
    ///
    /// 所以这里做两道：
    ///   1. <c>ConfigErrors</c> 的 postfix —— 开发者模式下由原版自己报出来，位置就在 Def 的错误列表里；
    ///   2. <c>Game.InitNewGame</c> 的 postfix —— **不依赖 DevMode**，开新档时把结果直接写进日志，
    ///      顺便把运行时真实的 durationTicks 与每张图的 tick 数打出来，方便和 XML 对照。
    ///
    /// 两处共用 <see cref="Validate"/>，判定逻辑只有一份。
    /// </summary>
    public static class RukayaWingAnimationValidator
    {
        /// <summary>扑翼的前后两层 tag：只有这两个 tag 需要"同拍 + 每帧等长"这档规则。</summary>
        private const string FlapBackTagName = "Rukaya_WingFlapBack";

        private const string FlapFrontTagName = "Rukaya_WingFlapFront";

        /// <summary>只校验我们自己的四个扑翼 def，避免对别的模组的动画刷屏。</summary>
        private static readonly string[] OurDefNames =
        {
            "Rukaya_WingFlap_South",
            "Rukaya_WingFlap_North",
            "Rukaya_WingFlap_East",
            "Rukaya_WingFlap_West"
        };

        /// <summary>是不是扑翼的前后两层（只有它们要按扑翼档全查）。</summary>
        private static bool IsFlapTagName(string tag)
        {
            return tag == FlapBackTagName || tag == FlapFrontTagName;
        }

        public static bool IsOurs(string defName)
        {
            if (defName == null)
            {
                return false;
            }

            for (int i = 0; i < OurDefNames.Length; i++)
            {
                if (defName == OurDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 校验一个 AnimationDef 的关键帧时序。返回空列表表示没有问题。
        ///
        /// **通用档**（所有 keyframePart，含将来新增的 <c>Root</c> / <c>Head</c> 等）：
        ///   · <c>durationTicks &gt; 0</c>；
        ///   · 关键帧 tick 严格递增；
        ///   · 末关键帧 tick == <c>durationTicks</c>（否则末尾那一段会被钳位成"停住不动"）；
        ///     只有一条关键帧的恒定分部免检这一条。
        ///
        /// **扑翼档**（只对 <c>Rukaya_WingFlapBack</c> / <c>Rukaya_WingFlapFront</c>）：
        ///   · 各段 tick 间隔相等；
        ///   · 两个 tag 的关键帧 tick 逐条相同（同拍换图）；
        ///   · 按引擎规则逐 tick 推演整轮，8 张图各占的 tick 数必须完全相等。
        /// </summary>
        public static List<string> Validate(AnimationDef def)
        {
            List<string> errors = new List<string>();

            if (def == null)
            {
                errors.Add("[Rukaya] 扑翼校验收到 null 的 AnimationDef。");
                return errors;
            }

            if (def.durationTicks <= 0)
            {
                errors.Add($"{def.defName}: durationTicks={def.durationTicks}，"
                           + "引擎会让 AnimationTick 恒为 0，动画一帧都不会换。");
                return errors;
            }

            if (def.keyframeParts == null || def.keyframeParts.Count == 0)
            {
                errors.Add($"{def.defName}: 没有 keyframeParts，动画不会有任何帧。");
                return errors;
            }

            List<Keyframe> flapReference = null;
            string flapReferenceTag = null;
            int flapInterval = -1;
            bool flapOk = true;
            int partIndex = 0;

            foreach (KeyValuePair<PawnRenderNodeTagDef, KeyframeAnimationPart> pair in def.keyframeParts)
            {
                partIndex++;
                KeyframeAnimationPart part = pair.Value;
                List<Keyframe> keys = part?.keyframes;
                string tag = pair.Key?.defName ?? $"part{partIndex}";
                bool isFlap = IsFlapTagName(tag);

                if (keys == null || keys.Count == 0)
                {
                    errors.Add($"{def.defName}: tag {tag} 没有任何关键帧。");
                    if (isFlap)
                    {
                        flapOk = false;
                    }

                    continue;
                }

                // ---- 通用档 ①：tick 必须严格递增（重复 / 倒序会让取帧退化）----
                bool ascending = true;
                for (int i = 1; i < keys.Count; i++)
                {
                    if (keys[i].tick <= keys[i - 1].tick)
                    {
                        errors.Add($"{def.defName}: tag {tag} 的关键帧 tick 非严格递增"
                                   + $"（第 {i} 条是 {keys[i - 1].tick}，第 {i + 1} 条是 {keys[i].tick}）。");
                        ascending = false;
                        break;
                    }
                }

                // ---- 通用档 ②：末关键帧必须落在 durationTicks 上（否则末尾被钳位）----
                //
                // 例外：**恒定分部**（只有一条关键帧）不需要收尾帧。它不是"循环"，而是"整个过程都取
                // 这一条"，引擎对单帧分部在整轮里恒定返回该帧（GetKeyframeData 的
                // `tick >= 末帧.tick` 分支），既没有插值也没有尾巴可钳位。
                // 我们的 Head 反向补偿就是这么写的：恒定 -8°，写第二条等于白写。
                int lastTick = keys[keys.Count - 1].tick;
                bool constantPart = keys.Count == 1;
                bool endsAtDuration = constantPart || lastTick == def.durationTicks;
                if (ascending && !endsAtDuration)
                {
                    if (lastTick > def.durationTicks)
                    {
                        errors.Add($"{def.defName}: tag {tag} 的末关键帧在 tick {lastTick}，"
                                   + $"已经越过 durationTicks={def.durationTicks}；"
                                   + "AnimationTick 只会取到 durationTicks−1，越界的关键帧永远取不到，"
                                   + "实际末帧会被它前一条一直钳位。请把末关键帧挪到 tick == durationTicks。");
                    }
                    else
                    {
                        errors.Add($"{def.defName}: tag {tag} 的末关键帧在 tick {lastTick}，"
                                   + $"而 durationTicks={def.durationTicks}；引擎把末关键帧一直钳位到循环末尾，"
                                   + $"所以 tick {lastTick} 之后的 {def.durationTicks - lastTick} tick 会停在这一条不动。"
                                   + "请把末关键帧补到 tick == durationTicks（循环动画就是回到起点的收尾帧）。");
                    }
                }

                if (!ascending || !endsAtDuration)
                {
                    if (isFlap)
                    {
                        flapOk = false;
                    }

                    continue;
                }

                // 非扑翼分部（Root / Head / 将来的其它分部）只需满足通用档：
                // 它们可以有自己的节奏，间隔不必与翅膀对齐。
                if (!isFlap)
                {
                    continue;
                }

                // ---- 以下为扑翼档 ----
                if (keys.Count < 2)
                {
                    errors.Add($"{def.defName}: tag {tag} 的关键帧少于 2 个，无法构成循环。");
                    flapOk = false;
                    continue;
                }

                int partInterval = keys[1].tick - keys[0].tick;
                for (int i = 2; i < keys.Count; i++)
                {
                    int gap = keys[i].tick - keys[i - 1].tick;
                    if (gap != partInterval)
                    {
                        errors.Add($"{def.defName}: tag {tag} 的第 {i + 1} 段间隔是 {gap}，"
                                   + $"而首段间隔是 {partInterval}；扑翼要求所有帧等长。");
                        flapOk = false;
                    }
                }

                if (flapReference == null)
                {
                    flapReference = keys;
                    flapReferenceTag = tag;
                    flapInterval = partInterval;
                    continue;
                }

                if (keys.Count != flapReference.Count || partInterval != flapInterval)
                {
                    errors.Add($"{def.defName}: tag {tag} 的帧布局（{keys.Count} 条关键帧 / 间隔 {partInterval}）"
                               + $"与 tag {flapReferenceTag} 的（{flapReference.Count} 条 / 间隔 {flapInterval}）不一致；"
                               + "前后两层用同一套帧，应当完全相同。");
                    flapOk = false;
                    continue;
                }

                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i].tick != flapReference[i].tick)
                    {
                        errors.Add($"{def.defName}: tag {tag} 的第 {i + 1} 条关键帧在 tick {keys[i].tick}，"
                                   + $"而 tag {flapReferenceTag} 在同一条是 tick {flapReference[i].tick}；"
                                   + "两层必须同拍换图，否则同一个 tick 会画出翼根与翼尖不同步的组合。");
                        flapOk = false;
                        break;
                    }
                }
            }

            if (flapReference == null)
            {
                // 没有扑翼分部（不是我们四个 def 的正常状态）：通用档已经查完。
                return errors;
            }

            if (!flapOk || flapInterval <= 0)
            {
                return errors;
            }

            if (!TryCountTicksPerFrame(def, flapReference, out List<string> frameOrder, out Dictionary<string, int> ticksPerFrame))
            {
                errors.Add($"{def.defName}: 按引擎规则推演 {def.durationTicks} tick 时有 tick 取不到关键帧"
                           + "（原版在这里会 Log.Error 并画不出图），请检查关键帧的 tick 是否严格递增。");
                return errors;
            }

            if (frameOrder.Count < 2)
            {
                errors.Add($"{def.defName}: 整轮 {def.durationTicks} tick 只出现了 {frameOrder.Count} 张图，"
                           + "扑翼等于没有换帧，请检查每条关键帧是否都填了 graphicState。");
                return errors;
            }

            int expected = def.durationTicks / frameOrder.Count;
            bool uniform = true;
            StringBuilder detail = new StringBuilder();
            for (int i = 0; i < frameOrder.Count; i++)
            {
                int count = ticksPerFrame[frameOrder[i]];
                if (count != expected)
                {
                    uniform = false;
                }

                if (i > 0)
                {
                    detail.Append('、');
                }

                detail.Append($"{ShortName(frameOrder[i])}={count}");
            }

            if (!uniform)
            {
                int lastTick = flapReference[flapReference.Count - 1].tick;
                string hint = lastTick == def.durationTicks
                    ? "当前末关键帧的 tick 已经等于 durationTicks，问题在别处（帧间隔不匀，"
                      + "或末关键帧之外还多留了一段尾巴）——请对照上面每张图各占多少 tick 判断。"
                    : "最稳的写法是：首关键帧落在 tick 0，末关键帧落在 tick == durationTicks "
                      + "并回到第 1 张图（本模组四个扑翼 def 就是这么写的）。";
                errors.Add($"{def.defName}: 每张图的展示时长不相等 —— {detail}（一整轮 {def.durationTicks} tick）。"
                           + "原因是引擎取帧取的是「第一条 tick >= 当前 tick 的关键帧」，"
                           + $"末帧还会被钳位到循环末尾，所以末帧时长 = 帧间隔({flapInterval}) "
                           + $"+ durationTicks − 1 − 末关键帧.tick = {flapInterval + def.durationTicks - 1 - lastTick}。"
                           + hint
                           + $"（当前末关键帧.tick={lastTick}、durationTicks={def.durationTicks}）");
            }

            return errors;
        }

        /// <summary>把四个 def 的校验结果一次性写进日志。不依赖 DevMode。</summary>
        public static void LogValidationReport(string reason)
        {
            List<string> problems = new List<string>();
            List<string> summary = new List<string>();

            foreach (string defName in OurDefNames)
            {
                AnimationDef def = DefDatabase<AnimationDef>.GetNamedSilentFail(defName);

                if (def == null)
                {
                    problems.Add($"[Rukaya] 扑翼校验：找不到 AnimationDef {defName}。");
                    continue;
                }

                summary.Add($"{defName}: durationTicks={def.durationTicks}, {DescribeLayout(def)}");
                problems.AddRange(Validate(def));
            }

            Log.Message($"[Rukaya] 扑翼时序校验（{reason}）：\n  " + string.Join("\n  ", summary));

            if (problems.Count == 0)
            {
                Log.Message("[Rukaya] 扑翼时序校验通过：每张图显示时长一致，末帧没有偏长/偏短。");
                return;
            }

            foreach (string problem in problems)
            {
                Log.Error("[Rukaya] 扑翼时序校验失败 —— " + problem);
            }
        }

        /// <summary>给日志用的一行摘要：关键帧布局 + 按引擎规则实际推演出的每张图 tick 数。</summary>
        private static string DescribeLayout(AnimationDef def)
        {
            if (def.keyframeParts == null || def.keyframeParts.Count == 0)
            {
                return "没有 keyframeParts";
            }

            // 摘要行只描述**扑翼分部**（Rukaya_WingFlapBack / Front）：将来若加了 Root / Head，
            // 它们的节奏与"每张图几 tick"无关，混进同一行反而看不清。
            List<Keyframe> keys = null;
            foreach (KeyValuePair<PawnRenderNodeTagDef, KeyframeAnimationPart> pair in def.keyframeParts)
            {
                if (IsFlapTagName(pair.Key?.defName))
                {
                    keys = pair.Value?.keyframes;
                    break;
                }
            }

            if (keys == null)
            {
                return "没有扑翼分部（Rukaya_WingFlapBack / Front）";
            }

            if (keys.Count < 2)
            {
                return "扑翼分部的关键帧不足 2 条";
            }

            int interval = keys[1].tick - keys[0].tick;
            int lastTick = keys[keys.Count - 1].tick;
            string head = $"{keys.Count} 条关键帧, 帧间隔={interval}, 末关键帧={lastTick}, "
                          + $"钳位尾巴={def.durationTicks - lastTick} tick";

            if (!TryCountTicksPerFrame(def, keys, out List<string> frameOrder, out Dictionary<string, int> ticksPerFrame))
            {
                return head + ", 每张图 tick 数=推演失败";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < frameOrder.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }

                sb.Append(ticksPerFrame[frameOrder[i]]);
            }

            return head + $", 每张图 tick 数=[{sb}]";
        }

        /// <summary>
        /// 按引擎规则把整整一轮（tick 0 … durationTicks−1）推演一遍，返回每张图各占多少 tick。
        /// 返回 false 表示有 tick 取不到关键帧，推演没有意义。
        /// </summary>
        private static bool TryCountTicksPerFrame(AnimationDef def, List<Keyframe> keys,
            out List<string> frameOrder, out Dictionary<string, int> ticksPerFrame)
        {
            frameOrder = new List<string>();
            ticksPerFrame = new Dictionary<string, int>();

            if (def == null || keys == null || keys.Count == 0 || def.durationTicks <= 0)
            {
                return false;
            }

            for (int tick = 0; tick < def.durationTicks; tick++)
            {
                Keyframe key = KeyframeForTick(tick, keys);
                if (key == null)
                {
                    return false;
                }

                string stateName = key.graphicState?.defName ?? "(无 graphicState)";
                if (!ticksPerFrame.ContainsKey(stateName))
                {
                    frameOrder.Add(stateName);
                    ticksPerFrame[stateName] = 0;
                }

                ticksPerFrame[stateName] = ticksPerFrame[stateName] + 1;
            }

            return true;
        }

        /// <summary>
        /// 引擎取帧规则的逐字复刻：<c>Verse/AnimationWorker_Keyframes.GetKeyframeForTick</c>。
        /// 返回 null 对应原版那条「取不到关键帧」的 Log.Error 分支。
        /// </summary>
        private static Keyframe KeyframeForTick(int tick, List<Keyframe> keys)
        {
            if (tick <= keys[0].tick)
            {
                return keys[0];
            }

            if (tick >= keys[keys.Count - 1].tick)
            {
                return keys[keys.Count - 1];
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (tick <= keys[i].tick)
                {
                    return keys[i];
                }
            }

            return null;
        }

        /// <summary>把 <c>Rukaya_WingFlapSouth_3</c> 缩成 <c>South_3</c>，只为了让日志短一点。</summary>
        private static string ShortName(string stateName)
        {
            const string prefix = "Rukaya_WingFlap";
            return stateName.StartsWith(prefix, StringComparison.Ordinal)
                ? stateName.Substring(prefix.Length)
                : stateName;
        }
    }

    /// <summary>
    /// 出口一：让原版的 Def 错误检查也能报出问题（仅在开发者模式下由原版触发）。
    /// </summary>
    [HarmonyPatch(typeof(AnimationDef), nameof(AnimationDef.ConfigErrors))]
    public static class Patch_AnimationDef_ValidateWingKeyframes
    {
        [HarmonyPostfix]
        public static IEnumerable<string> Postfix(IEnumerable<string> __result, AnimationDef __instance)
        {
            foreach (string existing in __result)
            {
                yield return existing;
            }

            if (__instance == null || !RukayaWingAnimationValidator.IsOurs(__instance.defName))
            {
                yield break;
            }

            foreach (string error in RukayaWingAnimationValidator.Validate(__instance))
            {
                yield return error;
            }
        }
    }

    /// <summary>
    /// 出口二：**不依赖 DevMode**。开新档时把校验结果与运行时真实数值写进日志。
    ///
    /// 选 <c>Game.InitNewGame</c> 是因为它在全部 Def 加载完成之后、且必然发生一次；
    /// 静态构造函数时机太早（Def 还没加载完）拿不到 DefDatabase。
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Patch_Game_ValidateWingKeyframesOnNewGame
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RukayaWingAnimationValidator.LogValidationReport("开新档时");
        }
    }
}
