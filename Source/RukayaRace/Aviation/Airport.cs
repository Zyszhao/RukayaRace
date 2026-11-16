using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public static class AV
{
    public static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed("RAV_" + name + (name is "Yak141" or "Mig31" ? "Aircraft" : ""));
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
    public static Vector3 Flat(IntVec3 c) => c.ToVector3Shifted();
    public static Vector3 Dir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    public static float Distance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    public static IEnumerable<Aircraft> Planes(Map m) => m.listerThings.AllThings.OfType<Aircraft>();
    public static IEnumerable<ParkingPad> Pads(Map m) => m.listerThings.AllThings.OfType<ParkingPad>();
    public static RunwayZone NearestRunway(Map map, IntVec3 position) => map.zoneManager.AllZones.OfType<RunwayZone>()
        .Where(z => z.CellCount > 0).OrderBy(z => z.Start.DistanceToSquared(position)).FirstOrDefault();
    public static bool SafeAir(IntVec3 c, Map m, int margin = 24)
        => c.x >= margin && c.z >= margin && c.x < m.Size.x - margin && c.z < m.Size.z - margin;

    // A conservative square swept envelope covers the rendered body during turns.
    public static bool Clear(IntVec3 center, Map map, int radius, Aircraft self = null, bool roof = true)
    {
        foreach (IntVec3 c in CellRect.CenteredOn(center, radius))
        {
            if (!c.InBounds(map) || c.Fogged(map) || !c.Walkable(map) || (roof && c.Roofed(map))) return false;
            TerrainDef terrain = c.GetTerrain(map);
            if (terrain == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.Light)) return false;
            foreach (Thing t in c.GetThingList(map))
            {
                if (t == self || t is ParkingPad || (t is Aircraft p && p.Airborne)) continue;
                if (t is Aircraft || t is Plant plant && plant.def.plant.IsTree
                    || t.def.category == ThingCategory.Building && t.def.fillPercent > 0.1f) return false;
            }
        }
        return true;
    }

    public static List<IntVec3> TaxiPath(Map map, IntVec3 from, IntVec3 to, Aircraft plane)
    {
        var queue = new Queue<IntVec3>();
        var parents = new Dictionary<IntVec3, IntVec3>();
        queue.Enqueue(from); parents[from] = IntVec3.Invalid;
        while (queue.Count > 0 && parents.Count < 40000)
        {
            IntVec3 c = queue.Dequeue();
            if (c == to)
            {
                var result = new List<IntVec3>();
                for (IntVec3 n = to; n.IsValid; n = parents[n]) result.Add(n);
                result.Reverse(); return result;
            }
            foreach (IntVec3 d in GenAdj.CardinalDirections)
            {
                IntVec3 n = c + d;
                if (parents.ContainsKey(n) || !Clear(n, map, plane?.Clearance ?? 5, plane)) continue;
                parents[n] = c; queue.Enqueue(n);
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
    protected override Color NextZoneColor => new Color(0.3f, 0.75f, 0.95f, 0.3f);
    public RunwayZone() { }
    public RunwayZone(ZoneManager manager) : base("跑道", manager) { }
    public CellRect Rect => cells.Count == 0 ? CellRect.Empty : CellRect.FromLimits(cells.Min(c => c.x), cells.Min(c => c.z), cells.Max(c => c.x), cells.Max(c => c.z));
    public bool AlongZ => Rect.Height >= Rect.Width;
    public IntVec3 Start => AlongZ ? new IntVec3(Rect.CenterCell.x, 0, reverse ? Rect.maxZ - 4 : Rect.minZ + 4)
        : new IntVec3(reverse ? Rect.maxX - 4 : Rect.minX + 4, 0, Rect.CenterCell.z);
    public IntVec3 End => AlongZ ? new IntVec3(Rect.CenterCell.x, 0, reverse ? Rect.minZ + 4 : Rect.maxZ - 4)
        : new IntVec3(reverse ? Rect.minX + 4 : Rect.maxX - 4, 0, Rect.CenterCell.z);
    public IntVec3 LandingStart => oppositeLanding ? End : Start;
    public IntVec3 LandingEnd => oppositeLanding ? Start : End;
    public string Error(Aircraft plane)
    {
        CellRect r = Rect;
        if (reserved != null && (!reserved.Spawned || reserved.Destroyed || reserved.State == FlightState.Parked)) reserved = null;
        if (reserved != null && reserved != plane) return "跑道已被其他飞机预留";
        if (CellCount != r.Area || Math.Min(r.Width, r.Height) < 9 || Math.Max(r.Width, r.Height) < 44)
            return "跑道必须是至少 9×44 格的完整矩形";
        if (!AV.SafeAir(Start, Map, 18) || !AV.SafeAir(End, Map, 18)) return "跑道距地图边缘不足 18 格";
        foreach (IntVec3 c in ClearanceCells)
            if (!AV.Clear(c, Map, 0, plane)) return "跑道阻挡：" + BlockDescription(c, plane);
        if (plane != null)
        {
            IntVec3 direction = AlongZ ? IntVec3.North : IntVec3.East;
            IntVec3 begin = AlongZ ? (Start.z < End.z ? Start : End) : (Start.x < End.x ? Start : End);
            int length = Mathf.Abs(End.x - Start.x) + Mathf.Abs(End.z - Start.z);
            for (int i = 0; i <= length; i++) if (!AV.Clear(begin + direction * i, Map, plane.Clearance, plane)) return "跑道肩部或端部净空不足";
        }
        return null;
    }
    public IEnumerable<IntVec3> ClearanceCells => Rect.ExpandedBy(1).Cells.Where(c => c.InBounds(Map));
    public List<IntVec3> BlockedCells(Aircraft plane = null) => ClearanceCells.Where(c => !AV.Clear(c, Map, 0, plane)).ToList();
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
        foreach (Plant plant in ClearanceCells.SelectMany(c => c.GetThingList(Map)).OfType<Plant>().Distinct())
            if (plant.def.plant.allowAutoCut && !plant.DeliberatelyCultivated() && !Map.designationManager.HasMapDesignationOn(plant))
                Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.CutPlant));
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
        }
        if (Find.Selector.IsSelected(this)) GenDraw.DrawFieldEdges(BlockedCells(reserved).Take(96).ToList(), Color.red);
    }
    public override string GetInspectString() => AV.Inspect(base.GetInspectString(), $"{Rect.Width}×{Rect.Height} 格；起飞 {Start} → {End}\n降落 {LandingStart} → {LandingEnd}\n" + (Error(null) ?? "跑道可用"));
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
    }
}

