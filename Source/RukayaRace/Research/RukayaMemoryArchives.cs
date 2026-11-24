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
        private static List<ResearchProjectDef> cachedProjects;
        private static List<ThingDef> cachedArchiveInvestigationDefs;
        public static bool IsAirDefense(ThingDef memory) => memory?.defName == "Rukaya_CoreMemory_AirDefense";

        public static IReadOnlyList<ResearchProjectDef> Projects
        {
            get
            {
                if (cachedProjects != null) return cachedProjects;
                List<ResearchProjectDef> list = new List<ResearchProjectDef>();
                List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ResearchProjectDef p = all[i];
                    if (p.requiredAnalyzed != null && p.requiredAnalyzed.Any(IsMemory))
                        list.Add(p);
                }
                cachedProjects = list;
                return list;
            }
        }

        internal static List<ThingDef> ArchiveInvestigationDefs
        {
            get
            {
                if (cachedArchiveInvestigationDefs != null) return cachedArchiveInvestigationDefs;
                List<ThingDef> list = new List<ThingDef>();
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].HasComp(typeof(CompRukayaArchiveInvestigation)))
                        list.Add(all[i]);
                }
                cachedArchiveInvestigationDefs = list;
                return list;
            }
        }

        public static bool IsMemory(ThingDef def) => def?.defName.StartsWith("Rukaya_CoreMemory_") == true;

        public static ResearchProjectDef ProjectFor(ThingDef memory) => Projects.First(p => p.requiredAnalyzed.Contains(memory));

        public static List<ThingDef> Candidates(bool refresh = false) => Current.Game
            .GetComponent<GameComponent_RukayaMemoryArchives>().Candidates(refresh);

        public static void Invalidate() => Current.Game?.GetComponent<GameComponent_RukayaMemoryArchives>().Invalidate();

        public static bool TryGenerate(Map map, Pawn worker, out Quest quest)
        {
            quest = null;
            if (map == null) return false;
            List<ThingDef> list = Candidates(true);
            if (list.Count == 0) return false;
            ThingDef selected = list.RandomElement();
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

        private static bool MapHasUnrecoveredArchive(Map map, ThingDef memory)
        {
            List<ThingDef> defs = RukayaMemoryArchives.ArchiveInvestigationDefs;
            for (int d = 0; d < defs.Count; d++)
            {
                List<Thing> things = map.listerThings.ThingsOfDef(defs[d]);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].TryGetComp<CompRukayaArchiveInvestigation>() is CompRukayaArchiveInvestigation comp
                        && !comp.Recovered && comp.Props.memory == memory)
                        return true;
                }
            }
            return false;
        }

        public List<ThingDef> Candidates(bool refresh)
        {
            int tick = Find.TickManager.TicksGame;
            if (!refresh && candidates != null && tick >= cacheTick && tick - cacheTick < 600) return candidates;
            cacheTick = tick;
            candidates = new List<ThingDef>();
            var available = new HashSet<Thing>();
            var tmp = new List<Thing>();
            IReadOnlyList<ResearchProjectDef> projects = RukayaMemoryArchives.Projects;
            foreach (Map map in Find.Maps)
            {
                for (int i = 0; i < projects.Count; i++)
                {
                    List<Thing> mapMemories = map.listerThings.ThingsOfDef(projects[i].requiredAnalyzed[0]);
                    for (int m = 0; m < mapMemories.Count; m++) available.Add(mapMemories[m]);
                }
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int p = 0; p < pawns.Count; p++)
                {
                    Pawn pawn = pawns[p];
                    if (pawn.carryTracker?.CarriedThing is Thing carried && RukayaMemoryArchives.IsMemory(carried.def))
                        available.Add(carried);
                    ThingOwner<Thing> inv = pawn.inventory?.innerContainer;
                    if (inv != null)
                    {
                        for (int j = 0; j < inv.Count; j++)
                            if (RukayaMemoryArchives.IsMemory(inv[j].def)) available.Add(inv[j]);
                    }
                }
                List<Thing> transporters = map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter);
                for (int t = 0; t < transporters.Count; t++)
                {
                    if (transporters[t] is IThingHolder holder)
                    {
                        tmp.Clear();
                        ThingOwnerUtility.GetAllThingsRecursively(holder, tmp, allowUnreal: false);
                        for (int j = 0; j < tmp.Count; j++)
                            if (RukayaMemoryArchives.IsMemory(tmp[j].def)) available.Add(tmp[j]);
                    }
                }
            }
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj.Faction != Faction.OfPlayer || !(obj is IThingHolder holder)) continue;
                tmp.Clear();
                ThingOwnerUtility.GetAllThingsRecursively(holder, tmp, allowUnreal: false);
                for (int j = 0; j < tmp.Count; j++)
                    if (RukayaMemoryArchives.IsMemory(tmp[j].def)) available.Add(tmp[j]);
            }
            List<Site> sites = Find.WorldObjects.Sites;
            for (int pIdx = 0; pIdx < projects.Count; pIdx++)
            {
                ResearchProjectDef project = projects[pIdx];
                if (project.IsFinished || project.AnalyzedThingsRequirementsMet) continue;
                ThingDef memory = project.requiredAnalyzed[0];
                var props = memory.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>();
                Find.AnalysisManager.TryGetAnalysisProgress(props.analysisID, out var progress);
                int done = progress?.timesDone ?? 0;
                int owned = 0;
                foreach (Thing t in available)
                    if (!t.Destroyed && t.def == memory) owned += t.stackCount;
                bool outstanding = false;
                for (int sIdx = 0; sIdx < sites.Count && !outstanding; sIdx++)
                {
                    Site s = sites[sIdx];
                    string mainDefName = s.MainSitePartDef.defName;
                    for (int partIdx = 0; partIdx < s.parts.Count; partIdx++)
                    {
                        SitePart part = s.parts[partIdx];
                        if (part.def.defName == RukayaMemoryArchives.SitePartName && part.things != null)
                        {
                            for (int ti = 0; ti < part.things.Count; ti++)
                                if (part.things[ti].def == memory) { outstanding = true; break; }
                        }
                        if (outstanding) break;
                    }
                    if (outstanding) break;
                    if (mainDefName == RukayaMemoryArchives.SitePartName || mainDefName == RukayaMemoryArchives.MissileSiteName)
                    {
                        if (s.HasMap)
                        {
                            if (s.Map.listerThings.ThingsOfDef(memory).Count > 0 || MapHasUnrecoveredArchive(s.Map, memory))
                                outstanding = true;
                        }
                        else if (RukayaMemoryArchives.IsAirDefense(memory) && mainDefName == RukayaMemoryArchives.MissileSiteName)
                        {
                            outstanding = true;
                        }
                    }
                }
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
