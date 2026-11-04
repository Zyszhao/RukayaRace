using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    /// <summary>
    /// 剧本开局把"玩家阵营（Rukaya_PlayerFaction）↔ NPC 卢卡娅阵营（Rukaya_DrifterAlliance）"
    /// 的好感度直接设成 100 —— 两者是同族。
    ///
    /// 自然好感度（需求方要求 75）不在本类里做：原版 <c>Faction.NaturalGoodwill</c> 是
    /// <c>GoodwillSituationManager.GetNaturalGoodwill</c> 累加所有 <c>GoodwillSituationDef</c> 的偏移
    /// 算出来的，没有存档字段，因此由 <see cref="GoodwillSituationWorker_RukayaKin"/> +
    /// <c>GoodwillSituationDef Rukaya_KinRelations</c> 提供。
    /// </summary>
    public class ScenPart_RukayaKinRelations : ScenPart
    {
        /// <summary>开局给玩家阵营定的好感度。</summary>
        private const int InitialGoodwill = 100;

        public override void PostWorldGenerate()
        {
            Faction player = Find.GameInitData?.playerFaction;
            if (player == null)
            {
                return;
            }
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            {
                if (other == null || other == player || other.def != RukayaScenarioDefOf.Rukaya_DrifterAlliance)
                {
                    continue;
                }
                if (!player.HasGoodwill || !other.HasGoodwill)
                {
                    continue;
                }
                int delta = InitialGoodwill - player.BaseGoodwillWith(other);
                if (delta != 0)
                {
                    // 开局阶段不弹信件、不触发敌意变化提示。
                    player.TryAffectGoodwillWith(other, delta, canSendMessage: false, canSendHostilityLetter: false);
                }
            }
        }
    }
}
