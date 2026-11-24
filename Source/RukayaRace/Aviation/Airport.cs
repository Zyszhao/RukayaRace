using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public enum PadSize { Small = 1, Medium = 2, Large = 3 }

public static class AV
{
    private static readonly Dictionary<string, ThingDef> CachedDefs = new();
    private static ThingDef cachedYakDef, cachedMigDef, cachedAn72Def, cachedPadSmallDef, cachedPadDef, cachedPadLargeDef;
    private static ThingDef YakDef => cachedYakDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_Yak141Aircraft");
    private static ThingDef MigDef => cachedMigDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_Mig31Aircraft");
    private static ThingDef An72Def => cachedAn72Def ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_An72Aircraft");
    public static ThingDef PadSmallDef => cachedPadSmallDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_PadSmall");
    public static ThingDef PadDef => cachedPadDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_Pad");
    public static ThingDef PadLargeDef => cachedPadLargeDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("RAV_PadLarge");

    public static bool IsPadDef(BuildableDef def) => def != null && (def == PadSmallDef || def == PadDef || def == PadLargeDef);

    public static PadSize PadSizeOf(ThingDef def)
    {
        if (def == null) return PadSize.Medium;
        if (def.defName == "RAV_PadLarge") return PadSize.Large;
        if (def.defName == "RAV_PadSmall") return PadSize.Small;
        return PadSize.Medium;
    }

    public static PadSize RequiredPadSizeFor(ThingDef aircraftDef)
    {
        if (aircraftDef == null) return PadSize.Small;
        if (aircraftDef.defName == "RAV_An72Aircraft") return PadSize.Large;
        if (aircraftDef.defName == "RAV_Mig31Aircraft") return PadSize.Medium;
        return PadSize.Small;
    }

    public static int ClearanceFor(PadSize size) => size switch
    {
        PadSize.Small => 4,
        PadSize.Large => 7,
        _ => 5
    };

    public static string PadSizeName(PadSize size) => size switch
    {
        PadSize.Small => "小型停机坪（9×9）",
        PadSize.Large => "大型停机坪（15×15）",
        _ => "中型停机坪（11×11）"
    };

    public static string PadSizeShortName(PadSize size) => size switch
    {
        PadSize.Small => "小型",
        PadSize.Large => "大型",
        _ => "中型"
    };

