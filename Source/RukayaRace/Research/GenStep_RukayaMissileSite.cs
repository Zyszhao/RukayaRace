using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RukayaRace.Research
{
    public enum RukayaRuinOccupants { Mechanoids, Rukaya, Fleshbeasts, Pirates }

    // New choices are saved without regenerating any buildings or pawns from existing maps.
    public class RukayaMissileSiteState : MapComponent
    {
        public string layout;
        public RukayaRuinOccupants occupants;
        public List<string> outbuildings = new List<string>();
        public RukayaMissileSiteState(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref layout, "rukayaRuinLayout");
            Scribe_Values.Look(ref occupants, "rukayaRuinOccupants");
            Scribe_Collections.Look(ref outbuildings, "rukayaRuinOutbuildings", LookMode.Value);
        }
    }

    public class GenStep_RukayaMissileSite : GenStep
    {
        public override int SeedPart => 174205931;
        public static bool UsesRuins => ModsConfig.OdysseyActive || ModsConfig.IdeologyActive;
        public static string ContentsTheme => ModsConfig.OdysseyActive ? "Odyssey"
            : ModsConfig.IdeologyActive ? "Ideology" : "CoreFallback";

        public static string ChooseLayout() => new[] { "Rukaya_AbandonedMissileSite",
            "Rukaya_AbandonedMissileSite_Workshop", "Rukaya_AbandonedMissileSite_Barracks" }.RandomElement();

        public static List<RukayaRuinOccupants> AvailableOccupants()
        {
            var choices = new List<RukayaRuinOccupants> { RukayaRuinOccupants.Mechanoids };
            if (RukayaFaction != null) choices.Add(RukayaRuinOccupants.Rukaya);
            if (ModsConfig.AnomalyActive && Faction.OfEntities != null) choices.Add(RukayaRuinOccupants.Fleshbeasts);
            if (PirateFaction != null) choices.Add(RukayaRuinOccupants.Pirates);
            return choices;
        }

        public static RukayaRuinOccupants ChooseOccupants() => AvailableOccupants().RandomElement();
        private static Faction RukayaFaction => Find.FactionManager.AllFactionsListForReading
            .FirstOrDefault(f => f.def.defName == "Rukaya_DrifterAlliance" && !f.defeated && !f.IsPlayer);
        // Faction.OfPirates only finds the baseline gang, not Biotech's waster/yttakin pirate factions.
        private static Faction PirateFaction => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
            !f.defeated && (f.def.defName == "Pirate" || f.def.defName == "PirateYttakin"
                || f.def.defName == "PirateWaster" || f.def.defName == "CannibalPirate"));

        public override void Generate(Map map, GenStepParams parms)
        {
            var state = map.GetComponent<RukayaMissileSiteState>();
            state.layout = ChooseLayout();
            state.occupants = ChooseOccupants();
            PrefabDef prefab = DefDatabase<PrefabDef>.GetNamed(state.layout);
            IntVec3 root = PrefabUtility.GetRoot(prefab, map.Center, Rot4.North);
            var footprints = new List<CellRect>();
            var hutAreas = new List<CellRect>();
            var hutExits = new List<IntVec3>();
            SpawnModule(prefab, map.Center, map, footprints);

            // Each detached hut has its own sealed shell and several open cells to the main building.
            var offsets = new List<IntVec3> { new IntVec3(-17, 0, 6), new IntVec3(-16, 0, 43),
                new IntVec3(73, 0, 17), new IntVec3(52, 0, 68) };
            offsets.Shuffle();
            var huts = new List<string> { "guard", "relay", "supply" };
            huts.Shuffle();
            for (int i = 0, count = Rand.RangeInclusive(2, 3); i < count; i++)
            {
                PrefabDef hut = DefDatabase<PrefabDef>.GetNamed("Rukaya_MissileOutbuilding_" + huts[i]);
                IntVec3 center = root + offsets[i] + new IntVec3(hut.size.x / 2, 0, hut.size.z / 2);
                CellRect footprint = new CellRect(root.x + offsets[i].x, root.z + offsets[i].z, hut.size.x, hut.size.z);
                // Older queued sites may request smaller maps; omit a hut instead of spawning outside the map.
                if (!footprint.Cells.All(c => c.InBounds(map))) continue;
                SpawnModule(hut, center, map, footprints);
                state.outbuildings.Add(hut.defName);
                hutAreas.Add(new CellRect(footprint.minX + 2, footprint.minZ + 2, 9, 7));
                IntVec3 exit = huts[i] == "guard" ? new IntVec3(8, 0, 0)
                    : huts[i] == "relay" ? new IntVec3(12, 0, 4) : new IntVec3(0, 0, 4);
                hutExits.Add(root + offsets[i] + exit);
            }

            CellRect approach = new CellRect(root.x + 32, 0, 5, root.z + 2);
            Prepare(approach, map);
            foreach (IntVec3 c in approach.Cells) map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            MapGenerator.PlayerStartSpot = root + new IntVec3(34, 0, -4);
            foreach (IntVec3 exit in hutExits) ClearOutdoorPath(exit, MapGenerator.PlayerStartSpot, footprints, map);
            MapGenerator.rootsToUnfog.RemoveAll(c => footprints.Any(r => r.Contains(c)));

            ThingDef targetDef = DefDatabase<ThingDef>.GetNamed("Rukaya_SAMLauncher_Abandoned");
            if (map.listerThings.ThingsOfDef(targetDef).Count != 1)
                throw new System.InvalidOperationException("Rukaya missile site must contain exactly one investigation target.");

            if (Find.Storyteller.difficulty.allowViolentQuests)
                SpawnOccupants(map, root, state.occupants, parms.sitePart?.parms.threatPoints ?? 300f, hutAreas);
        }

        private static void SpawnModule(PrefabDef prefab, IntVec3 center, Map map, List<CellRect> footprints)
        {
            IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
            CellRect footprint = new CellRect(root.x, root.z, prefab.size.x, prefab.size.z);
            Prepare(footprint, map);
            ThingDef wall = ModsConfig.OdysseyActive ? DefDatabase<ThingDef>.GetNamed("AncientFortifiedWall") : null;
            PrefabUtility.SpawnPrefab(prefab, map, center, Rot4.North, overrideSpawnData: data =>
                wall != null && data.def == ThingDefOf.Wall ? System.Tuple.Create<ThingDef, ThingDef>(wall, null) : null);
            PrefabUtility.SpawnPrefab(DefDatabase<PrefabDef>.GetNamed(prefab.defName + "_" + ContentsTheme), map, center, Rot4.North);
            foreach (var (data, local) in prefab.GetTerrain())
            {
                IntVec3 cell = root + local;
                bool indoors = data.def == TerrainDefOf.PavedTile || data.def == TerrainDefOf.MetalTile;
                if (indoors) map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                if (UsesRuins) map.terrainGrid.SetTerrain(cell,
                    DefDatabase<TerrainDef>.GetNamed(data.def == TerrainDefOf.Concrete ? "AncientConcrete" : "AncientTile"));
            }
            footprints.Add(footprint);
            MapGenerator.UsedRects.Add(footprint);
        }

        private static void SpawnOccupants(Map map, IntVec3 root, RukayaRuinOccupants theme, float points, List<CellRect> hutAreas)
        {
            Faction faction = theme == RukayaRuinOccupants.Rukaya ? RukayaFaction
                : theme == RukayaRuinOccupants.Pirates ? PirateFaction
                : theme == RukayaRuinOccupants.Fleshbeasts ? Faction.OfEntities : Faction.OfMechanoids;
            int count = theme == RukayaRuinOccupants.Mechanoids ? (points >= 800f ? 3 : points >= 400f ? 2 : 1)
                : theme == RukayaRuinOccupants.Fleshbeasts ? UnityEngine.Mathf.Clamp((int)(points / 120f) + 1, 2, 8)
                : UnityEngine.Mathf.Clamp((int)(points / 180f) + 1, 2, 6);
            CellRect[] sections = { new CellRect(36, 32, 4, 4), new CellRect(48, 16, 7, 10),
                new CellRect(9, 51, 10, 7), new CellRect(4, 16, 16, 8), new CellRect(48, 40, 16, 6) };
            var sleeping = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                CellRect area = sections[i % sections.Length].MovedBy(root);
                bool inHut = count > 1 && i == count - 1 && hutAreas.Count > 0;
                if (inHut) area = hutAreas.RandomElement();
                List<IntVec3> cells = area.Cells.Where(c => c.Standable(map)
                    && (!inHut || c.Roofed(map)) && c.GetEdifice(map) == null && c.GetFirstPawn(map) == null).ToList();
                if (cells.Count == 0) continue;
                IntVec3 cell = cells.RandomElement();
                string kind = theme == RukayaRuinOccupants.Rukaya
                    ? new[] { "Rukaya_Militia", "Rukaya_MilitiaMelee", "Rukaya_RegularRifleman" }.RandomElement()
                    : theme == RukayaRuinOccupants.Pirates ? new[] { "Pirate", "Drifter", "Scavenger" }.RandomElement()
                    : theme == RukayaRuinOccupants.Fleshbeasts ? (i % 3 == 0 && points >= 400f ? "Trispike" : "Fingerspike")
                    : i == 2 ? "Mech_Lancer" : "Mech_Scyther";
                Pawn pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind), faction);
                GenSpawn.Spawn(pawn, cell, map);
                if ((theme == RukayaRuinOccupants.Mechanoids || theme == RukayaRuinOccupants.Fleshbeasts)
                    && pawn.GetComp<CompCanBeDormant>() != null)
                {
                    CompWakeUpDormant wake = pawn.GetComp<CompWakeUpDormant>();
                    if (wake != null) wake.wakeUpIfTargetClose = true;
                    LordMaker.MakeNewLord(faction, new LordJob_SleepThenAssaultColony(faction) { wakeOnPawnUnfogged = true }, map, new[] { pawn });
                    sleeping.Add(pawn);
                }
                else
                {
                    // Preserve diplomacy, including friendly or neutral Rukaya inhabitants.
                    LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(cell, 2f, 8f), map, new[] { pawn });
                }
            }
            GenStep_SleepingMechanoids.SendMechanoidsToSleepImmediately(sleeping);
        }

        internal static void Prepare(CellRect rect, Map map)
        {
            foreach (IntVec3 c in rect.Cells.Where(c => c.InBounds(map)))
            {
                foreach (Thing thing in c.GetThingList(map).ToList())
                    if (thing is Building || thing is Plant || thing.def.category == ThingCategory.Item || thing.def.category == ThingCategory.Filth)
                        thing.Destroy(DestroyMode.Vanish);
                map.roofGrid.SetRoof(c, null);
                if (!c.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy))
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Gravel);
            }
        }

        internal static void ClearOutdoorPath(IntVec3 start, IntVec3 goal, List<CellRect> footprints, Map map)
        {
            // Natural mountains/water must not strand a detached hut. Route outside every prefab; no enclosed connector.
            var queue = new Queue<IntVec3>();
            var previous = new Dictionary<IntVec3, IntVec3> { [start] = start };
            queue.Enqueue(start);
            while (queue.Count > 0 && !previous.ContainsKey(goal))
            {
                IntVec3 current = queue.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = current + direction;
                    if (!next.InBounds(map) || previous.ContainsKey(next) || footprints.Any(r => r.Contains(next))) continue;
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }
            if (!previous.ContainsKey(goal)) return;
            for (IntVec3 cell = goal; ; cell = previous[cell])
            {
                Prepare(new CellRect(cell.x, cell.z, 1, 1), map);
                if (cell.GetTerrain(map).passability == Traversability.Impassable)
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
                if (cell == start) break;
            }
        }
    }
}
