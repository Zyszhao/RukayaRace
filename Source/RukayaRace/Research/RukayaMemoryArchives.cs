using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace RukayaRace.Research
{
    public static class RukayaMemoryArchives
    {
        public const string QuestName = "Rukaya_MemoryArchive";
        public const string SitePartName = "Rukaya_MemoryArchive";
        public const string MissileSiteName = "Rukaya_AbandonedMissileSite";
        public static bool IsAirDefense(ThingDef memory) => memory?.defName == "Rukaya_CoreMemory_AirDefense";

        public static IEnumerable<ResearchProjectDef> Projects => DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(p => p.requiredAnalyzed != null && p.requiredAnalyzed.Any(IsMemory));

        public static bool IsMemory(ThingDef def) => def?.defName.StartsWith("Rukaya_CoreMemory_") == true;

        public static ResearchProjectDef ProjectFor(ThingDef memory) => Projects.First(p => p.requiredAnalyzed.Contains(memory));

        public static List<ThingDef> Candidates(bool refresh = false) => Current.Game
            .GetComponent<GameComponent_RukayaMemoryArchives>().Candidates(refresh);

        public static void Invalidate() => Current.Game?.GetComponent<GameComponent_RukayaMemoryArchives>().Invalidate();

        public static bool TryGenerate(Map map, Pawn worker, out Quest quest)
        {
            quest = null;
            if (map == null || !Candidates(true).Any()) return false;
            ThingDef selected = Candidates(true).RandomElement();
            QuestScriptDef def = DefDatabase<QuestScriptDef>.GetNamed(IsAirDefense(selected) ? MissileSiteName : QuestName);
            Slate slate = new Slate();
            slate.Set("map", map);
            slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(map));
            slate.Set("worker", worker);
            slate.Set("rukayaSelectedMemory", selected);
            if (!def.CanRun(slate, map)) return false;
            quest = QuestUtility.GenerateQuestAndMakeAvailable(def, slate);
            Invalidate();
            if (quest == null) return false;
            QuestUtility.SendLetterQuestAvailable(quest);
            return true;
        }
    }

    // Selection is derived from live items and sites, so lost/expired rewards cannot permanently block a technology.
    public class GameComponent_RukayaMemoryArchives : GameComponent
    {
        private bool initialized;
        private int cacheTick = -9999;
        private List<ThingDef> candidates;

        public GameComponent_RukayaMemoryArchives(Game game) { }

        public void Invalidate() { candidates = null; }

        public override void ExposeData() => Scribe_Values.Look(ref initialized, "rukayaMemoryArchivesInitialized", false);

        public override void StartedNewGame()
        {
            InitializeTasks();
            initialized = true;
        }

        public override void LoadedGame()
        {
            InitializeTasks();
            if (!initialized)
            {
                foreach (ResearchProjectDef project in RukayaMemoryArchives.Projects)
                {
                    if (project.IsFinished || Find.ResearchManager.IsCurrentProject(project))
                        Find.AnalysisManager.ForceCompleteAnalysisProgress(project.requiredAnalyzed[0]
                            .GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>().analysisID);
                }
                // The old advanced-weapons project already granted naval artillery construction.
                if (DefDatabase<ResearchProjectDef>.GetNamed("Rukaya_Weapon_III").IsFinished)
                    Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Rukaya_NavalArtillery"),
                        doCompletionLetter: false);
                initialized = true;
            }
            Invalidate();
        }

        private static void InitializeTasks()
        {
            foreach (ResearchProjectDef project in RukayaMemoryArchives.Projects)
            {
                var props = project.requiredAnalyzed[0].GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>();
                Find.AnalysisManager.AddAnalysisTask(props.analysisID, props.analysisRequiredRange.min);
            }
        }

        public List<ThingDef> Candidates(bool refresh)
        {
            int tick = Find.TickManager.TicksGame;
            if (!refresh && candidates != null && tick >= cacheTick && tick - cacheTick < 250) return candidates;
            cacheTick = tick;
            candidates = new List<ThingDef>();
            var available = new HashSet<Thing>();
            var tmp = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, tmp, allowUnreal: false);
                foreach (Thing thing in tmp) if (RukayaMemoryArchives.IsMemory(thing.def)) available.Add(thing);
            }
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj.Faction != Faction.OfPlayer || !(obj is IThingHolder holder)) continue;
                ThingOwnerUtility.GetAllThingsRecursively(holder, tmp, allowUnreal: false);
                foreach (Thing thing in tmp) if (RukayaMemoryArchives.IsMemory(thing.def)) available.Add(thing);
            }
            foreach (ResearchProjectDef project in RukayaMemoryArchives.Projects)
            {
                if (project.IsFinished || project.AnalyzedThingsRequirementsMet) continue;
                ThingDef memory = project.requiredAnalyzed[0];
                var props = memory.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>();
                Find.AnalysisManager.TryGetAnalysisProgress(props.analysisID, out var progress);
                int done = progress?.timesDone ?? 0;
                int owned = available.Where(t => !t.Destroyed && t.def == memory).Sum(t => t.stackCount);
                bool outstanding = Find.WorldObjects.Sites.Any(s => s.parts.Any(p =>
                    p.def.defName == RukayaMemoryArchives.SitePartName && p.things != null && p.things.Any(t => t.def == memory))
                    || (s.MainSitePartDef.defName == RukayaMemoryArchives.SitePartName && s.HasMap
                        && s.Map.listerThings.ThingsOfDef(memory).Count > 0));
                outstanding |= Find.WorldObjects.Sites.Any(s =>
                    (s.MainSitePartDef.defName == RukayaMemoryArchives.SitePartName
                        || s.MainSitePartDef.defName == RukayaMemoryArchives.MissileSiteName)
                    && (s.HasMap ? s.Map.listerThings.ThingsOfDef(memory).Count > 0
                        || s.Map.listerThings.AllThings.Any(t =>
                            t.TryGetComp<CompRukayaArchiveInvestigation>() is CompRukayaArchiveInvestigation comp
                            && !comp.Recovered && comp.Props.memory == memory)
                        : RukayaMemoryArchives.IsAirDefense(memory)
                            && s.MainSitePartDef.defName == RukayaMemoryArchives.MissileSiteName));
                if (!outstanding && done + owned < props.analysisRequiredRange.min) candidates.Add(memory);
            }
            return candidates;
        }
    }

    public class QuestNode_RukayaSelectMemory : QuestNode
    {
        public bool missileSite;
        private List<ThingDef> Pool() => RukayaMemoryArchives.Candidates(true)
            .Where(t => RukayaMemoryArchives.IsAirDefense(t) == missileSite).ToList();

        protected override bool TestRunInt(Slate slate) => Pool().Count > 0;

        protected override void RunInt()
        {
            List<ThingDef> pool = Pool();
            ThingDef memory = QuestGen.slate.Get<ThingDef>("rukayaSelectedMemory");
            if (memory == null || !pool.Contains(memory)) memory = pool.RandomElement();
            QuestGen.slate.Set("itemStashSingleThing", memory);
            QuestGen.slate.Set("memoryLabel", memory.LabelCap.ToString());
            QuestGen.slate.Set("memoryResearch", RukayaMemoryArchives.ProjectFor(memory).LabelCap.ToString());
        }
    }

    // This site suppresses its own guards on peaceful difficulty; it remains a valid exploration task.
    public class QuestNode_RukayaGenerateMissileSite : QuestNode_GenerateSite
    {
        protected override bool TestRunInt(Slate slate) => sitePartsParams.GetValue(slate)?.All(
            p => p.def.defName == RukayaMemoryArchives.MissileSiteName) == true;
    }
}
