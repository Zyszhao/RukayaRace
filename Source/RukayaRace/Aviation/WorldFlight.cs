using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public class WorldFlight : WorldObject, IThingHolder
{
    public ThingOwner<Thing> contents;
    public PlanetTile origin, destination;
    public int ticksLeft, totalTicks;
    public int retryTicks;
    public bool arrivalQueued;
    public bool firstArrival;
    public bool docked;
    public Settlement dockSettlement;
    public PlanetTile returnTile = PlanetTile.Invalid;
    public Aircraft Plane => contents?.OfType<Aircraft>().FirstOrDefault();
    public WorldFlight() { contents = new ThingOwner<Thing>(this); }
    protected override int UpdateRateTicks => 1;
    // Resolve from this flight's cargo; the shared WorldObjectDef is also used by
    // other aircraft and must not be changed, including during save loading.
    public override Material Material => Plane is Aircraft plane
        ? MaterialPool.MatFrom(plane.def.graphicData.texPath, def.shader?.Shader ?? ShaderDatabase.WorldOverlayTransparentLit, 3550)
        : base.Material;
    public override Texture2D ExpandingIcon => Plane is Aircraft plane
        ? AV.Icon(plane.def.graphicData.texPath) : base.ExpandingIcon;
    public override Material ExpandingMaterial => def.expandingShader == null ? null
        : MaterialPool.MatFrom(new MaterialRequest
        {
            mainTex = ExpandingIcon, shader = def.expandingShader.Shader,
            color = Color.white, maskTex = def.ExpandingIconTextureMask
        });
    public override string Label => Plane?.LabelCap + (docked ? dockSettlement != null ? "（驻留据点）" : "（驻留）" : "（飞行中）");
    public override Vector3 DrawPos => docked ? Find.WorldGrid.GetTileCenter(Tile) : Vector3.Slerp(Find.WorldGrid.GetTileCenter(origin), Find.WorldGrid.GetTileCenter(destination),
        totalTicks > 0 ? 1f - (float)ticksLeft / totalTicks : 1f);
    public ThingOwner GetDirectlyHeldThings() => contents;
    public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);

    public static void Choose(Aircraft plane)
    {
        if ((plane.State == FlightState.Flying ? plane.PayloadError() : plane.FlightError()) is string error) { AV.Say(error); return; }
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(plane));
        Find.WorldTargeter.BeginTargeting(t =>
        {
            if (!t.Tile.Valid || t.Tile.Layer != plane.Map.Tile.Layer || t.Tile == plane.Map.Tile) { AV.Say("请选择同一星球表面的其他地块。"); return false; }
            int distance = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(plane.Map.Tile, t.Tile));
            if (distance > plane.Range || plane.fuel < FuelCost(plane, distance) + (plane.State == FlightState.Parked ? plane.TakeoffFuelCost + 3 : 0)) { AV.Say("超出航程或燃料不足。"); return false; }
            if (Find.World.Impassable(t.Tile)) { AV.Say("不能在海洋或不可通行地块降落。"); return false; }
            Map existing = Current.Game.FindMap(t.Tile);
            if (!CanEnterTask(plane, t.Tile)) { AV.Say("任务地点当前无法进入。"); return false; }
            if (!plane.arriveAirborne && existing?.IsPlayerHome == true && FindArrival(existing, plane, out _, out _) == false) { AV.Say("目的地没有空闲停机坪、合格跑道或通道。"); return false; }
            plane.worldDestination = t.Tile; plane.worldDeparture = true;
            if (plane.State == FlightState.Flying) { Depart(plane); return !plane.Spawned; }
            if (!plane.BeginTakeoff()) { plane.worldDeparture = false; return false; }
            return true;
        }, true, closeWorldTabWhenFinished: true, extraLabelGetter: t =>
        {
            if (!t.Tile.Valid) return "选择目的地";
            int d = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(plane.Map.Tile, t.Tile));
            return $"距离 {d}/{plane.Range}  ·  燃料 {FuelCost(plane, d) + (plane.State == FlightState.Parked ? plane.TakeoffFuelCost : 0):F0}  ·  航时 {plane.WorldTravelTicks(d).ToStringTicksToPeriod()}";
        });
    }
    public static float FuelCost(Aircraft plane, int distance) => 5 + distance * (plane.Heavy ? 1.8f : 2.8f);
    private static bool CanEnterTask(Aircraft plane, PlanetTile tile) => Find.WorldObjects.MapParentAt(tile) is not Site site
        || (bool)TransportersArrivalAction_VisitSite.CanVisit(new IThingHolder[] { plane.Cargo }, site);
    private void StayOnWorld(Settlement settlement = null)
    {
        docked = true; dockSettlement = settlement; Tile = destination; ticksLeft = 0;
        Plane.worldDeparture = false; Plane.worldDestination = PlanetTile.Invalid;
        CameraJumper.TryJumpAndSelect(this);
        Messages.Message(settlement != null ? "飞机已抵达据点。" : "飞机已抵达，在世界地图驻留。", MessageTypeDefOf.PositiveEvent, false);
    }
    public static void Depart(Aircraft plane)
    {
        if (!plane.worldDestination.Valid) { plane.worldDeparture = false; return; }
        Map sourceMap = plane.Map; int distance = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(sourceMap.Tile, plane.worldDestination));
        float cost = FuelCost(plane, distance);
        if (plane.fuel < cost) { plane.worldDeparture = false; AV.Say("燃料不足，取消世界地图出发。"); return; }
        WorldFlight flight = (WorldFlight)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("RAV_Flight"));
        flight.origin = sourceMap.Tile; flight.destination = plane.worldDestination; flight.Tile = flight.origin; flight.SetFaction(plane.Faction);
        flight.returnTile = sourceMap.Tile;
        flight.totalTicks = flight.ticksLeft = plane.WorldTravelTicks(distance);
        if (!plane.returnBaseTile.Valid || sourceMap.IsPlayerHome) plane.returnBaseTile = sourceMap.Tile;
        flight.firstArrival = Current.Game.FindMap(flight.destination) == null;
        plane.fuel -= cost; plane.ReleaseReservations(); plane.transferInProgress = true;
        // Vanilla CleanUpLoadingVars ejects the entire container. Finish only the loading job here.
        plane.Cargo.TryRemoveLord(sourceMap); plane.Cargo.groupID = -1; plane.Cargo.leftToLoad?.Clear();
        plane.DeSpawn(); plane.transferInProgress = false;
        flight.contents.TryAdd(plane); Find.WorldObjects.Add(flight);
        Messages.Message("飞机已进入世界地图航线。", MessageTypeDefOf.NeutralEvent, false);
    }
    protected override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        if (Plane == null) return;
        Plane.Cargo.innerContainer.DoTick();
        if (docked) return;
        if (arrivalQueued) return;
        if (retryTicks > 0) { retryTicks -= delta; return; }
        ticksLeft = Math.Max(0, ticksLeft - delta);
        if (ticksLeft > 0) return;
        arrivalQueued = true;
        LongEventHandler.QueueLongEvent(() =>
        {
            try { Arrive(); }
            catch (Exception e) { Log.Error("[Rukaya Aviation] Arrival retained in world object: " + e); }
            finally { arrivalQueued = false; }
        }, "GeneratingMap", false, null);
        retryTicks = 600; // Occupied destination: keep aircraft and payload safely in holding orbit.
    }
    public void Arrive()
    {
        Aircraft plane = Plane; if (plane == null) return;
        if (!plane.returnBaseTile.Valid) plane.returnBaseTile = returnTile.Valid ? returnTile : origin;
        Settlement settlement = Find.WorldObjects.SettlementAt(destination);
        if (settlement != null && settlement.Faction != null && !settlement.Faction.IsPlayer
            && !settlement.Faction.HostileTo(plane.Faction) && (bool)TransportersArrivalAction_VisitSettlement.CanVisit(new IThingHolder[] { plane.Cargo }, settlement))
        {
            StayOnWorld(settlement);
            return;
        }
        Map map = Current.Game.FindMap(destination);
        Site site = Find.WorldObjects.MapParentAt(destination) as Site;
        if (site != null && !CanEnterTask(plane, destination)) { StayOnWorld(); return; }
        // Empty tiles are world stops. Only an existing task site may request a
        // new map, using that site's generator and preferred size as vanilla does.
        if (map == null && site == null) { StayOnWorld(); return; }
        if (map == null)
        {
            map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, site.PreferredMapSize, null);
            if (map != null)
            {
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
                PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter_Send(map.mapPawns.AllPawns, "LetterRelatedPawnsInMapWherePlayerLanded".Translate(Faction.OfPlayer.def.pawnsPlural), LetterDefOf.NeutralEvent, informEvenIfSeenBefore: true);
                if (site.Faction != null && site.Faction != Faction.OfPlayer && site.MainSitePartDef?.considerEnteringAsAttack == true)
                    Faction.OfPlayer.TryAffectGoodwillWith(site.Faction, Faction.OfPlayer.GoodwillToMakeHostile(site.Faction), canSendMessage: true, canSendHostilityLetter: true, HistoryEventDefOf.AttackedSettlement);
            }
        }
        if (map == null) return;
        ParkingPad pad = null; RunwayZone runway = null; IntVec3 pos = IntVec3.Invalid;
        if (plane.arriveAirborne)
        {
            pos = map.AllCells.OrderBy(c => c.DistanceToSquared(map.Center)).FirstOrDefault(c => AV.SafeAir(c, map, 42), IntVec3.Invalid);
            if (!pos.IsValid) return;
        }
        else if (map.IsPlayerHome)
        {
            if (!FindArrival(map, plane, out pad, out runway)) return;
            pos = plane.Heavy ? runway.LandingStart : pad.Position;
        }
        else
        {
            pos = map.AllCells.OrderBy(c => c.DistanceToSquared(map.Center)).FirstOrDefault(c => AV.SafeAir(c, map, 24) && AV.Clear(c, map, plane.Clearance), IntVec3.Invalid);
            if (!pos.IsValid || !AV.Clear(pos, map, plane.Clearance)) return;
        }
        contents.Remove(plane); plane.State = FlightState.Parked; plane.worldDeparture = false; plane.worldDestination = PlanetTile.Invalid;
        plane.route.Clear(); plane.CancelAttack();
        GenSpawn.Spawn(plane, pos, map, Rot4.North);
        if (plane.arriveAirborne)
        {
            plane.State = FlightState.Flying; plane.yaw = 0; plane.Loiter(); plane.BoostEngines();
        }
        else if (pad != null) { pad.reserved = plane; plane.landingPad = pad; plane.activeRunway = runway; if (runway != null) runway.reserved = plane;
            if (plane.Heavy) { plane.State = FlightState.Landing; plane.phaseTicks = 0; plane.yaw = (AV.Flat(runway.LandingEnd) - AV.Flat(runway.LandingStart)).AngleFlat(); }
            else { plane.taxi = null; plane.State = FlightState.Landing; plane.phaseTicks = 0; plane.yaw = pad.Heading; } }
        plane.ResetFlightVisuals(); plane.Refuelable.SyncGroundAccess();
        Current.Game.CurrentMap = map; CameraJumper.TryJumpAndSelect(plane);
        Messages.Message(plane.arriveAirborne ? "飞机已进入目标空域。" : pad == null && plane.Heavy ? "飞机已着陆。再次起飞需要跑道。" : "飞机已抵达。", plane, MessageTypeDefOf.PositiveEvent);
        Destroy();
    }
    public static bool FindArrival(Map map, Aircraft plane, out ParkingPad pad, out RunwayZone runway)
    {
        pad = null; runway = null;
        foreach (ParkingPad p in AV.Pads(map).Where(p => p.Faction == plane.Faction && p.Available(plane)))
        {
            if (!AV.Clear(p.Position, map, plane.Clearance, plane)) continue;
            if (!plane.Heavy) { pad = p; return true; }
            foreach (RunwayZone z in map.zoneManager.AllZones.OfType<RunwayZone>().OrderBy(z => z == p.runway ? 0 : 1))
                if (z.Error(plane) == null && AV.TaxiPath(map, z.LandingEnd, p.Position, plane) != null) { pad = p; runway = z; return true; }
        }
        return false;
    }
    public bool LeaveDock(PlanetTile tile)
    {
        Aircraft p = Plane;
        if (!tile.Valid || tile == Tile || tile.Layer != Tile.Layer || Find.World.Impassable(tile)) return false;
        int distance = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(Tile, tile));
        if (distance > p.Range || p.fuel < FuelCost(p, distance) + p.TakeoffFuelCost + 3) { AV.Say("超出航程或燃料不足。可从货舱化合燃料中加注。"); return false; }
        if (p.Cargo.MassUsage + p.MountWeight > p.TakeoffLimit) { AV.Say("货舱和挂载超过起飞重量，请通过贸易减载。"); return false; }
        if (p.PayloadError() is string payload) { AV.Say(payload); return false; }
        Map map = Current.Game.FindMap(tile);
        if (!CanEnterTask(p, tile)) { AV.Say("任务地点当前无法进入。"); return false; }
        if (!p.arriveAirborne && map?.IsPlayerHome == true && !FindArrival(map, p, out _, out _)) { AV.Say("目的地机场暂不可用。"); return false; }
        p.fuel -= FuelCost(p, distance) + p.TakeoffFuelCost; origin = Tile; destination = tile; docked = false; dockSettlement = null;
        ticksLeft = totalTicks = p.WorldTravelTicks(distance); retryTicks = 0;
        firstArrival = map == null; return true;
    }
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo g in base.GetGizmos()) yield return g;
        if (Plane == null || Faction != Faction.OfPlayer) yield break;
        yield return AV.Button("抵达：" + (Plane.arriveAirborne ? "保持飞行" : "降落"), "选择抵达地图后的状态。", () => Plane.arriveAirborne = !Plane.arriveAirborne, AviationArt.ArrivalMode);
        if (!docked) yield break;
        if (dockSettlement != null && dockSettlement.Spawned) yield return AV.Button("据点贸易", "与据点交易货舱物资。", () =>
        {
            Aircraft p = Plane;
            Pawn negotiator = p.Cargo.innerContainer.OfType<Pawn>().FirstOrDefault(x => x.IsFreeColonist && !x.Downed && x.CanTradeWith(dockSettlement.Faction, dockSettlement.TraderKind).Accepted);
            if (negotiator == null || !(bool)TransportersArrivalAction_Trade.CanTradeWith(new IThingHolder[] { p.Cargo }, dockSettlement)) { AV.Say("此据点或乘员当前无法交易。"); return; }
            Find.WindowStack.Add(new AircraftTradeDialog(negotiator, new AircraftTrader(p, dockSettlement)));
        }, AviationArt.Trade);
        yield return AV.Button("货舱加油", "将货舱中的化合燃料加入油箱。", () => Plane.RefuelFromCargo(), "Things/Item/Resource/Chemfuel");
        yield return AV.Button("返航", "返回原出发地，检查目的地机场。", () => LeaveDock(returnTile), AviationArt.ReturnHome);
        yield return AV.Button("选择下一站", "选择同一星球表面的下一目的地。", () => Find.WorldTargeter.BeginTargeting(t => t.Tile.Valid && LeaveDock(t.Tile), true), AviationArt.Command("TakeoffWorld"));
    }
    public override string GetInspectString() => docked ? $"驻留：{dockSettlement?.LabelCap ?? "世界地图"}\n燃料 {Plane?.fuel:F0}；货舱 {Plane?.Cargo.MassUsage:F0} kg\n载荷效率 {Plane?.FlightPerformance:P0}" : $"{Plane?.LabelCap}\n预计抵达：{ticksLeft.ToStringTicksToPeriod()}\n抵达：{(Plane?.arriveAirborne == true ? "保持飞行" : "降落")}；载荷效率 {Plane?.FlightPerformance:P0}";
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_Deep.Look(ref contents, "contents", this); Scribe_Values.Look(ref origin, "origin"); Scribe_Values.Look(ref destination, "destination");
        Scribe_Values.Look(ref ticksLeft, "ticksLeft"); Scribe_Values.Look(ref totalTicks, "totalTicks"); Scribe_Values.Look(ref firstArrival, "firstArrival"); Scribe_Values.Look(ref retryTicks, "retryTicks");
        Scribe_Values.Look(ref docked, "docked"); Scribe_References.Look(ref dockSettlement, "dockSettlement"); Scribe_Values.Look(ref returnTile, "returnTile");
        if (Scribe.mode == LoadSaveMode.PostLoadInit) { contents ??= new ThingOwner<Thing>(this); arrivalQueued = false; }
    }
}
