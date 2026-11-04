using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    /// <summary>
    /// 给 NPC 卢卡娅阵营（Rukaya_DrifterAlliance）提供自然好感度 = Def 里配置的
    /// <c>naturalGoodwillOffset</c>（75）。
    ///
    /// 为什么必须做成"局势 + worker"：原版自然好感度不是存档字段，而是
    /// <c>GoodwillSituationManager.GetNaturalGoodwill</c> 累加所有 <c>GoodwillSituationDef</c> 里
    /// <c>Worker.GetNaturalGoodwillOffset</c> 的结果（每 1000 tick 重算一次），没有"直接写入"的 API。
    /// 这里再用"当前剧本是否带 ScenPart_RukayaKinRelations"把生效范围限定在本剧本内，
    /// 而不是全模组所有剧本都享受同族加成。
    /// </summary>
    public class GoodwillSituationWorker_RukayaKin : GoodwillSituationWorker
    {
        public override int GetNaturalGoodwillOffset(Faction other)
        {
            if (other == null || other.def != RukayaScenarioDefOf.Rukaya_DrifterAlliance)
            {
                return 0;
            }
            return ScenarioHasKinRelations() ? this.def.naturalGoodwillOffset : 0;
        }

        private static bool ScenarioHasKinRelations()
        {
            // 注意：本文件所在命名空间叫 RukayaRace.Scenario，会遮蔽 RimWorld.Scenario 类型，必须写全限定名。
            RimWorld.Scenario scenario = Find.Scenario;
            return scenario != null && scenario.AllParts.Any(part => part is ScenPart_RukayaKinRelations);
        }
    }
}