    public static ThingDef Def(string name)
    {
        if (CachedDefs.TryGetValue(name, out ThingDef def)) return def;
        def = DefDatabase<ThingDef>.GetNamed("RAV_" + name + (name is "Yak141" or "Mig31" or "An72" ? "Aircraft" : ""));
        CachedDefs[name] = def;
        return def;
    }
    public static void Say(string text)
    {
        Messages.Message(text, MessageTypeDefOf.RejectInput, false);
    }
    public static Texture2D Icon(string path) => ContentFinder<Texture2D>.Get(path);
    public static string Inspect(string original, string extra) => string.IsNullOrWhiteSpace(original) ? extra : original.TrimEnd() + "\n" + extra;
    public static Command_Action Button(string label, string desc, Action action, string icon = "UI/Commands/LaunchShip")
        => new Command_Action { defaultLabel = label, defaultDesc = desc, action = action, icon = Icon(icon) };
    public static void Target(Action<IntVec3> action)
        => Find.Targeter.BeginTargeting(LocationTargets, t => action(t.Cell));
    public static TargetingParameters LocationTargets => new TargetingParameters { canTargetLocations = true, canTargetBuildings = false, canTargetPawns = false };
    public static void Line(Vector3 a, Vector3 b, SimpleColor color)
    { a.y = b.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.01f; GenDraw.DrawLineBetween(a, b, color, 0.12f); }
    public static void DrawPath(IEnumerable<IntVec3> path, Vector3 from, SimpleColor color)
    {
        foreach (IntVec3 c in path) { Vector3 next = Flat(c); if (Distance(from, next) > 0.01f) Line(from, next, color); from = next; }
    }
    public static void DrawPath(List<IntVec3> path, int startIndex, Vector3 from, SimpleColor color)
    {
        for (int i = startIndex; i < path.Count; i++) { Vector3 next = Flat(path[i]); if (Distance(from, next) > 0.01f) Line(from, next, color); from = next; }
    }
    public static Vector3 Flat(IntVec3 c) => c.ToVector3Shifted();
    public static Vector3 Dir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    public static float Distance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    public static IEnumerable<Aircraft> Planes(Map m)
    {
        if (m == null) yield break;
        if (YakDef != null)
        {
            List<Thing> yaks = m.listerThings.ThingsOfDef(YakDef);
            for (int i = 0; i < yaks.Count; i++) if (yaks[i] is Aircraft a) yield return a;
        }
        if (MigDef != null)
        {
            List<Thing> migs = m.listerThings.ThingsOfDef(MigDef);
            for (int i = 0; i < migs.Count; i++) if (migs[i] is Aircraft a) yield return a;
        }
        if (An72Def != null)
        {
            List<Thing> an72s = m.listerThings.ThingsOfDef(An72Def);
            for (int i = 0; i < an72s.Count; i++) if (an72s[i] is Aircraft a) yield return a;
        }
    }
    public static IEnumerable<ParkingPad> Pads(Map m)
    {
        if (m == null) yield break;
        if (PadSmallDef != null)
        {
            List<Thing> pads = m.listerThings.ThingsOfDef(PadSmallDef);
            for (int i = 0; i < pads.Count; i++) if (pads[i] is ParkingPad pad) yield return pad;
        }
        if (PadDef != null)
        {
            List<Thing> pads = m.listerThings.ThingsOfDef(PadDef);
            for (int i = 0; i < pads.Count; i++) if (pads[i] is ParkingPad pad) yield return pad;
        }
        if (PadLargeDef != null)
        {
            List<Thing> pads = m.listerThings.ThingsOfDef(PadLargeDef);
            for (int i = 0; i < pads.Count; i++) if (pads[i] is ParkingPad pad) yield return pad;
        }
    }
    public static RunwayZone NearestRunway(Map map, IntVec3 position)
    {
        RunwayZone best = null;
        int bestDist = int.MaxValue;
        List<Zone> zones = map.zoneManager.AllZones;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i] is RunwayZone z && z.CellCount > 0)
            {
                int d = z.Start.DistanceToSquared(position);
                if (d < bestDist) { bestDist = d; best = z; }
            }
        }
        return best;
    }
    public static bool SafeAir(IntVec3 c, Map m, int margin = 24)
        => c.x >= margin && c.z >= margin && c.x < m.Size.x - margin && c.z < m.Size.z - margin;

    private static bool CellClearSingle(IntVec3 c, Map map, Aircraft self, bool roof)
    {
        if (!c.InBounds(map) || c.Fogged(map) || !c.Walkable(map) || (roof && c.Roofed(map))) return false;
        TerrainDef terrain = c.GetTerrain(map);
        if (terrain == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.Light)) return false;
        List<Thing> things = c.GetThingList(map);
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == self || t is ParkingPad || (t is Aircraft p && p.Airborne)) continue;
            if (t is Aircraft || t is Plant plant && plant.def.plant.IsTree
                || t.def.category == ThingCategory.Building && t.def.fillPercent > 0.1f) return false;
        }
        return true;
    }

    // A conservative square swept envelope covers the rendered body during turns.
    public static bool Clear(IntVec3 center, Map map, int radius, Aircraft self = null, bool roof = true)
    {
        if (radius <= 0) return CellClearSingle(center, map, self, roof);
        int minX = center.x - radius, maxX = center.x + radius;
        int minZ = center.z - radius, maxZ = center.z + radius;
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                if (!CellClearSingle(new IntVec3(x, 0, z), map, self, roof)) return false;
        return true;
    }

    public static List<IntVec3> TaxiPath(Map map, IntVec3 from, IntVec3 to, Aircraft plane, int? clearanceOverride = null)
    {
        int width = map.Size.x, height = map.Size.z;
        int radius = clearanceOverride ?? plane?.Clearance ?? 5;
        // Per-cell cache: 0 = unknown, 1 = clear, 2 = blocked. Eliminates redundant GetThingList scans across overlapping 11x11 footprints.
        byte[] cellClear = new byte[width * height];
        bool IsFootprintClear(IntVec3 center)
        {
            int minX = center.x - radius, maxX = center.x + radius;
            int minZ = center.z - radius, maxZ = center.z + radius;
            if (minX < 0 || minZ < 0 || maxX >= width || maxZ >= height) return false;
            for (int z = minZ; z <= maxZ; z++)
            {
                int row = z * width;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;
                    byte state = cellClear[idx];
                    if (state == 0)
                    {
                        state = CellClearSingle(new IntVec3(x, 0, z), map, plane, true) ? (byte)1 : (byte)2;
                        cellClear[idx] = state;
                    }
                    if (state == 2) return false;
                }
            }
            return true;
        }

        // A* with Manhattan heuristic finds the same shortest grid path while expanding far fewer nodes.
        var open = new PriorityQueue<IntVec3, int>();
        var parents = new Dictionary<IntVec3, IntVec3>(256);
        var gScore = new Dictionary<IntVec3, int>(256);
        gScore[from] = 0;
        parents[from] = IntVec3.Invalid;
        open.Enqueue(from, Math.Abs(to.x - from.x) + Math.Abs(to.z - from.z));
        int expanded = 0;
        while (open.Count > 0 && expanded < 40000)
        {
            IntVec3 c = open.Dequeue();
            if (c == to)
            {
                var result = new List<IntVec3>();
                for (IntVec3 n = to; n.IsValid; n = parents[n]) result.Add(n);
                result.Reverse(); return result;
            }
            expanded++;
            int nextG = gScore[c] + 1;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 n = c + GenAdj.CardinalDirections[i];
                if (gScore.TryGetValue(n, out int existingG) && existingG <= nextG) continue;
                if (!IsFootprintClear(n)) continue;
                parents[n] = c;
                gScore[n] = nextG;
                int f = nextG + Math.Abs(to.x - n.x) + Math.Abs(to.z - n.z);
                open.Enqueue(n, f);
            }
        }
        return null;
    }

    public static bool Spend(Map map, IntVec3 pos, ThingDef resource, int count)
    {
        var stacks = map.listerThings.ThingsOfDef(resource).Where(t => !t.IsForbidden(Faction.OfPlayer)
            && t.Position.InHorDistOf(pos, 18f)).ToList();
        if (stacks.Sum(t => t.stackCount) < count) return false;
        foreach (Thing t in stacks)
        {
            int n = Math.Min(count, t.stackCount); t.SplitOff(n).Destroy(); count -= n;
            if (count <= 0) break;
        }
        return true;
    }
}

