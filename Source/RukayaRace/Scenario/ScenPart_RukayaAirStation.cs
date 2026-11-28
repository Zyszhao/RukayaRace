using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RukayaAviation;
using Verse;

namespace RukayaRace.Scenario
{
    public class ScenPart_RukayaAirStation : ScenPart
    {
        private CellRect generatedFootprint;
        private CellRect generatedRunwayRect;
        private bool generatedReverse;
        private HashSet<IntVec3> generatedAirfieldCells;
        private RunwayZone generatedRunway;
        private ParkingPad[] generatedPads;
        private List<Thing> generatedBuildings;
        private Map generatedMap;

        private sealed class StationSpec
        {
            public string Def;
            public CellRect Runway;
            public bool Reverse;
            public IntVec3 Primary;
            public IntVec3 Secondary;
            public IntVec3 Start;
        }

        private static readonly StationSpec[] Specs =
        {
            new StationSpec { Def = "Rukaya_AirStation_Standard", Runway = new CellRect(3, 30, 36, 7), Primary = new IntVec3(27, 0, 24), Secondary = new IntVec3(15, 0, 24), Start = new IntVec3(19, 0, 10) },
            new StationSpec { Def = "Rukaya_AirStation_Wing", Runway = new CellRect(27, 2, 7, 36), Primary = new IntVec3(21, 0, 22), Secondary = new IntVec3(21, 0, 10), Start = new IntVec3(38, 0, 19) },
            new StationSpec { Def = "Rukaya_AirStation_Bunker", Runway = new CellRect(4, 3, 36, 7), Primary = new IntVec3(19, 0, 15), Secondary = new IntVec3(8, 0, 15), Start = new IntVec3(21, 0, 23) },
            new StationSpec { Def = "Rukaya_AirStation_ApronHub", Runway = new CellRect(33, 4, 7, 36), Reverse = true, Primary = new IntVec3(27, 0, 24), Secondary = new IntVec3(27, 0, 35), Start = new IntVec3(18, 0, 27) }
        };

