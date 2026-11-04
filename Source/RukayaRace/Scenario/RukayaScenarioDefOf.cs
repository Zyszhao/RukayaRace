using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    /// <summary>本模组剧本相关 Def 的引用（与 <c>RukayaActiveMatterDefOf</c> 同一套写法）。</summary>
    [DefOf]
    public static class RukayaScenarioDefOf
    {
        /// <summary>世界上的 NPC 卢卡娅阵营（玩家阵营是 <c>Rukaya_PlayerFaction</c>）。</summary>
        public static FactionDef Rukaya_DrifterAlliance;

        /// <summary>"玩家 ↔ NPC 卢卡娅"之间的同族亲情局势，提供自然好感度。</summary>
        public static GoodwillSituationDef Rukaya_KinRelations;

        static RukayaScenarioDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaScenarioDefOf));
        }
    }
}