public class RunwayZone : Zone
{
    public bool reverse;
    public bool oppositeLanding;
    public bool autoCut = true;
    public Aircraft reserved;
    private CellRect cachedRect;
    private bool rectDirty = true;
    private int blockedDrawCacheTick = -9999;
    private readonly List<IntVec3> blockedDrawCache = new();
    protected override Color NextZoneColor => new Color(0.3f, 0.75f, 0.95f, 0.3f);
    public RunwayZone() { }
    public RunwayZone(ZoneManager manager) : base("跑道", manager) { }
    public override void AddCell(IntVec3 c) { base.AddCell(c); rectDirty = true; }
    public override void RemoveCell(IntVec3 c) { base.RemoveCell(c); rectDirty = true; }
    public CellRect Rect
    {
        get
        {
            if (!rectDirty) return cachedRect;
            rectDirty = false;
            if (cells.Count == 0) { cachedRect = CellRect.Empty; return cachedRect; }
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (c.x < minX) minX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.x > maxX) maxX = c.x;
                if (c.z > maxZ) maxZ = c.z;
            }
            cachedRect = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            return cachedRect;
        }
    }
    public bool AlongZ => Rect.Height >= Rect.Width;
    private int EndInset => Math.Min(6, Math.Max(4, (Math.Min(Rect.Width, Rect.Height) - 1) / 2));
    public IntVec3 Start => AlongZ ? new IntVec3(Rect.CenterCell.x, 0, reverse ? Rect.maxZ - EndInset : Rect.minZ + EndInset)
        : new IntVec3(reverse ? Rect.maxX - EndInset : Rect.minX + EndInset, 0, Rect.CenterCell.z);
    public IntVec3 End => AlongZ ? new IntVec3(Rect.CenterCell.x, 0, reverse ? Rect.minZ + EndInset : Rect.maxZ - EndInset)
        : new IntVec3(reverse ? Rect.minX + EndInset : Rect.maxX - EndInset, 0, Rect.CenterCell.z);
    public IntVec3 LandingStart => oppositeLanding ? End : Start;
    public IntVec3 LandingEnd => oppositeLanding ? Start : End;
    public const int MinBaseWidth = 7;
    public const int MinBaseLength = 34;

    public string SupportedAircraftSummary()
    {
        CellRect r = Rect;
        if (CellCount != r.Area) return "无（非完整矩形）";
        int w = Math.Min(r.Width, r.Height);
        int l = Math.Max(r.Width, r.Height);
        List<string> supported = new();
        if (w >= 7 && l >= 34) supported.Add("轻型战斗机");
        if (w >= 9 && l >= 46) supported.Add("截击机");
        if (w >= 13 && l >= 54) supported.Add("运输机");
        return supported.Count == 0 ? "无（尺寸不足，最小需 7×34 格）" : string.Join("、", supported);
    }

    public string Error(Aircraft plane)
    {
        CellRect r = Rect;
        if (reserved != null && (!reserved.Spawned || reserved.Destroyed || reserved.State == FlightState.Parked)) reserved = null;
        if (reserved != null && reserved != plane) return "跑道已被其他飞机预留";
        int minW = plane?.MinRunwayWidth ?? MinBaseWidth;
        int minL = plane?.MinRunwayLength ?? MinBaseLength;
        if (CellCount != r.Area)
            return $"跑道必须是完整矩形（至少 {minW}×{minL} 格）";
        if (Math.Min(r.Width, r.Height) < minW || Math.Max(r.Width, r.Height) < minL)
            return plane != null
                ? $"跑道尺寸不足，{plane.def.label}需要至少 {minW}×{minL} 格"
                : $"跑道必须是至少 {MinBaseWidth}×{MinBaseLength} 格的完整矩形";
        IntVec3 start = Start, end = End;
        if (!AV.SafeAir(start, Map, 18) || !AV.SafeAir(end, Map, 18)) return "跑道距地图边缘不足 18 格";
        CellRect expanded = r.ExpandedBy(1);
        for (int z = expanded.minZ; z <= expanded.maxZ; z++)
            for (int x = expanded.minX; x <= expanded.maxX; x++)
            {
                IntVec3 c = new IntVec3(x, 0, z);
                if (c.InBounds(Map) && !AV.Clear(c, Map, 0, plane)) return "跑道阻挡：" + BlockDescription(c, plane);
            }
        if (plane != null)
        {
            bool alongZ = r.Height >= r.Width;
            IntVec3 direction = alongZ ? IntVec3.North : IntVec3.East;
            IntVec3 begin = alongZ ? (start.z < end.z ? start : end) : (start.x < end.x ? start : end);
            int length = Mathf.Abs(end.x - start.x) + Mathf.Abs(end.z - start.z);
            int clearance = plane.Clearance;
            // Check shoulder/end clearance using the bounding rectangle of all swept cells once rather than overlapping checks per step.
            int minX = Math.Min(begin.x, begin.x + direction.x * length) - clearance;
            int maxX = Math.Max(begin.x, begin.x + direction.x * length) + clearance;
            int minZ = Math.Min(begin.z, begin.z + direction.z * length) - clearance;
            int maxZ = Math.Max(begin.z, begin.z + direction.z * length) + clearance;
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    if (!AV.Clear(new IntVec3(x, 0, z), Map, 0, plane)) return "跑道肩部或端部净空不足";
        }
        return null;
    }
    public IEnumerable<IntVec3> ClearanceCells => Rect.ExpandedBy(1).Cells.Where(c => c.InBounds(Map));
    public List<IntVec3> BlockedCells(Aircraft plane = null)
    {
        List<IntVec3> result = new List<IntVec3>();
        CellRect expanded = Rect.ExpandedBy(1);
        for (int z = expanded.minZ; z <= expanded.maxZ; z++)
            for (int x = expanded.minX; x <= expanded.maxX; x++)
            {
                IntVec3 c = new IntVec3(x, 0, z);
                if (c.InBounds(Map) && !AV.Clear(c, Map, 0, plane)) result.Add(c);
            }
        return result;
    }
    public IntVec3 FirstBlockedCell(Aircraft plane = null)
    {
        CellRect expanded = Rect.ExpandedBy(1);
        for (int z = expanded.minZ; z <= expanded.maxZ; z++)
            for (int x = expanded.minX; x <= expanded.maxX; x++)
            {
                IntVec3 c = new IntVec3(x, 0, z);
                if (c.InBounds(Map) && !AV.Clear(c, Map, 0, plane)) return c;
            }
        return IntVec3.Invalid;
    }
    private string BlockDescription(IntVec3 c, Aircraft plane)
    {
        if (c.Fogged(Map)) return $"未探索区域 {c}";
        if (c.Roofed(Map)) return $"屋顶 {c}";
        Thing t = c.GetThingList(Map).FirstOrDefault(t => t != plane && (t is Aircraft p && !p.Airborne || t is Plant plant && plant.def.plant.IsTree || t.def.category == ThingCategory.Building && t is not ParkingPad && t.def.fillPercent > 0.1f));
        return (t?.LabelCap.ToString() ?? "地形") + " " + c;
    }
    public void MaintainVegetation()
    {
        if (!autoCut) return;
        CellRect expanded = Rect.ExpandedBy(1);
        for (int z = expanded.minZ; z <= expanded.maxZ; z++)
            for (int x = expanded.minX; x <= expanded.maxX; x++)
            {
                IntVec3 c = new IntVec3(x, 0, z);
                if (!c.InBounds(Map)) continue;
                List<Thing> things = c.GetThingList(Map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Plant plant && plant.def.plant.allowAutoCut
                        && !plant.DeliberatelyCultivated()
                        && !Map.designationManager.HasMapDesignationOn(plant))
                        Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.CutPlant));
                }
            }
    }
    public void DrawMarkers()
    {
        if (Hidden || CellCount == 0) return;
        Vector3 start = AV.Flat(Start), direction = (AV.Flat(End) - start).normalized, side = Quaternion.AngleAxis(90, Vector3.up) * direction;
        AV.Line(start - side * 3, start + side * 3, SimpleColor.Green);
        Vector3 tip = start + direction * 9;
        AV.Line(start, tip, SimpleColor.Green); AV.Line(tip, tip - direction * 3 + side * 2, SimpleColor.Green); AV.Line(tip, tip - direction * 3 - side * 2, SimpleColor.Green);
        if (Find.Selector.IsSelected(this))
        {
            Vector3 landing = AV.Flat(LandingStart) + side * 2, landingDirection = (AV.Flat(LandingEnd) - AV.Flat(LandingStart)).normalized;
            Vector3 landingTip = landing + landingDirection * 6;
            AV.Line(landing, landingTip, SimpleColor.Cyan);
            AV.Line(landingTip, landingTip - landingDirection * 2 + side, SimpleColor.Cyan);
            AV.Line(landingTip, landingTip - landingDirection * 2 - side, SimpleColor.Cyan);
            int tick = Find.TickManager.TicksGame;
            if (tick - blockedDrawCacheTick >= 15 || tick < blockedDrawCacheTick)
            {
                blockedDrawCacheTick = tick;
                blockedDrawCache.Clear();
                CellRect expanded = Rect.ExpandedBy(1);
                for (int z = expanded.minZ; z <= expanded.maxZ && blockedDrawCache.Count < 96; z++)
                    for (int x = expanded.minX; x <= expanded.maxX && blockedDrawCache.Count < 96; x++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (c.InBounds(Map) && !AV.Clear(c, Map, 0, reserved)) blockedDrawCache.Add(c);
                    }
            }
            if (blockedDrawCache.Count > 0) GenDraw.DrawFieldEdges(blockedDrawCache, Color.red);
        }
    }
    public override string GetInspectString() => AV.Inspect(base.GetInspectString(), $"{Rect.Width}×{Rect.Height} 格；起飞 {Start} → {End}\n降落 {LandingStart} → {LandingEnd}\n支持机型：{SupportedAircraftSummary()}\n" + (Error(null) ?? "跑道可用"));
    public override IEnumerable<Gizmo> GetZoneAddGizmos() { yield return new Designator_RunwayExpand(); }
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo g in base.GetGizmos())
            yield return g is Designator_ZoneDelete_Shrink ? new Designator_RunwayShrink() : g;
        yield return new Command_HideRunway(this);
        yield return new Command_Toggle { defaultLabel = "自动割除", defaultDesc = "安排割除跑道和道肩内的野生植物。", icon = Designator_PlantsCut.IconTex, isActive = () => autoCut, toggleAction = () => { autoCut = !autoCut; MaintainVegetation(); } };
        Command_Action direction = AV.Button("反转跑道方向", "交换起飞端和跑道末端。跑道占用时不能调整。", () => { if (reserved == null) reverse = !reverse; }, AviationArt.Command("RunwayZone_ReverseDirection"));
        if (reserved != null) direction.Disable("跑道正在使用。");
        yield return direction;
        Command_Toggle landing = new Command_Toggle { defaultLabel = "反向降落", defaultDesc = "将起飞端用作降落终点。", icon = AV.Icon(AviationArt.Command("RunwayZone_ReversedLanding")), isActive = () => oppositeLanding, toggleAction = () => { if (reserved == null) oppositeLanding = !oppositeLanding; } };
        if (reserved != null) landing.Disable("跑道正在使用。");
        yield return landing;
    }
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_Values.Look(ref reverse, "reverse"); Scribe_Values.Look(ref oppositeLanding, "oppositeLanding"); Scribe_References.Look(ref reserved, "reserved"); Scribe_Values.Look(ref autoCut, "autoCut", true);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) rectDirty = true;
    }
}