        public override void GenerateIntoMap(Map map)
        {
            if (!(map.IsPlayerHome || Find.GameInitData != null && map.Index == 0) || map.Size.x < 200 || map.Size.z < 200) return;
            StationSpec spec = Specs.RandomElement();
            PrefabDef prefab = DefDatabase<PrefabDef>.GetNamed(spec.Def);
            var structure = new HashSet<IntVec3>();
            var roofCells = new HashSet<IntVec3>();
            var conduits = new List<(ThingDef def, IntVec3 cell)>();
            foreach (var (data, local) in prefab.GetTerrain())
            {
                structure.Add(local);
                if (data.def == TerrainDefOf.MetalTile) roofCells.Add(local);
            }
            foreach (var (data, local) in prefab.GetThings())
            {
                IntVec3 position = PrefabUtility.GetAdjustedThingLocalPosition(data, Rot4.North, local);
                Rot4 rotation = data.def.rotatable ? Rot4.North.Rotated(data.relativeRotation) : data.def.defaultPlacingRot;
                structure.UnionWith(GenAdj.OccupiedRect(position, rotation, data.def.size).Cells);
                if (data.def.defName is "Rukaya_FacilityWallGreen" or "Door") roofCells.Add(position);
                if (data.def.defName == "HiddenConduit") conduits.Add((data.def, position));
            }
            HashSet<IntVec3> flightCells = AirfieldCells(spec);
            var requiredCells = new HashSet<IntVec3>(structure);
            requiredCells.UnionWith(flightCells);
            IntVec3 center = FindSite(map, prefab, requiredCells, flightCells, spec);
            IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
            CellRect footprint = new CellRect(root.x, root.z, prefab.size.x, prefab.size.z);
            var preparedCells = new HashSet<IntVec3>(flightCells);
            foreach (IntVec3 cell in structure)
                preparedCells.UnionWith(new CellRect(cell.x, cell.z, 1, 1).ExpandedBy(2).Cells);
            Prepare(preparedCells, structure, flightCells, root, map);

            generatedBuildings = new List<Thing>();
            PrefabUtility.SpawnPrefab(prefab, map, center, Rot4.North, Faction.OfPlayer, generatedBuildings);
            foreach (var (def, local) in conduits)
            {
                IntVec3 cell = root + local;
                Thing conduit = cell.GetThingList(map).FirstOrDefault(t => t.def == def);
                if (conduit == null)
                {
                    conduit = ThingMaker.MakeThing(def);
                    GenSpawn.Spawn(conduit, cell, map, Rot4.North, WipeMode.Vanish);
                }
                if (conduit != null && conduit.def.CanHaveFaction) conduit.SetFaction(Faction.OfPlayer);
            }
            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            foreach (Thing thing in generatedBuildings.Where(t => t.Spawned))
                thing.SetForbidden(false, false);
            foreach (IntVec3 local in roofCells)
                map.roofGrid.SetRoof(root + local, RoofDefOf.RoofConstructed);

            generatedRunwayRect = spec.Runway.MovedBy(root);
            generatedReverse = spec.Reverse;
            generatedRunway = null;
            generatedAirfieldCells = flightCells.Select(c => root + c).ToHashSet();
            generatedPads = generatedBuildings.OfType<ParkingPad>().Where(p => p.Spawned).ToArray();
            RestoreRunway(map);
            ParkingPad primary = generatedPads.FirstOrDefault(p => p.Position == root + spec.Primary);
            if (primary != null)
            {
                ThingDef aircraftDef = DefDatabase<ThingDef>.GetNamed("RAV_Yak141Aircraft");
                Aircraft aircraft = (Aircraft)ThingMaker.MakeThing(aircraftDef, null);
                aircraft.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(aircraft, primary.Position, map, primary.Rotation, WipeMode.Vanish);
                while (aircraft.mounts.Count < aircraft.HardpointCount) aircraft.mounts.Add(new Hardpoint());
                aircraft.fuel = 90f;
                aircraft.mounts[0] = new Hardpoint { kind = StoreKind.Cannon, rounds = aircraft.StoreCapacity(0, StoreKind.Cannon) };
                aircraft.mounts[1] = new Hardpoint { kind = StoreKind.Bomb, rounds = aircraft.StoreCapacity(1, StoreKind.Bomb) };
                aircraft.mounts[3] = new Hardpoint { kind = StoreKind.Bomb, rounds = aircraft.StoreCapacity(3, StoreKind.Bomb) };
                aircraft.mounts[4] = new Hardpoint { kind = StoreKind.Cannon, rounds = aircraft.StoreCapacity(4, StoreKind.Cannon) };
                aircraft.NormalizeHardpoints();
                aircraft.RefreshCapacity();
                primary.Bind(aircraft);
                aircraft.yaw = primary.Heading;
            }

            MapGenerator.PlayerStartSpot = root + spec.Start;
            if (!MapGenerator.PlayerStartSpot.Standable(map)) MapGenerator.PlayerStartSpot = CellFinder.RandomSpawnCellForPawnNear(map.Center, map);
            MapGenerator.rootsToUnfog.Add(MapGenerator.PlayerStartSpot);
            generatedMap = map;
            generatedFootprint = footprint;
            MapGenerator.UsedRects.Add(footprint);
            foreach (IntVec3 cell in structure) map.areaManager.Home[root + cell] = true;
            foreach (Thing thing in generatedBuildings.Where(t => t.Spawned))
            {
                if (thing.def.defName == "Rukaya_ActiveMatterGenerator") thing.TryGetComp<CompRefuelable>()?.Refuel(36f);
                thing.TryGetComp<CompPowerBattery>()?.SetStoredEnergyPct(0.5f);
            }
        }

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map != generatedMap) return;
            foreach (IntVec3 cell in generatedFootprint.ExpandedBy(4).Cells)
                if (cell.InBounds(map)) map.fogGrid.Unfog(cell);