public class Alert_RunwayBlocked : Alert
{
    public Alert_RunwayBlocked()
    {
        defaultLabel = "跑道受阻";
        defaultExplanation = "跑道或道肩存在屋顶、树木、建筑或不适用的地形。点击提示定位，选中跑道可查看红色阻挡标记；启用自动割除可安排殖民者清除野生植物。";
    }
    public override AlertReport GetReport() => AlertReport.CulpritsAre(Find.Maps
        .Where(m => m.IsPlayerHome).SelectMany(m => m.zoneManager.AllZones.OfType<RunwayZone>())
        .Where(z => z.CellCount > 0 && z.BlockedCells(z.reserved).Count > 0)
        .Select(z => new GlobalTargetInfo(z.BlockedCells(z.reserved)[0], z.Map)).ToList());
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
        defaultDesc = "划出至少9×44格的矩形跑道，距地图边缘18格。跑道、道肩及11格宽的滑行通道需要净空。";
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
    public override bool ForceAllowPlaceOver(BuildableDef other) => other == AV.Def("Pad");
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        ParkingPad pad = AV.Pads(map).FirstOrDefault(p => p.Position == loc && p.Faction == Faction.OfPlayer);
        if (pad == null) return "飞机必须放置在己方已建成停机坪的中心。";
        if (pad.bound != null && !pad.bound.Destroyed && pad.bound != thing && pad.bound != thingToIgnore
            || pad.reserved != null && !pad.reserved.Destroyed && pad.reserved != thing && pad.reserved != thingToIgnore) return "此停机坪已绑定或预留给另一架飞机。";
        if (AV.Planes(map).Any(p => p != thing && p != thingToIgnore && !p.Airborne && p.Position.InHorDistOf(loc, 7))) return "停机坪已有飞机。";
        foreach (Thing t in map.listerThings.AllThings)
            if (t != thing && t != thingToIgnore && t.Position == loc && (t is Blueprint || t is Frame)
                && t.def.entityDefToBuild is ThingDef def && typeof(Aircraft).IsAssignableFrom(def.thingClass)) return "停机坪已有飞机建造任务。";
        return true;
    }
}