public class Alert_RunwayBlocked : Alert
{
    private readonly List<GlobalTargetInfo> culprits = new();
    public Alert_RunwayBlocked()
    {
        defaultLabel = "跑道受阻";
        defaultExplanation = "跑道或道肩存在屋顶、树木、建筑或不适用的地形。点击提示定位，选中跑道可查看红色阻挡标记；启用自动割除可安排殖民者清除野生植物。";
    }
    public override AlertReport GetReport()
    {
        culprits.Clear();
        List<Map> maps = Find.Maps;
        for (int m = 0; m < maps.Count; m++)
        {
            Map map = maps[m];
            if (!map.IsPlayerHome) continue;
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is RunwayZone z && z.CellCount > 0)
                {
                    IntVec3 blocked = z.FirstBlockedCell(z.reserved);
                    if (blocked.IsValid) culprits.Add(new GlobalTargetInfo(blocked, z.Map));
                }
            }
        }
        return AlertReport.CulpritsAre(culprits);
    }
}

public class Command_HideRunway : Command_Hide
{
    public Command_HideRunway(RunwayZone zone) : base(zone) { }
    protected override IEnumerable<FloatMenuOption> GetOptions() => ZoneTypeOptions<RunwayZone>("跑道", AV.Icon(AviationArt.Command("RunwayZone_Set")));
}