            foreach (IntVec3 cell in generatedAirfieldCells)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing is Pawn || thing is ParkingPad || thing is Aircraft || thing.Faction == Faction.OfPlayer) continue;
                    if (CanClear(thing))
                        thing.Destroy(DestroyMode.Vanish);
                }
                map.roofGrid.SetRoof(cell, null);
                TerrainDef terrain = cell.GetTerrain(map);
                if (!terrain.affordances.Contains(TerrainAffordanceDefOf.Light) || terrain.passability == Traversability.Impassable)
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
            }
            foreach (IntVec3 cell in generatedRunwayRect.Cells)
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            RestoreRunway(map);

            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            foreach (Thing thing in generatedBuildings.Where(t => t.Spawned))
            {
                if (thing.def.defName == "SolarGenerator")
                    foreach (IntVec3 cell in thing.OccupiedRect().Cells)
                        map.roofGrid.SetRoof(cell, null);
                CompPowerTrader trader = thing.TryGetComp<CompPowerTrader>();
                if (trader?.PowerNet != null && !trader.PowerOn && !thing.IsBrokenDown()
                    && (thing.TryGetComp<CompFlickable>()?.SwitchIsOn ?? true))
                    trader.PowerOn = true;
            }
            generatedMap = null;
            generatedRunway = null;
            generatedAirfieldCells = null;
            generatedPads = null;
            generatedBuildings = null;
        }

        private void RestoreRunway(Map map)
        {
            if (!map.zoneManager.AllZones.Contains(generatedRunway))
            {
                generatedRunway = new RunwayZone(map.zoneManager) { reverse = generatedReverse, autoCut = true };
                map.zoneManager.RegisterZone(generatedRunway);
            }
            foreach (IntVec3 cell in generatedRunwayRect.Cells)
            {
                Zone existing = map.zoneManager.ZoneAt(cell);
                if (existing == generatedRunway) continue;
                existing?.RemoveCell(cell);
                generatedRunway.AddCell(cell);
            }
            foreach (ParkingPad pad in generatedPads.Where(p => p.Spawned)) pad.runway = generatedRunway;
        }

        private static HashSet<IntVec3> AirfieldCells(StationSpec spec)
        {
            var cells = spec.Runway.ExpandedBy(2).Cells.ToHashSet();
            bool alongZ = spec.Runway.Height >= spec.Runway.Width;
            IntVec3 start = alongZ
                ? new IntVec3(spec.Runway.CenterCell.x, 0, spec.Reverse ? spec.Runway.maxZ - 4 : spec.Runway.minZ + 4)
                : new IntVec3(spec.Reverse ? spec.Runway.maxX - 4 : spec.Runway.minX + 4, 0, spec.Runway.CenterCell.z);
            foreach (IntVec3 pad in new[] { spec.Primary, spec.Secondary })
            {
                IntVec3 center = pad;
                while (true)
                {
                    cells.UnionWith(new CellRect(center.x, center.z, 1, 1).ExpandedBy(4).Cells);
                    if (center == start) break;
                    if (alongZ ? center.x != start.x : center.z == start.z) center.x += Math.Sign(start.x - center.x);
                    else center.z += Math.Sign(start.z - center.z);
                }
            }
            return cells;
        }

        private static IntVec3 FindSite(Map map, PrefabDef prefab, HashSet<IntVec3> requiredCells, HashSet<IntVec3> flightCells, StationSpec spec)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestScore = int.MinValue;
            for (int z = 24; z < map.Size.z - 24; z += 4)
                for (int x = 24; x < map.Size.x - 24; x += 4)
                {
                    IntVec3 center = new IntVec3(x, 0, z);
                    IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
                    CellRect rect = new CellRect(root.x, root.z, prefab.size.x, prefab.size.z);
                    if (rect.minX < 22 || rect.minZ < 22 || rect.maxX > map.Size.x - 23 || rect.maxZ > map.Size.z - 23
                        || MapGenerator.UsedRects.Any(r => r.Overlaps(rect.ExpandedBy(2)))) continue;
                    int score = -center.DistanceToSquared(map.Center) / 10;
                    bool obstructed = false;
                    int thickRoofCells = 0;
                    foreach (IntVec3 local in requiredCells)
                    {
                        IntVec3 c = root + local;
                        if (c.GetThingList(map).Any(t => t.def.category == ThingCategory.Building && !CanClear(t)))
                        {
                            obstructed = true;
                            break;
                        }
                        RoofDef roof = map.roofGrid.RoofAt(c);
                        if (roof != null && roof.isThickRoof)
                        {
                            thickRoofCells++;
                            score -= 500;
                        }
                        else if (roof != null && roof.isNatural)
                            score -= 40;
                        Building edifice = c.GetEdifice(map);
                        if (edifice?.def.mineable == true)
                            score -= 30;
                        if (c.GetTerrain(map).passability == Traversability.Impassable) score -= 12;
                        if (c.GetTerrain(map).IsWater || c.GetTerrain(map).defName.Contains("Marsh")) score -= 20;
                    }
                    if (obstructed) continue;
                    bool alongZ = spec.Runway.Height >= spec.Runway.Width;
                    IntVec3 runwayStart = alongZ
                        ? new IntVec3(spec.Runway.CenterCell.x, 0, spec.Reverse ? spec.Runway.maxZ - 4 : spec.Runway.minZ + 4)
                        : new IntVec3(spec.Reverse ? spec.Runway.maxX - 4 : spec.Runway.minX + 4, 0, spec.Runway.CenterCell.z);
                    IntVec3 runwayEnd = alongZ
                        ? new IntVec3(spec.Runway.CenterCell.x, 0, spec.Reverse ? spec.Runway.minZ + 4 : spec.Runway.maxZ - 4)
                        : new IntVec3(spec.Reverse ? spec.Runway.minX + 4 : spec.Runway.maxX - 4, 0, spec.Runway.CenterCell.z);
                    foreach (IntVec3 endpoint in new[] { runwayStart, runwayEnd })
                        for (int distance = 1; distance <= 6; distance++)
                        {
                            IntVec3 direction = alongZ
                                ? new IntVec3(0, 0, endpoint == runwayStart ? Math.Sign(runwayStart.z - runwayEnd.z) : Math.Sign(runwayEnd.z - runwayStart.z))
                                : new IntVec3(endpoint == runwayStart ? Math.Sign(runwayStart.x - runwayEnd.x) : Math.Sign(runwayEnd.x - runwayStart.x), 0, 0);
                            IntVec3 c = root + endpoint + direction * distance;
                            RoofDef roof = c.InBounds(map) ? map.roofGrid.RoofAt(c) : null;
                            if (roof?.isThickRoof == true)
                            {
                                thickRoofCells++;
                                score -= 500;
                            }
                            else if (roof?.isNatural == true) score -= 40;
                            if (c.InBounds(map) && c.GetEdifice(map)?.def.mineable == true) score -= 30;
                        }
                    if (thickRoofCells > 0) score -= 1000000 + thickRoofCells * 1000;
                    if (!obstructed && score > bestScore) { best = center; bestScore = score; }
                }
            if (!best.IsValid) throw new InvalidOperationException("No safe air-station site found on the starting map.");
            return best;
        }

        private static bool CanClear(Thing thing) => (thing.def.destroyable || thing.def.mineable) && thing.def.defName != "SteamGeyser"
            && thing.def.category is ThingCategory.Building or ThingCategory.Plant or ThingCategory.Item or ThingCategory.Filth;

        private static void Prepare(HashSet<IntVec3> cells, HashSet<IntVec3> structure, HashSet<IntVec3> airfield, IntVec3 root, Map map)
        {
            foreach (IntVec3 local in cells)
            {
                IntVec3 cell = root + local;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (CanClear(thing)) thing.Destroy(DestroyMode.Vanish);
                map.roofGrid.SetRoof(cell, null);
                bool hasStructure = structure.Contains(local);
                if (!hasStructure && !airfield.Contains(local)) continue;
                TerrainDef terrain = cell.GetTerrain(map);
                TerrainAffordanceDef affordance = hasStructure ? TerrainAffordanceDefOf.Heavy : TerrainAffordanceDefOf.Light;
                if (!terrain.affordances.Contains(affordance) || terrain.passability == Traversability.Impassable)
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
            }
        }
    }
}