public class PlaceWorker_ParkingPad : PlaceWorker
{
    private Map previewMap;
    private IntVec3 previewPosition, previewStart;
    private List<IntVec3> path;
    private float nextRefresh;
    public RunwayZone PreviewRunway { get; private set; }
    public IReadOnlyList<IntVec3> PreviewPath => path;
    public void UpdatePreview(Map map, IntVec3 position)
    {
        RunwayZone nearest = AV.NearestRunway(map, position);
        IntVec3 start = nearest?.Start ?? IntVec3.Invalid;
        if (previewMap == map && previewPosition == position && PreviewRunway == nearest && previewStart == start && Time.realtimeSinceStartup < nextRefresh) return;
        previewMap = map; previewPosition = position; previewStart = start; PreviewRunway = nearest; nextRefresh = Time.realtimeSinceStartup + 0.5f;
        path = nearest == null || !AV.Clear(position, map, 5) ? null : AV.TaxiPath(map, position, start, null);
    }
    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        Map map = Find.CurrentMap; if (map == null) return;
        UpdatePreview(map, center); ParkingPad.DrawHeading(AV.Flat(center), rot.AsAngle);
        if (PreviewRunway == null) return;
        if (path == null) AV.Line(AV.Flat(center), AV.Flat(PreviewRunway.Start), SimpleColor.Red);
        else AV.DrawPath(path, AV.Flat(center), SimpleColor.Cyan);
    }
    public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)
    {
        if (Find.CurrentMap == null) return;
        UpdatePreview(Find.CurrentMap, center);
        DrawTextLine(curX, ref curY, PreviewRunway == null ? "无跑道（垂直起降可用）" : path == null ? "滑行通道受阻（需11格宽净空）" : $"最近跑道：{PreviewRunway.label}；滑行通道可用");
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
    public float Heading => Rotation.AsAngle;
    public static string HeadingName(Rot4 rot) => rot.AsInt switch { 0 => "北", 1 => "东", 2 => "南", _ => "西" };
    public static void DrawHeading(Vector3 center, float heading)
    {
        Vector3 forward = AV.Dir(heading), side = AV.Dir(heading + 90), tip = center + forward * 4.5f;
        AV.Line(center + forward * 3, tip, SimpleColor.Cyan);
        AV.Line(tip, tip - forward + side * 0.7f, SimpleColor.Cyan); AV.Line(tip, tip - forward - side * 0.7f, SimpleColor.Cyan);
    }
    public void Bind(Aircraft plane)
    {
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
        CellRect r = this.OccupiedRect(); float y = Altitudes.AltitudeFor(AltitudeLayer.FloorEmplacement) + 0.01f;
        Vector3 a = new Vector3(r.minX + 0.12f, y, r.minZ + 0.12f), b = new Vector3(r.maxX + 0.88f, y, r.maxZ + 0.88f);
        Material mat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.65f, 0.8f, 0.85f));
        GenDraw.DrawLineBetween(a, new Vector3(b.x, y, a.z), mat, 0.16f); GenDraw.DrawLineBetween(a, new Vector3(a.x, y, b.z), mat, 0.16f);
        GenDraw.DrawLineBetween(b, new Vector3(a.x, y, b.z), mat, 0.16f); GenDraw.DrawLineBetween(b, new Vector3(b.x, y, a.z), mat, 0.16f);
    }
    public override void DrawExtraSelectionOverlays() { base.DrawExtraSelectionOverlays(); DrawHeading(DrawPos, Heading); DrawConnection(); }
    public void DrawConnection()
    {
        if (bound != null && !bound.UsesRunwayTakeoff) return;
        RunwayZone z = runway != null && Map.zoneManager.AllZones.Contains(runway) ? runway : AV.NearestRunway(Map, Position);
        if (z == null || z.CellCount == 0) return;
        Aircraft p = bound;
        if (Find.TickManager.TicksGame - previewTick > 120 || previewStart != z.Start || previewPlane != p)
        {
            previewTick = Find.TickManager.TicksGame; previewStart = z.Start; previewPlane = p;
            previewPath = AV.TaxiPath(Map, Position, z.Start, p);
        }
        if (previewPath == null) AV.Line(DrawPos, AV.Flat(z.Start), SimpleColor.Red);
        else AV.DrawPath(previewPath, DrawPos, SimpleColor.Cyan);
    }
    public bool Available(Aircraft plane)
    {
        if (reserved != null && (reserved.Destroyed || !reserved.Spawned)) reserved = null;
        return (bound == null || bound == plane || bound.Destroyed) && (reserved == null || reserved == plane) && !AV.Planes(Map).Any(p => p != plane && !p.Airborne && p.Position.InHorDistOf(Position, 7));
    }
    public override string GetInspectString() => AV.Inspect(base.GetInspectString(), "绑定：" + (bound?.LabelCap ?? "自动分配")
        + "\n跑道：" + (bound != null && !bound.UsesRunwayTakeoff ? "垂直起飞，无需跑道" : runway?.label ?? "最近跑道") + "\n机头朝向：" + HeadingName(Rotation) + "\n预留：" + (reserved?.LabelCap ?? "空闲"));
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo g in base.GetGizmos()) yield return g;
        yield return AV.Button("机头朝向：" + HeadingName(Rotation), "指定停放时的机头方向。", () => Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0, 4)
            .Select(i => new FloatMenuOption(HeadingName(new Rot4(i)), () => Rotation = new Rot4(i))).ToList())), AviationArt.Command("Apron_Facing"));
        yield return AV.Button("关联跑道", "选择优先使用的跑道。", () => Find.WindowStack.Add(new FloatMenu(Map.zoneManager.AllZones.OfType<RunwayZone>()
            .Select(z => new FloatMenuOption(z.label, () => runway = z)).Append(new FloatMenuOption("自动选择", () => runway = null)).ToList())), AviationArt.Command("Apron_LinkRunway"));
        yield return AV.Button("绑定飞机", "为指定飞机保留停机坪。", () => Find.WindowStack.Add(new FloatMenu(AV.Planes(Map).Where(p => p.Faction == Faction.OfPlayer)
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