public class Designator_Runway : Designator_ZoneAdd
{
    protected override string NewZoneLabel => "跑道";
    public Designator_Runway()
    {
        zoneTypeToPlace = typeof(RunwayZone); defaultLabel = "跑道区";
        defaultDesc = "划出矩形净空跑道（距地图边缘至少18格），并保持跑道、道肩与滑行通道净空。所需尺寸：轻型战斗机 34×7（可选，支持垂直起降）、截击机 46×9、运输机 54×13。";
        icon = AV.Icon(AviationArt.Command("RunwayZone_Set"));
    }
    protected override Zone MakeNewZone() => new RunwayZone(Map.zoneManager);
}

public class Designator_RunwayExpand : Designator_Runway
{
    public Designator_RunwayExpand() { defaultLabel = "DesignatorZoneExpand".Translate(); hotKey = KeyBindingDefOf.Misc6; }
}

public class Designator_RunwayShrink : Designator_ZoneDelete_Shrink
{
    public Designator_RunwayShrink() { icon = AV.Icon(AviationArt.Command("RunwayZone_Remove")); }
}

public class PlaceWorker_Aircraft : PlaceWorker
{
    public override bool ForceAllowPlaceOver(BuildableDef other) => AV.IsPadDef(other);
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        ParkingPad pad = AV.Pads(map).FirstOrDefault(p => p.Position == loc && p.Faction == Faction.OfPlayer);
        if (pad == null) return "飞机必须放置在己方已建成停机坪的中心。";
        ThingDef aircraftDef = checkingDef as ThingDef;
        PadSize requiredSize = AV.RequiredPadSizeFor(aircraftDef);
        if (pad.PadSize < requiredSize)
            return $"此停机坪过小，至少需要{AV.PadSizeName(requiredSize)}。";
        if (pad.bound != null && !pad.bound.Destroyed && pad.bound != thing && pad.bound != thingToIgnore
            || pad.reserved != null && !pad.reserved.Destroyed && pad.reserved != thing && pad.reserved != thingToIgnore) return "此停机坪已绑定或预留给另一架飞机。";
        int selfClearance = AV.ClearanceFor(requiredSize);
        if (AV.Planes(map).Any(p => p != thing && p != thingToIgnore && !p.Airborne
            && p.Position.InHorDistOf(loc, Math.Max(7, (p.Clearance + selfClearance + 2) / 2))))
            return "停机坪附近已有其他飞机。";
        List<Thing> cellThings = loc.GetThingList(map);
        for (int i = 0; i < cellThings.Count; i++)
        {
            Thing t = cellThings[i];
            if (t != thing && t != thingToIgnore && t.Position == loc && (t is Blueprint || t is Frame)
                && t.def.entityDefToBuild is ThingDef def && typeof(Aircraft).IsAssignableFrom(def.thingClass))
                return "停机坪已有飞机建造任务。";
        }
        return true;
    }
}

