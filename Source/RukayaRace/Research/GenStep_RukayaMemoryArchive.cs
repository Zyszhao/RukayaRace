using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Research
{
    public class GenStep_RukayaMemoryArchive : GenStep
    {
        public override int SeedPart => 174205932;

        public override void Generate(Map map, GenStepParams parms)
        {
            // Retain ItemStash's saved selection, including tasks created before this update.
            // The carried memory is replaced by an investigation target, never placed as loot.
            Thing selection = parms.sitePart?.things?.FirstOrDefault(t => RukayaMemoryArchives.IsMemory(t.def));
            if (selection == null)
                throw new System.InvalidOperationException("Rukaya archive has no saved memory selection.");
            string targetName = selection.def.defName.Replace("Rukaya_CoreMemory_", "Rukaya_Archive_");
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamed(targetName);
            PrefabDef prefab = DefDatabase<PrefabDef>.GetNamed("Rukaya_ArchiveRoom");
            IntVec3 root = PrefabUtility.GetRoot(prefab, map.Center, Rot4.North);
            CellRect footprint = new CellRect(root.x, root.z, prefab.size.x, prefab.size.z);
            GenStep_RukayaMissileSite.Prepare(footprint, map);
            PrefabUtility.SpawnPrefab(prefab, map, map.Center, Rot4.North);
            foreach (var (data, local) in prefab.GetTerrain())
                if (data.def == TerrainDefOf.MetalTile)
                    map.roofGrid.SetRoof(root + local, RoofDefOf.RoofConstructed);

            Thing target = GenSpawn.Spawn(targetDef, root + new IntVec3(11, 0, 13), map);
            parms.sitePart.things.Remove(selection);
            selection.Destroy(DestroyMode.Vanish);
            if (target.TryGetComp<CompRukayaArchiveInvestigation>() == null)
                throw new System.InvalidOperationException("Rukaya archive target cannot be investigated.");

            MapGenerator.UsedRects.Add(footprint);
            MapGenerator.SetVar("RectOfInterest", footprint);
            MapGenerator.PlayerStartSpot = root + new IntVec3(11, 0, -3);
            GenStep_RukayaMissileSite.ClearOutdoorPath(root + new IntVec3(11, 0, 0),
                new IntVec3(root.x + 11, 0, 0), new List<CellRect> { footprint.ContractedBy(1) }, map);
            MapGenerator.rootsToUnfog.RemoveAll(c => footprint.Contains(c));
        }
    }
}
