using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    /// <summary>
    /// "三名同伴到来"剧本事件的数据载体。
    ///
    /// 原版没有任何办法在事件里指定刷出 pawn 的名字：
    /// <c>IncidentParms</c> / <c>QuestNode_GeneratePawn</c> / <c>QuestGen_Pawns</c> 都没有名字字段，
    /// <c>PawnGenerationRequest</c> 也只有 <c>FixedLastName</c> / <c>FixedBirthName</c>（没有 firstname）。
    /// 所以这里在 <see cref="IncidentDef"/> 上挂一份 firstname 列表，
    /// 由 <see cref="IncidentWorker_RukayaTrioArrival"/> 在生成完成之后写进 pawn.Name：
    /// firstname 强制，姓氏 / 特质 / 背景故事 / 服装随机；剧本到来者使用玩家主文化。
    /// </summary>
    public class IncidentDef_RukayaTrioArrival : IncidentDef
    {
        /// <summary>
        /// 按抵达顺序强制使用的 firstname。
        /// 中文侧由 DefInjected 覆盖（<c>Rukaya_TrioArrival.firstNames</c>），
        /// 取值与 <c>Content/Languages/*/Strings/Names/Rukaya_First.txt</c> 的前三项保持一致。
        ///
        /// 必须带 <see cref="TranslationCanChangeCountAttribute"/>：原版 <c>DefInjectionPackage</c> 只接受
        /// 「字符串字段」或「带该属性的 List&lt;string&gt; 字段」的翻译；缺这个属性时整表注入会被拒绝、
        /// def 里的英文原名会留在游戏里 —— 这正是 2026-10-08 实测「三个 pawn 名字是英文」的原因。
        /// </summary>
        [TranslationCanChangeCount]
        public List<string> firstNames = new List<string>();

        /// <summary>
        /// 与 <see cref="firstNames"/> **按下标一一对应**的自带武器：事件生成时就装备好，
        /// 与剧本发给开局角色的那把枪无关。数量不足时该人不发武器。
        /// </summary>
        public List<ThingDef> weapons = new List<ThingDef>();

        /// <summary>
        /// 与 <see cref="firstNames"/> **按下标一一对应**：true = 该角色必定带一道头部伤疤 + 一道眼部伤疤
        /// （轻微伤疤，几乎不影响数值）。其余伤疤与仿生体会被一律清除。
        /// </summary>
        public List<bool> scarHeadAndEye = new List<bool>();

        /// <summary>三张表必须等长，否则名字 / 武器 / 伤疤会错位到别人身上。</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (weapons != null && firstNames != null && weapons.Count != firstNames.Count)
            {
                yield return "weapons count (" + weapons.Count + ") must match firstNames count (" + firstNames.Count + ")";
            }
            if (scarHeadAndEye != null && firstNames != null && scarHeadAndEye.Count != firstNames.Count)
            {
                yield return "scarHeadAndEye count (" + scarHeadAndEye.Count + ") must match firstNames count (" + firstNames.Count + ")";
            }
        }
    }
}