public class PlaceWorker_ParkingPad : PlaceWorker
{
    private Map previewMap;
    private IntVec3 previewPosition, previewStart;
    private ThingDef previewDef;
    private List<IntVec3> path;
    private float nextRefresh;
    public RunwayZone PreviewRunway { get; private set; }
    public IReadOnlyList<IntVec3> PreviewPath => path;
    public void UpdatePreview(Map map, IntVec3 position, ThingDef padDef = null)
    {
        RunwayZone nearest = AV.NearestRunway(map, position);
        IntVec3 start = nearest?.Start ?? IntVec3.Invalid;
        if (previewMap == map && previewPosition == position && previewDef == padDef && PreviewRunway == nearest && previewStart == start && Time.realtimeSinceStartup < nextRefresh) return;
        previewMap = map; previewPosition = position; previewDef = padDef; previewStart = start; PreviewRunway = nearest; nextRefresh = Time.realtimeSinceStartup + 0.5f;
        int clearance = AV.ClearanceFor(AV.PadSizeOf(padDef));
        path = nearest == null || !AV.Clear(position, map, clearance) ? null : AV.TaxiPath(map, position, start, null, clearance);
    }
    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        Map map = Find.CurrentMap; if (map == null) return;
        UpdatePreview(map, center, def);
        CellRect rect = GenAdj.OccupiedRect(center, rot, def.size);
        float y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.01f;
        Color frameColor = new Color(ghostCol.r, ghostCol.g, ghostCol.b, 0.9f);
        Color accentColor = new Color(Mathf.Min(1f, ghostCol.r + 0.25f), Mathf.Min(1f, ghostCol.g + 0.25f), Mathf.Min(1f, ghostCol.b + 0.25f), 1f);
        ParkingPad.DrawOutline(rect, y, frameColor, accentColor, rot.AsAngle);
        ParkingPad.DrawHeading(AV.Flat(center), rot.AsAngle, rect);
        if (PreviewRunway == null) return;
        if (path == null) AV.Line(AV.Flat(center), AV.Flat(PreviewRunway.Start), SimpleColor.Red);
        else AV.DrawPath(path, AV.Flat(center), SimpleColor.Cyan);
    }
    public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)
    {
        if (Find.CurrentMap == null) return;
        ThingDef padDef = def as ThingDef;
        UpdatePreview(Find.CurrentMap, center, padDef);
        PadSize padSize = AV.PadSizeOf(padDef);
        int clearance = AV.ClearanceFor(padSize);
        int corridorWidth = clearance * 2 + 1;
        DrawTextLine(curX, ref curY, $"{AV.PadSizeName(padSize)}  ·  机头朝向：{ParkingPad.HeadingName(rot)}");
        DrawTextLine(curX, ref curY, PreviewRunway == null
            ? (padSize == PadSize.Small ? "无跑道（轻型战斗机可垂直起降）" : "无跑道（需划定跑道区）")
            : path == null ? $"滑行通道受阻（需{corridorWidth}格宽净空）" : $"最近跑道：{PreviewRunway.label}；滑行通道可用");
    }
}

