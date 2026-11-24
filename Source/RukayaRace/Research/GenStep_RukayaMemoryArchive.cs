using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Research
{
    public class GenStep_RukayaMemoryArchive : GenStep
    {
        public override int SeedPart => 174205932;

        private readonly struct FacilityLayoutSpec
        {
            public readonly string Layout;
            public readonly string TargetDefName;
            public readonly int EntryX;
            public readonly bool IsMilitary;
            public readonly CellRect[] Sections;

            public FacilityLayoutSpec(string layout, string targetDefName, int entryX, bool isMilitary, CellRect[] sections)
            {
                Layout = layout;
                TargetDefName = targetDefName;
                EntryX = entryX;
                IsMilitary = isMilitary;
                Sections = sections;
            }
        }

        private static readonly Dictionary<string, FacilityLayoutSpec[]> LayoutsByMemory = new Dictionary<string, FacilityLayoutSpec[]>
        {
            ["Rukaya_CoreMemory_Refining"] = new[]
            {
                new FacilityLayoutSpec("Rukaya_RefiningArchive_Industrial", "Rukaya_Archive_Refining", 31, false,
                    new[] { new CellRect(25, 24, 5, 5), new CellRect(45, 18, 9, 8), new CellRect(8, 39, 8, 10), new CellRect(45, 44, 9, 7) }),
                new FacilityLayoutSpec("Rukaya_RefiningArchive_Compact", "Rukaya_Archive_Refining", 22, false,
                    new[] { new CellRect(23, 34, 5, 5), new CellRect(7, 23, 8, 10), new CellRect(40, 30, 9, 8), new CellRect(22, 55, 12, 7) }),
                new FacilityLayoutSpec("Rukaya_RefiningArchive_Depot", "Rukaya_Archive_Refining", 31, false,
                    new[] { new CellRect(22, 4, 18, 7), new CellRect(22, 14, 18, 22), new CellRect(4, 19, 14, 14), new CellRect(44, 19, 14, 14) }),
            },
            ["Rukaya_CoreMemory_Scanning"] = new[]
            {
                new FacilityLayoutSpec("Rukaya_ScanningArchive_Lab", "Rukaya_Archive_Scanning", 29, false,
                    new[] { new CellRect(22, 4, 14, 6), new CellRect(22, 13, 14, 20), new CellRect(4, 21, 14, 12), new CellRect(40, 21, 14, 12) }),
                new FacilityLayoutSpec("Rukaya_ScanningArchive_Wing", "Rukaya_Archive_Scanning", 32, false,
                    new[] { new CellRect(25, 4, 14, 6), new CellRect(25, 13, 14, 18), new CellRect(19, 35, 26, 8), new CellRect(4, 10, 8, 10) }),
                new FacilityLayoutSpec("Rukaya_ScanningArchive_Annex", "Rukaya_Archive_Scanning", 26, false,
                    new[] { new CellRect(19, 4, 14, 6), new CellRect(22, 14, 8, 19), new CellRect(15, 37, 22, 16), new CellRect(5, 24, 14, 10) }),
            },
            ["Rukaya_CoreMemory_Printing"] = new[]
            {
                new FacilityLayoutSpec("Rukaya_PrintingArchive_Line", "Rukaya_Archive_Printing", 32, false,
                    new[] { new CellRect(23, 4, 18, 6), new CellRect(22, 13, 20, 20), new CellRect(4, 23, 14, 14), new CellRect(46, 23, 14, 8) }),
                new FacilityLayoutSpec("Rukaya_PrintingArchive_Vault", "Rukaya_Archive_Printing", 27, false,
                    new[] { new CellRect(19, 4, 16, 6), new CellRect(19, 13, 16, 10), new CellRect(16, 27, 22, 20), new CellRect(4, 10, 11, 11) }),
                new FacilityLayoutSpec("Rukaya_PrintingArchive_Workshop", "Rukaya_Archive_Printing", 14, false,
                    new[] { new CellRect(5, 4, 18, 8), new CellRect(5, 16, 18, 16), new CellRect(28, 15, 26, 20), new CellRect(28, 4, 26, 7) }),
            },
            ["Rukaya_CoreMemory_Naval"] = new[]
            {
                new FacilityLayoutSpec("Rukaya_NavalArchive_Battery", "Rukaya_Archive_Naval", 31, true,
                    new[] { new CellRect(22, 6, 18, 7), new CellRect(22, 16, 18, 20), new CellRect(4, 18, 14, 8), new CellRect(44, 18, 14, 8) }),
                new FacilityLayoutSpec("Rukaya_NavalArchive_Bunker", "Rukaya_Archive_Naval", 28, true,
                    new[] { new CellRect(20, 6, 16, 6), new CellRect(20, 15, 16, 9), new CellRect(20, 27, 16, 18), new CellRect(4, 27, 12, 16) }),
                new FacilityLayoutSpec("Rukaya_NavalArchive_Bastion", "Rukaya_Archive_Naval", 14, true,
                    new[] { new CellRect(5, 5, 18, 8), new CellRect(28, 17, 21, 18), new CellRect(28, 4, 30, 9), new CellRect(27, 39, 15, 7) }),
            },
            ["Rukaya_CoreMemory_Mig31Memory"] = new[]
            {
                new FacilityLayoutSpec("Rukaya_Mig31Archive_Hangar", "Rukaya_Archive_Mig31Site", 38, true,
                    new[] { new CellRect(26, 26, 24, 21), new CellRect(5, 20, 17, 9), new CellRect(54, 20, 17, 9), new CellRect(26, 51, 24, 7) }),
                new FacilityLayoutSpec("Rukaya_Mig31Archive_Apron", "Rukaya_Archive_Mig31Site", 39, true,
                    new[] { new CellRect(26, 20, 26, 16), new CellRect(20, 5, 13, 8), new CellRect(45, 5, 13, 8), new CellRect(20, 38, 38, 6) }),
                new FacilityLayoutSpec("Rukaya_Mig31Archive_Revetment", "Rukaya_Archive_Mig31Site", 25, true,
                    new[] { new CellRect(18, 6, 16, 13), new CellRect(18, 23, 18, 20), new CellRect(38, 7, 12, 14), new CellRect(22, 47, 14, 13) }),
            },
        };

        public override void Generate(Map map, GenStepParams parms)
        {
            // Retain ItemStash's saved selection, including tasks created before this update.
            // The carried memory is replaced by an investigation target, never placed as loot.
            Thing selection = parms.sitePart?.things?.FirstOrDefault(t => RukayaMemoryArchives.IsMemory(t.def));
            if (selection == null)
                throw new System.InvalidOperationException("Rukaya archive has no saved memory selection.");

            if (LayoutsByMemory.TryGetValue(selection.def.defName, out FacilityLayoutSpec[] layouts) && layouts.Length > 0)
            {
                FacilityLayoutSpec spec = layouts.RandomElement();
                GenerateFacility(map, map.Center, spec, parms.sitePart?.parms.threatPoints ?? 300f);
                parms.sitePart.things.Remove(selection);
                selection.Destroy(DestroyMode.Vanish);
                return;
            }

            // Fallback for any future memory types without dedicated facility prefabs.
            string targetName = selection.def.defName == "Rukaya_CoreMemory_Mig31Memory"
                ? "Rukaya_Archive_Mig31Site"
                : selection.def.defName.Replace("Rukaya_CoreMemory_", "Rukaya_Archive_");
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamed(targetName);
            PrefabDef prefab = DefDatabase<PrefabDef>.GetNamed("Rukaya_ArchiveRoom");
            IntVec3 root = PrefabUtility.GetRoot(prefab, map.Center, Rot4.North);
            CellRect footprint = new CellRect(root.x, root.z, prefab.size.x, prefab.size.z);
            GenStep_RukayaMissileSite.Prepare(footprint, map);
            PrefabUtility.SpawnPrefab(prefab, map, map.Center, Rot4.North);
            if (ModsConfig.OdysseyActive)
            {
                PrefabDef contents = DefDatabase<PrefabDef>.GetNamed("Rukaya_ArchiveRoom_Odyssey");
                PrefabUtility.SpawnPrefab(contents, map, map.Center, Rot4.North);
            }
            foreach (var (data, local) in prefab.GetTerrain())
                if (data.def == TerrainDefOf.MetalTile)
                    map.roofGrid.SetRoof(root + local, RoofDefOf.RoofConstructed);

            IntVec3 targetPosition = root + new IntVec3(11, 0, 13);
            Thing target = GenSpawn.Spawn(targetDef, targetPosition, map);
            parms.sitePart.things.Remove(selection);
            selection.Destroy(DestroyMode.Vanish);
            if (target.TryGetComp<CompRukayaArchiveInvestigation>() == null)
                throw new System.InvalidOperationException("Rukaya archive target cannot be investigated.");

            MapGenerator.UsedRects.Add(footprint);
            MapGenerator.SetVar("RectOfInterest", footprint);
            IntVec3 entry = root + new IntVec3(11, 0, -3);
            MapGenerator.PlayerStartSpot = entry;
            GenStep_RukayaMissileSite.ClearOutdoorPath(entry + new IntVec3(0, 0, 3),
                new IntVec3(entry.x, 0, 0), new List<CellRect> { footprint.ContractedBy(1) }, map);
            MapGenerator.rootsToUnfog.RemoveAll(c => footprint.Contains(c));
        }

        // Kept for compatibility with any callers referencing GenerateRefining.
        internal static void GenerateRefining(Map map, IntVec3 center, string layout, float points)
        {
            FacilityLayoutSpec spec = LayoutsByMemory["Rukaya_CoreMemory_Refining"]
                .FirstOrDefault(s => s.Layout == layout);
            if (string.IsNullOrEmpty(spec.Layout))
                spec = LayoutsByMemory["Rukaya_CoreMemory_Refining"][0];
            GenerateFacility(map, center, spec, points);
        }

        private static void GenerateFacility(Map map, IntVec3 center, FacilityLayoutSpec spec, float points)
        {
            PrefabDef prefab = DefDatabase<PrefabDef>.GetNamed(spec.Layout);
            IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
            var footprints = new List<CellRect>();
            var hutAreas = new List<CellRect>();
            var exits = new List<IntVec3>();
            GenStep_RukayaMissileSite.SpawnModule(prefab, center, map, footprints, clearImpassableTerrain: true);
            var state = map.GetComponent<RukayaMissileSiteState>();
            state.outbuildings.Clear();
            state.layout = spec.Layout;
            state.occupants = GenStep_RukayaMissileSite.ChooseOccupants();

            string hutShellPrefix = spec.IsMilitary ? "Rukaya_MissileOutbuilding_" : "Rukaya_FacilityOutbuildingBlue_";
            var huts = new List<string> { "guard", "relay", "supply" };
            huts.Shuffle();
            IntVec3[] offsets =
            {
                new IntVec3(-16, 0, 8),
                new IntVec3(prefab.size.x + 4, 0, System.Math.Min(32, System.Math.Max(6, prefab.size.z - 14))),
                new IntVec3(8, 0, prefab.size.z + 5)
            };
            for (int index = 0; index < huts.Count; index++)
            {
                PrefabDef hut = DefDatabase<PrefabDef>.GetNamed(hutShellPrefix + huts[index]);
                IntVec3 hutRoot = root + offsets[index];
                var rect = new CellRect(hutRoot.x, hutRoot.z, hut.size.x, hut.size.z);
                if (!rect.Cells.All(cell => cell.InBounds(map))) continue;
                GenStep_RukayaMissileSite.SpawnModule(hut,
                    hutRoot + new IntVec3(hut.size.x / 2, 0, hut.size.z / 2), map, footprints,
                    clearImpassableTerrain: true,
                    contentsPrefix: "Rukaya_MissileOutbuilding_" + huts[index]);
                state.outbuildings.Add(hut.defName);
                hutAreas.Add(new CellRect(hutRoot.x + 2, hutRoot.z + 2, 9, 7));
                IntVec3 exit = huts[index] == "guard" ? new IntVec3(8, 0, 0)
                    : huts[index] == "relay" ? new IntVec3(12, 0, 4) : new IntVec3(0, 0, 4);
                exits.Add(hutRoot + exit);
            }

            ThingDef targetDef = DefDatabase<ThingDef>.GetNamed(spec.TargetDefName);
            List<Thing> targets = map.listerThings.ThingsOfDef(targetDef);
            if (targets.Count != 1 || targets[0].TryGetComp<CompRukayaArchiveInvestigation>() == null
                || !new CellRect(root.x, root.z, prefab.size.x, prefab.size.z).Contains(targets[0].Position))
                throw new System.InvalidOperationException($"Rukaya archive {spec.Layout} must contain exactly one investigation target ({spec.TargetDefName}).");

            IntVec3 entry = root + new IntVec3(spec.EntryX, 0, -3);
            MapGenerator.PlayerStartSpot = entry;
            GenStep_RukayaMissileSite.ClearOutdoorPath(entry + new IntVec3(0, 0, 3),
                new IntVec3(entry.x, 0, 0), footprints, map);
            foreach (IntVec3 exit in exits)
                GenStep_RukayaMissileSite.ClearOutdoorPath(exit, entry, footprints, map);
            MapGenerator.rootsToUnfog.RemoveAll(cell => footprints.Any(rect => rect.Contains(cell)));
            MapGenerator.SetVar("RectOfInterest", footprints[0]);
            if (Find.Storyteller.difficulty.allowViolentQuests)
                GenStep_RukayaMissileSite.SpawnOccupants(map, root, state.occupants, points, hutAreas, spec.Sections);
        }
    }
}