public class ParkingPad : Building
{
    public Aircraft bound;
    public Aircraft reserved;
    public RunwayZone runway;
    private List<IntVec3> previewPath;
    private int previewTick = -99999;
    private IntVec3 previewStart;
    private Aircraft previewPlane;
    public PadSize PadSize => AV.PadSizeOf(def);
    public int PadClearance => AV.ClearanceFor(PadSize);
    public bool Supports(Aircraft plane) => plane == null || PadSize >= plane.RequiredPadSize;
    public float Heading => Rotation.AsAngle;
    public static string HeadingName(Rot4 rot) => rot.AsInt switch { 0 => "北", 1 => "东", 2 => "南", _ => "西" };
    public static void DrawHeading(Vector3 center, float heading, CellRect? rect = null)
    {
        float halfSpan = rect.HasValue ? Math.Min(rect.Value.Width, rect.Value.Height) * 0.5f : 5.5f;
        float tailStart = Math.Max(1.8f, halfSpan - 2.6f);
        float tipDist = Math.Max(3.2f, halfSpan - 0.8f);
        Vector3 forward = AV.Dir(heading), side = AV.Dir(heading + 90), tip = center + forward * tipDist;
        AV.Line(center + forward * tailStart, tip, SimpleColor.Cyan);
        AV.Line(tip, tip - forward * 1.1f + side * 0.8f, SimpleColor.Cyan);
        AV.Line(tip, tip - forward * 1.1f - side * 0.8f, SimpleColor.Cyan);
    }
    public static void DrawOutline(CellRect r, float y, Color borderColor, Color accentColor, float heading)
    {
        float minX = r.minX + 0.12f, maxX = r.maxX + 0.88f;
        float minZ = r.minZ + 0.12f, maxZ = r.maxZ + 0.88f;
        Vector3 bl = new(minX, y, minZ), br = new(maxX, y, minZ), tl = new(minX, y, maxZ), tr = new(maxX, y, maxZ);
        Material borderMat = SolidColorMaterials.SimpleSolidColorMaterial(borderColor);
        Material accentMat = SolidColorMaterials.SimpleSolidColorMaterial(accentColor);
        // Outer perimeter frame
        GenDraw.DrawLineBetween(bl, br, borderMat, 0.14f);
        GenDraw.DrawLineBetween(bl, tl, borderMat, 0.14f);
        GenDraw.DrawLineBetween(tr, tl, borderMat, 0.14f);
        GenDraw.DrawLineBetween(tr, br, borderMat, 0.14f);
        // Bold L-shaped corner brackets
        float corner = Mathf.Clamp(Math.Min(r.Width, r.Height) * 0.18f, 1.4f, 2.4f);
        float cy = y + 0.004f;
        void Corner(Vector3 c, float dx, float dz)
        {
            Vector3 p = new(c.x, cy, c.z);
            GenDraw.DrawLineBetween(p, new Vector3(c.x + dx, cy, c.z), accentMat, 0.28f);
            GenDraw.DrawLineBetween(p, new Vector3(c.x, cy, c.z + dz), accentMat, 0.28f);
        }
        Corner(bl, corner, corner);
        Corner(br, -corner, corner);
        Corner(tl, corner, -corner);
        Corner(tr, -corner, -corner);
        // Center guide cross + nose centerline
        Vector3 center = new((minX + maxX) * 0.5f, cy, (minZ + maxZ) * 0.5f);
        Vector3 forward = AV.Dir(heading), side = AV.Dir(heading + 90f);
        float halfSpan = Math.Min(r.Width, r.Height) * 0.5f;
        float crossHalf = Mathf.Clamp(halfSpan * 0.25f, 1.1f, 2.0f);
        GenDraw.DrawLineBetween(center - side * crossHalf, center + side * crossHalf, borderMat, 0.12f);
        GenDraw.DrawLineBetween(center - forward * (halfSpan - 1.2f), center + forward * (halfSpan - 2.6f), borderMat, 0.12f);
    }
    public void Bind(Aircraft plane)
    {
        if (plane != null && !Supports(plane))
        {
            AV.Say($"{LabelCap}尺寸不足，无法绑定{plane.LabelCap}（至少需要{AV.PadSizeName(plane.RequiredPadSize)}）。");
            return;
        }
        if (bound != null && bound != plane && bound.homePad == this) bound.homePad = null;
        if (plane != null)
        {
            if (plane.homePad != null && plane.homePad != this && plane.homePad.bound == plane) plane.homePad.bound = null;
            foreach (Map m in Find.Maps) foreach (ParkingPad p in AV.Pads(m)) if (p != this && p.bound == plane) { p.bound = null; if (p.reserved == plane && plane.State == FlightState.Parked) p.reserved = null; }
            plane.homePad = this;
        }
        bound = plane; previewTick = -99999;
    }
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        CellRect r = this.OccupiedRect();
        float y = Altitudes.AltitudeFor(AltitudeLayer.FloorEmplacement) + 0.01f;
        Color baseCol = PadSize switch
        {
            PadSize.Small => new Color(0.55f, 0.85f, 0.78f),
            PadSize.Large => new Color(0.95f, 0.80f, 0.42f),
            _ => new Color(0.58f, 0.80f, 0.92f)
        };
        Color accentCol = new Color(Mathf.Min(1f, baseCol.r + 0.18f), Mathf.Min(1f, baseCol.g + 0.18f), Mathf.Min(1f, baseCol.b + 0.18f));
        DrawOutline(r, y, baseCol, accentCol, Heading);
    }
    public override void DrawExtraSelectionOverlays() { base.DrawExtraSelectionOverlays(); DrawHeading(DrawPos, Heading, this.OccupiedRect()); DrawConnection(); }
    public void DrawConnection()
    {
        if (bound != null && !bound.UsesRunwayTakeoff) return;
        RunwayZone z = runway != null && Map.zoneManager.AllZones.Contains(runway) ? runway : AV.NearestRunway(Map, Position);
        if (z == null || z.CellCount == 0) return;
        Aircraft p = bound;
        if (Find.TickManager.TicksGame - previewTick > 120 || previewStart != z.Start || previewPlane != p)
        {
            previewTick = Find.TickManager.TicksGame; previewStart = z.Start; previewPlane = p;
            previewPath = AV.TaxiPath(Map, Position, z.Start, p, p == null ? PadClearance : null);
        }
        if (previewPath == null) AV.Line(DrawPos, AV.Flat(z.Start), SimpleColor.Red);
        else AV.DrawPath(previewPath, DrawPos, SimpleColor.Cyan);
    }
    public bool Available(Aircraft plane)
    {
        if (!Supports(plane)) return false;
        if (reserved != null && (reserved.Destroyed || !reserved.Spawned)) reserved = null;
        int selfClearance = plane?.Clearance ?? PadClearance;
        return (bound == null || bound == plane || bound.Destroyed) && (reserved == null || reserved == plane)
            && !AV.Planes(Map).Any(p => p != plane && !p.Airborne
                && p.Position.InHorDistOf(Position, Math.Max(7, (p.Clearance + selfClearance + 2) / 2)));
    }
    public override string GetInspectString() => AV.Inspect(base.GetInspectString(),
        $"规格：{AV.PadSizeName(PadSize)}（向上兼容更小机型）\n绑定：" + (bound?.LabelCap ?? "自动分配")
        + "\n跑道：" + (bound != null && !bound.UsesRunwayTakeoff ? "垂直起飞，无需跑道" : runway?.label ?? "最近跑道")
        + "\n机头朝向：" + HeadingName(Rotation) + "\n预留：" + (reserved?.LabelCap ?? "空闲"));
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo g in base.GetGizmos()) yield return g;
        yield return AV.Button("机头朝向：" + HeadingName(Rotation), "指定停放时的机头方向。", () => Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0, 4)
            .Select(i => new FloatMenuOption(HeadingName(new Rot4(i)), () => Rotation = new Rot4(i))).ToList())), AviationArt.Command("Apron_Facing"));
        yield return AV.Button("关联跑道", "选择优先使用的跑道。", () => Find.WindowStack.Add(new FloatMenu(Map.zoneManager.AllZones.OfType<RunwayZone>()
            .Select(z => new FloatMenuOption(z.label, () => runway = z)).Append(new FloatMenuOption("自动选择", () => runway = null)).ToList())), AviationArt.Command("Apron_LinkRunway"));
        yield return AV.Button("绑定飞机", "为指定飞机保留停机坪。", () => Find.WindowStack.Add(new FloatMenu(AV.Planes(Map).Where(p => p.Faction == Faction.OfPlayer && Supports(p))
            .Select(p => new FloatMenuOption(p.LabelCap, () => Bind(p))).Append(new FloatMenuOption("解除绑定", () => Bind(null))).ToList())), AviationArt.Command("Apron_LinkPlane"));
    }
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_References.Look(ref bound, "bound"); Scribe_References.Look(ref reserved, "reserved"); Scribe_References.Look(ref runway, "runway");
    }
}

// Do not let CompTransporter eject passengers during our explicit map/world ownership transfer.
public class AircraftTransporter : CompTransporter
{
    public override bool RequiresFuelingPort => false;
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) { }
    public override void PostExposeData()
    {
        // Own the passengers deeply in every state. Vanilla transporters switch to WorldPawns references
        // off-map; custom flights are not vanilla transport-pod world objects and must retain ownership.
        Scribe_Values.Look(ref groupID, "groupID", -1);
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_Collections.Look(ref leftToLoad, "leftToLoad", LookMode.Deep);
        Scribe_Values.Look(ref massCapacityOverride, "massCapacityOverride", -1f);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) innerContainer ??= new ThingOwner<Thing>(this);
    }
    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (parent is Aircraft p && p.State != FlightState.Parked) yield break;
        foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
    }
}
public class AircraftTransporterProps : CompProperties_Transporter
{
    public AircraftTransporterProps() { compClass = typeof(AircraftTransporter); }
}
