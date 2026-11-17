using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RukayaAviation;

public enum FlightState { Parked, TaxiOut, TakingOff, Flying, Landing, TaxiIn, Crashing }
public enum AttackKind { None, Bomb, Rocket, Cannon, IncendiaryBomb, ClusterBomb, GroundMissile, ActiveMatterMissile }
public enum StoreKind { Empty, Bomb, Rocket, Cannon, Tank, CargoPod, PassengerPod, IncendiaryBomb, ClusterBomb, GroundMissile, ActiveMatterMissile }
public enum AttackPhase { None, Approach, Run, Exit, Hover, Target }

public class Hardpoint : IExposable
{
    public StoreKind kind;
    public int rounds;
    public float Weight => kind switch { StoreKind.Bomb or StoreKind.IncendiaryBomb => rounds * 70f, StoreKind.ClusterBomb => rounds * 90f, StoreKind.GroundMissile => 25 + rounds * 65f, StoreKind.ActiveMatterMissile => 100 + rounds * 450f, StoreKind.Rocket => 30 + rounds * 4f, StoreKind.Cannon => 90 + rounds * 0.15f, StoreKind.Tank => 120f, StoreKind.CargoPod => 80f, StoreKind.PassengerPod => 120f, _ => 0 };
    public static int Capacity(StoreKind k) => k switch { StoreKind.Bomb or StoreKind.IncendiaryBomb or StoreKind.ClusterBomb => 4, StoreKind.GroundMissile => 1, StoreKind.ActiveMatterMissile => 1, StoreKind.Rocket => 20, StoreKind.Cannon => 160, _ => 0 };
    public static string Name(StoreKind k) => k switch { StoreKind.Bomb => "高爆炸弹架", StoreKind.IncendiaryBomb => "燃烧弹架", StoreKind.ClusterBomb => "集束炸弹架", StoreKind.GroundMissile => "空对地导弹", StoreKind.ActiveMatterMissile => "活性物质导弹", StoreKind.Rocket => "火箭巢", StoreKind.Cannon => "机炮吊舱", StoreKind.Tank => "副油箱", StoreKind.CargoPod => "货物吊舱（+600 kg）", StoreKind.PassengerPod => "人员吊舱（+1席）", _ => "空载" };
    public static string Texture(StoreKind k) => k switch
    {
        StoreKind.Bomb => "Aviation/Stores/Bomb", StoreKind.IncendiaryBomb => "Aviation/Stores/IncendiaryBomb",
        StoreKind.ClusterBomb => "Aviation/Stores/ClusterBomb", StoreKind.Rocket => "Aviation/Stores/RocketPod",
        StoreKind.Cannon => "Aviation/Stores/CannonPod", StoreKind.GroundMissile => "Aviation/Stores/GroundMissile",
        StoreKind.ActiveMatterMissile => "Aviation/Stores/ActiveMatterMissile", StoreKind.Tank => "Aviation/Stores/DropTank",
        StoreKind.CargoPod => "Aviation/Stores/CargoPod", StoreKind.PassengerPod => "Aviation/Stores/PassengerPod",
        _ => "Things/Projectile/ShellHighExplosive"
    };
    public void ExposeData() { Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref rounds, "rounds"); }
}

public partial class Aircraft : Building, IAttackTarget, IRenameable
{
    protected override int MinTickIntervalRate => 1;
    public FlightState State;
    public Vector3 realPos;
    public float yaw;
    public AircraftFuel Refuelable => GetComp<AircraftFuel>();
    public float fuel
    {
        get => Refuelable.Fuel;
        set { float change = Mathf.Clamp(value, 0, MaxFuel) - fuel; if (change > 0) Refuelable.Refuel(change); else if (change < 0) Refuelable.ConsumeFuel(-change); }
    }
    private string customName;
    private int aircraftNumber;
    private float pendingFuelUnload;
    public float visualLift = -1;
    private Material bodyMaterial, shadowMaterial;
    private readonly Dictionary<StoreKind, Material> storeMaterials = new();
    public string BaseLabel => def.label + " " + aircraftNumber;
    public string RenamableLabel { get => customName ?? BaseLabel; set => customName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    public string InspectLabel => RenamableLabel;
    public override string Label => RenamableLabel;
    public override string LabelNoCount => RenamableLabel;
    public List<Hardpoint> mounts = new List<Hardpoint>();
    public ParkingPad homePad;
    public ParkingPad landingPad;
    public RunwayZone activeRunway;
    public List<IntVec3> taxi;
    public int taxiIndex;
    public List<Vector3> route = new List<Vector3>();
    public int routeIndex;
    private bool exactMoveRoute;
    public AttackKind attack;
    public Vector3 attackStart, attackEnd;
    public bool attacking;
    public AttackPhase attackPhase;
    public int shotsRemaining, shotIndex;
    public int rocketSalvo = 6;
    public List<Vector3> plannedImpacts = new();
    public Vector3 attackExit;
    public Vector3 attackRunStart, attackRunEnd;
    public int attackGeometryVersion = 1;
    public bool loitering;
    public Vector3 loiterCenter;
    public float loiterAngle;
    public int loiterDirection = 1;
    public const float LoiterRadius = 14f;
    public bool raidEntering, raidExiting;
    public int raidDuration = 3600;
    public bool landingRequested;
    public bool transferInProgress;
    public bool raid;
    public int ageTicks;
    public int passes;
    public int phaseTicks;
    public PlanetTile worldDestination = PlanetTile.Invalid;
    public bool worldDeparture;
    private int nextShot;
    private int nextTaxiRetry;
    private bool crewLimitWarned;

    public bool Heavy => def.defName == "RAV_Mig31Aircraft";
    // A swept envelope includes nose/tail as the aircraft turns, not just its wingspan.
    public int Clearance => Heavy ? 5 : 4;
    public float Speed => (Heavy ? 21f : 10f) * FlightPerformance;
    public float BaseFuel => Heavy ? 650f : 180f;
    public float DropTankCapacity => Heavy ? 120f : 60f;
    public float MaxFuel => BaseFuel + mounts.Count(m => m.kind == StoreKind.Tank) * DropTankCapacity;
    public int Range => (Heavy ? 180 : 45) + mounts.Count(m => m.kind == StoreKind.Tank) * (Heavy ? 35 : 15);
    public float MountWeight => mounts.Sum(m => m.Weight) + (!Heavy ? FixedCannon.Weight : 0);
    public float TakeoffLimit => Heavy ? 4200 : runwayTakeoff ? 1800 : 1100;
    public bool runwayTakeoff;
    public bool UsesRunwayTakeoff => Heavy || runwayTakeoff;
    public float TakeoffFuelCost => UsesRunwayTakeoff ? 5 : 18;
    public int HardpointCount => Heavy ? 7 : 5;
    public int CenterHardpoint => Heavy ? 3 : 2;
    public Hardpoint fixedCannon = new() { kind = StoreKind.Cannon, rounds = 160 };
    public Hardpoint FixedCannon => fixedCannon ??= new Hardpoint { kind = StoreKind.Cannon, rounds = 160 };
    public int StoreCapacity(int index, StoreKind kind) => Hardpoint.Capacity(kind) * (Heavy && index == CenterHardpoint && kind is StoreKind.Bomb or StoreKind.IncendiaryBomb or StoreKind.ClusterBomb or StoreKind.GroundMissile ? 2 : 1);
    public int CrewRequired => Heavy ? 2 : 1;
    public int Seats => CrewRequired + mounts.Count(m => m.kind == StoreKind.PassengerPod);
    public float CargoCapacity => (Heavy ? 1200 : 300) + mounts.Count(m => m.kind == StoreKind.CargoPod) * 600 + mounts.Count(m => m.kind == StoreKind.PassengerPod) * 100;
    public bool IsMiddleMount(int i) => Mathf.Abs(i - CenterHardpoint) <= 1;
    public bool CanMount(int i, StoreKind kind) => i >= 0 && i < HardpointCount && (Heavy || i != CenterHardpoint || kind is StoreKind.Empty or StoreKind.Tank or StoreKind.CargoPod or StoreKind.PassengerPod) && (kind != StoreKind.ActiveMatterMissile || Heavy && i == CenterHardpoint) && (kind is not (StoreKind.Tank or StoreKind.CargoPod or StoreKind.PassengerPod) || IsMiddleMount(i));
    public void RefreshCapacity() { if (Cargo != null) Cargo.massCapacityOverride = CargoCapacity; Refuelable?.RefreshCapacity(); }
    public void RemoveDropTankFuel()
    {
        // External tanks share their fill equally and feed the engine before the
        // internal tank. Never manufacture fuel when removing an empty tank.
        int tanks = mounts.Count(m => m.kind == StoreKind.Tank);
        float removed = tanks == 0 ? 0 : Mathf.Min(DropTankCapacity, Mathf.Max(0, fuel - BaseFuel) / tanks);
        fuel -= removed; pendingFuelUnload += removed; DropPendingFuel();
    }
    private void DropPendingFuel()
    {
        if (!Spawned || pendingFuelUnload < 1) return;
        while (pendingFuelUnload >= 1)
        {
            Thing stack = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
            int amount = Math.Min(ThingDefOf.Chemfuel.stackLimit, Mathf.FloorToInt(pendingFuelUnload)); stack.stackCount = amount;
            if (!GenPlace.TryPlaceThing(stack, Position, Map, ThingPlaceMode.Near)) { stack.Destroy(); return; }
            pendingFuelUnload -= amount;
        }
    }
    public string PayloadError()
    {
        if (Cargo.innerContainer.OfType<Pawn>().Count(p => p.IsFreeColonist && !p.Downed) < CrewRequired) return $"需要{CrewRequired}名清醒的殖民者担任机组。";
        if (Cargo.innerContainer.OfType<Pawn>().Count() > Seats) return $"乘员超过{Seats}席；请在中间挂点安装人员吊舱。";
        if (Cargo.MassUsage > CargoCapacity) return "超出货舱容量；请减载或安装货物吊舱。";
        if (Cargo.MassUsage + MountWeight > TakeoffLimit) return "超过起飞重量上限。";
        return null;
    }
    public CompTransporter Cargo => GetComp<CompTransporter>();
    public bool Airborne => State is FlightState.Flying or FlightState.Landing or FlightState.Crashing || State == FlightState.TakingOff && phaseTicks > 70;
    public Thing Thing => this;
    public LocalTargetInfo TargetCurrentlyAimingAt => LocalTargetInfo.Invalid;
    public float TargetPriorityFactor => 1f;
    public bool ThreatDisabled(IAttackTargetSearcher searcher) => State == FlightState.Parked;
    // Both layers are above the lighting overlay: changing fog occlusion must
    // never change lighting. Apply the same sky tint explicitly on both layers.
    public override Vector3 DrawPos => new Vector3(realPos.x, Altitudes.AltitudeFor(Airborne || Faction == Faction.OfPlayer && Spawned && Position.Fogged(Map) ? AltitudeLayer.MetaOverlays : AltitudeLayer.VisEffects) + 0.02f, realPos.z + Mathf.Max(0, visualLift) * 1.5f);
    public float TargetVisualLift
    {
        get
        {
            if (State == FlightState.TakingOff)
            {
                if (activeRunway == null) return Mathf.SmoothStep(0, 1, phaseTicks / 100f);
                float length = AV.Distance(AV.Flat(activeRunway.Start), AV.Flat(activeRunway.End));
                float progress = 1 - AV.Distance(realPos, AV.Flat(activeRunway.End)) / Mathf.Max(1, length);
                return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 0.97f, progress));
            }
            if (State == FlightState.Landing)
            {
                if (!Heavy) return 1 - Mathf.SmoothStep(0, 1, phaseTicks / 90f);
                if (activeRunway == null) return 1;
                float length = AV.Distance(AV.Flat(activeRunway.LandingStart), AV.Flat(activeRunway.LandingEnd));
                return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.15f, 1, AV.Distance(realPos, AV.Flat(activeRunway.LandingEnd)) / Mathf.Max(1, length)));
            }
            return State is FlightState.Flying or FlightState.Crashing ? 1 : 0;
        }
    }
    public void ResetFlightVisuals() => visualLift = TargetVisualLift;
    public override CellRect? CustomRectForSelector => CellRect.CenteredOn(DrawPos.ToIntVec3(), def.size);
    public Vector3 MountPosition(int i)
    {
        // Follow each texture's swept wing: outer stations sit farther aft.
        // Coordinates are relative to the square aircraft canvas, not its transparent margins.
        float side = i - CenterHardpoint;
        float widthScale = def.graphicData.drawSize.x / (Heavy ? 9f : 7f);
        float lengthScale = def.graphicData.drawSize.y / (Heavy ? 9f : 7f);
        Vector3 local = new Vector3(side * (Heavy ? 0.72f : 0.8f) * widthScale, 0,
            ((Heavy ? -0.8f : -0.85f) - Mathf.Abs(side) * (Heavy ? 0.35f : 0.05f)) * lengthScale);
        return realPos + Quaternion.AngleAxis(yaw, Vector3.up) * local;
    }
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        ClearFlightTrails();
        if (!respawningAfterLoad) { realPos = Position.ToVector3Shifted(); yaw = Rotation.AsAngle; }
        NormalizeHardpoints();
        aircraftNumber = Current.Game.GetComponent<AircraftNames>().Register(this, aircraftNumber);
        if (visualLift < 0) ResetFlightVisuals();
        RefreshCapacity();
        if (!respawningAfterLoad && State == FlightState.Parked && Faction == Faction.OfPlayer)
        {
            ParkingPad pad = AV.Pads(map).FirstOrDefault(p => p.Position == Position && p.Faction == Faction && (p.bound == null || p.bound.Destroyed) && (p.reserved == null || p.reserved == this));
            if (pad != null) { pad.Bind(this); yaw = pad.Heading; }
        }
    }
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        DrawWingTrails();
        float lift = Mathf.Max(0, visualLift);
        if (lift > 0.001f)
        {
            Vector3 shadow = realPos; shadow.y = Altitudes.AltitudeFor(AltitudeLayer.Shadows);
            shadowMaterial ??= new Material(MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent));
            shadowMaterial.color = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, lift));
            float size = Mathf.Lerp(0.7f, 1, lift);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(shadow, Quaternion.AngleAxis(yaw, Vector3.up), new Vector3(def.graphicData.drawSize.x * size, 1, def.graphicData.drawSize.y * size)), shadowMaterial, 0);
        }
        Color tint = Map?.skyManager.CurSky.colors.sky ?? Color.white; tint.a = 1;
        // Draw pylons below the aircraft, and use exactly the same positions for weapon origins.
        for (int i = 0; i < mounts.Count; i++)
        {
            Hardpoint m = mounts[i];
            if (!AviationArt.HasVisibleStore(m)) continue;
            Vector3 p = MountPosition(i); p.y = drawLoc.y - 0.005f; p.z += lift * 1.5f;
            if (!storeMaterials.TryGetValue(m.kind, out Material mat)) storeMaterials[m.kind] = mat = new Material(MaterialPool.MatFrom(Hardpoint.Texture(m.kind), ShaderDatabase.Cutout));
            mat.color = tint;
            Vector2 size = AviationArt.StoreSize(m.kind);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.AngleAxis(yaw, Vector3.up), new Vector3(size.x, 1, size.y)), mat, 0);
        }
        bodyMaterial ??= new Material(MaterialPool.MatFrom(def.graphicData.texPath, ShaderDatabase.Cutout));
        bodyMaterial.color = tint;
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(yaw, Vector3.up),
            new Vector3(def.graphicData.drawSize.x, 1, def.graphicData.drawSize.y)), bodyMaterial, 0);
    }
    public override void DrawExtraSelectionOverlays()
    {
        base.DrawExtraSelectionOverlays();
        if (taxi != null && State is FlightState.TaxiOut or FlightState.TaxiIn) AV.DrawPath(taxi.Skip(taxiIndex), DrawPos, SimpleColor.Cyan);
        if (State == FlightState.Parked && homePad?.Spawned == true) homePad.DrawConnection();
        if (route != null && route.Count > routeIndex)
        {
            Vector3 previous = DrawPos;
            for (int i = routeIndex; i < route.Count; i++) { GenDraw.DrawLineBetween(previous, route[i]); previous = route[i]; }
        }
        if (attack != AttackKind.None) DrawAttackPreview(attack, attackStart, attackEnd, plannedImpacts.Skip(shotIndex));
        if (loitering)
        {
            Vector3 previous = realPos;
            for (int i = 1; i <= 64; i++) { Vector3 next = loiterCenter + AV.Dir(loiterAngle + loiterDirection * i * 360f / 64) * LoiterRadius; AV.Line(previous, next, SimpleColor.White); previous = next; }
        }
    }
    public string FlightError(bool requireRunway = true)
    {
        if (!Spawned || State != FlightState.Parked) return "飞机必须停放后才能起飞";
        if (fuel < TakeoffFuelCost + 3) return $"至少需要{TakeoffFuelCost + 3:0}单位化合燃料";
        if (Cargo.AnythingLeftToLoad) return "货物尚未装载完成";
        if (PayloadError() is string payload) return payload;
        if (!Heavy && !AV.Clear(Position, Map, Clearance, this)) return "起飞净空不足";
        return null;
    }
    public bool BeginTakeoff()
    {
        string error = FlightError();
        if (error != null) { AV.Say(error); return false; }
        CancelAttack(); landingRequested = false; BoostEngines();
        loitering = false;
        if (!UsesRunwayTakeoff)
        {
            // VTOL owns only its pad and local clearance, never the runway queue.
            ReleaseReservations(); taxi = null; route.Clear(); routeIndex = 0;
            fuel -= TakeoffFuelCost; phaseTicks = 0; State = FlightState.TakingOff; Refuelable.SyncGroundAccess(); return true;
        }
        if (UsesRunwayTakeoff)
        {
            IEnumerable<RunwayZone> zones = Map.zoneManager.AllZones.OfType<RunwayZone>();
            zones = zones.OrderBy(z => z == homePad?.runway ? 0 : 1).ThenBy(z => z.Start.DistanceToSquared(Position));
            foreach (RunwayZone z in zones)
            {
                if (z.Error(this) != null) continue;
                List<IntVec3> path = AV.TaxiPath(Map, Position, z.Start, this);
                if (path == null) continue;
                activeRunway = z; z.reserved = this; taxi = path; taxiIndex = 0; State = FlightState.TaxiOut; Refuelable.SyncGroundAccess();
                return true;
            }
            AV.Say("没有可用的9×44格跑道，或通往跑道的11格宽净空滑行通道被阻挡。"); return false;
        }
        return false;
    }
    public void SetRoute(IEnumerable<Vector3> points)
    {
        exactMoveRoute = false; loitering = false; route = points.ToList(); routeIndex = 0; BoostEngines();
    }
    public void Loiter()
    {
        CancelAttack();
        loitering = false; route.Clear(); routeIndex = 0;
        if (!Heavy) return;
        foreach (int direction in new[] { 1, -1 })
        {
            Vector3 center = realPos + AV.Dir(yaw + direction * 90f) * LoiterRadius;
            if (center.x - LoiterRadius < 12 || center.z - LoiterRadius < 12 || center.x + LoiterRadius >= Map.Size.x - 12 || center.z + LoiterRadius >= Map.Size.z - 12) continue;
            loiterCenter = center; loiterAngle = (realPos - center).AngleFlat(); loiterDirection = direction; loitering = true; return;
        }
        // No tangent circle fits here: recover toward the interior before orbiting.
        SetRoute(new[] { AV.Flat(Map.Center) });
    }
    private void TickLoiter()
    {
        float angle = Speed / (60f * LoiterRadius) * Mathf.Rad2Deg;
        loiterAngle = Mathf.Repeat(loiterAngle + loiterDirection * angle, 360f);
        realPos = loiterCenter + AV.Dir(loiterAngle) * LoiterRadius;
        yaw = Mathf.Repeat(loiterAngle + loiterDirection * 90f, 360f);
        SetPositionSafely();
    }
    public void OrderMove(IntVec3 cell)
    {
        if (State != FlightState.Flying || !AV.SafeAir(cell, Map)) { AV.Say("请先起飞，目标需距地图边缘至少24格。"); return; }
        List<Vector3> path = Heavy ? AttackApproach.PlanMove(realPos, yaw, AV.Flat(cell), Map) : new() { AV.Flat(cell) };
        if (path == null) { AV.Say("无法在地图内完成转弯，请调整目标位置。"); return; }
        CancelAttack(); ReleaseReservations(); landingRequested = false; SetRoute(path); exactMoveRoute = Heavy;
    }
    public bool OrderAttack(AttackKind kind, IntVec3 start, IntVec3 end)
    {
        if (!Ordnance.IsBomb(kind) && kind is not (AttackKind.Rocket or AttackKind.Cannon)) return false;
        if (State != FlightState.Flying) { AV.Say("飞机需要处于飞行状态。"); return false; }
        if (Ammo(kind) <= 0) { AV.Say("没有此类挂载或弹药已用尽。"); return false; }
        Vector3 a = AV.Flat(start), b = AV.Flat(end), direction = (b - a).normalized;
        Vector3 exit = b + direction * 18;
        if (AV.Distance(a, b) < 1 || !new[] { a, b, exit }.All(p => AV.SafeAir(p.ToIntVec3(), Map, 25)))
        { AV.Say("请选择不同的起终点，并留出进场和脱离空间。"); return false; }
        // The marked line is the ground target strip. Forward weapons begin firing
        // before it, instead of shifting every impact beyond its first twelve cells.
        Vector3 runStart = Ordnance.IsBomb(kind) ? a : a - direction * ForwardLead;
        Vector3 runEnd = Ordnance.IsBomb(kind) ? b : b - direction * (Heavy ? 8 : 4);
        List<Vector3> approach = AttackApproach.CaptureForward(realPos, yaw, a, b, Map,
            Ordnance.IsBomb(kind) ? 8 : ForwardLead, Ordnance.IsBomb(kind) ? 0 : Heavy ? 1.5f : 3f,
            out Vector3 capture, out Vector3 offset);
        if (approach != null)
        {
            runStart = capture;
            runEnd = b + offset - direction * (Ordnance.IsBomb(kind) ? 0 : 2.5f);
            exit += offset;
        }
        else approach = Heavy ? AttackApproach.PlanEntry(realPos, yaw, runStart, direction.AngleFlat(), Map) : new List<Vector3> { runStart };
        if (approach == null || approach.Any(p => !AV.SafeAir(p.ToIntVec3(), Map, 12))) { AV.Say("无法在地图内完成转弯进场，请调整攻击起点或方向。"); return false; }
        ReleaseReservations(); CancelAttack(); landingRequested = false;
        attack = kind; attackStart = a; attackEnd = b; attackExit = exit; attackPhase = AttackPhase.Approach;
        attackRunStart = runStart; attackRunEnd = runEnd; attackGeometryVersion = 1;
        plannedImpacts = PlanImpacts(kind, a, b); shotsRemaining = plannedImpacts.Count; shotIndex = 0;
        SetRoute(approach); return true;
    }
    public void HoverAttack(AttackKind kind, Thing target)
        => OrderTargetAttack(kind, target, target?.Position ?? IntVec3.Invalid, true);
    public void RequestLanding()
    {
        if (State != FlightState.Flying) return;
        CancelAttack(); ReleaseReservations();
        IEnumerable<ParkingPad> pads = AV.Pads(Map).Where(p => p.Faction == Faction && p.Available(this));
        pads = pads.OrderBy(p => p == homePad ? 0 : p.bound == this ? 1 : 2).ThenBy(p => p.Position.DistanceToSquared(Position));
        foreach (ParkingPad pad in pads)
        {
            if (!AV.Clear(pad.Position, Map, Clearance, this)) continue;
            if (!Heavy)
            {
                landingPad = pad; pad.reserved = this; attack = AttackKind.None; landingRequested = true;
                SetRoute(new[] { AV.Flat(pad.Position) }); return;
            }
            var zones = Map.zoneManager.AllZones.OfType<RunwayZone>().OrderBy(z => z == pad.runway ? 0 : 1);
            foreach (RunwayZone z in zones)
            {
                if (z.Error(this) != null || AV.TaxiPath(Map, z.LandingEnd, pad.Position, this) == null) continue;
                Vector3 approach = AV.Flat(z.LandingStart) - (AV.Flat(z.LandingEnd) - AV.Flat(z.LandingStart)).normalized * 14;
                if (!AV.SafeAir(approach.ToIntVec3(), Map, 12)) continue;
                Vector3 threshold = AV.Flat(z.LandingStart); float heading = (AV.Flat(z.LandingEnd) - threshold).AngleFlat();
                List<Vector3> path = AttackApproach.PlanEntry(realPos, yaw, approach, heading, Map);
                if (path == null) continue;
                path.Add(threshold); // Finish with a straight, aligned fourteen-cell final.
                activeRunway = z; z.reserved = this; landingPad = pad; pad.reserved = this;
                landingRequested = true; attack = AttackKind.None; SetRoute(path); return;
            }
        }
        AV.Say("没有空闲停机坪，或跑道/滑行通道不通；飞机继续等待。");
    }
    protected override void Tick()
    {
        base.Tick();
        try { TickAircraft(); }
        finally
        {
            if (Spawned)
            {
                visualLift = Mathf.MoveTowards(Mathf.Max(0, visualLift), TargetVisualLift, 1f / 70);
                Refuelable.SyncGroundAccess();
                EmitEngineExhaust(); EmitWingTrails();
            }
        }
    }
    private void TickAircraft()
    {
        EnforceCrewLimit();
        if (!Spawned) return;
        if (boostTicks > 0) boostTicks--;
        if (State == FlightState.Parked)
        {
            if (homePad?.Spawned == true && Position == homePad.Position) yaw = Mathf.MoveTowardsAngle(yaw, homePad.Heading, 3);
            RecoverRetiredMounts(); DropPendingFuel(); return;
        }
        ageTicks++;
        if (State == FlightState.Crashing)
        {
            if (++phaseTicks >= 100) Crash();
            else { realPos += AV.Dir(yaw) * 0.12f; SetPositionSafely(); if (phaseTicks % 6 == 0) FleckMaker.ThrowSmoke(DrawPos, Map, 2f); }
            return;
        }
        if (Airborne && State != FlightState.TakingOff && ++phaseTicks % 60 == 0)
        {
            fuel = Mathf.Max(0, fuel - (Heavy ? 0.22f : 0.35f));
            if (fuel <= 0) { State = FlightState.Crashing; phaseTicks = 0; return; }
            if (HitPoints < MaxHitPoints * 0.3f && !landingRequested && !raid) RequestLanding();
        }
        if (TickFlightSafety()) return;
        if (State == FlightState.TaxiOut || State == FlightState.TaxiIn) { TickTaxi(); return; }
        if (State == FlightState.TakingOff)
        {
            phaseTicks++;
            if (activeRunway != null || Heavy || runwayTakeoff)
            {
                if (activeRunway == null || activeRunway.Error(this) != null) { AbortGround("已中止起飞：" + (activeRunway?.Error(this) ?? "跑道已移除")); return; }
                Vector3 end = AV.Flat(activeRunway.End); yaw = (end - realPos).AngleFlat();
                realPos = Vector3.MoveTowards(realPos, end, Mathf.Lerp(0.04f, Speed / 60, Mathf.Min(1, phaseTicks / 100f))); SetPositionSafely();
                if (AV.Distance(realPos, end) > 0.6f) return;
            }
            else if (phaseTicks < 100) return;
            State = FlightState.Flying; phaseTicks = 0; ReleaseRunway();
            if (homePad?.reserved == this) homePad.reserved = null;
            Loiter();
            if (worldDeparture) WorldFlight.Depart(this);
            return;
        }
        if (State == FlightState.Landing)
        {
            if (Heavy)
            {
                if (activeRunway == null || activeRunway.Error(this) != null) { GoAround(); return; }
                realPos = Vector3.MoveTowards(realPos, AV.Flat(activeRunway.LandingEnd), 0.18f); SetPositionSafely();
                if (AV.Distance(realPos, AV.Flat(activeRunway.LandingEnd)) > 0.05f) return;
            }
            else
            {
                if (landingPad?.Spawned == true) yaw = Mathf.MoveTowardsAngle(yaw, landingPad.Heading, 3);
                if (phaseTicks < 90) return;
                if (landingPad == null || !landingPad.Spawned || !landingPad.Available(this) || !AV.Clear(landingPad.Position, Map, Clearance, this)) { GoAround(); return; }
                realPos = AV.Flat(landingPad.Position); SetPositionSafely(); landingPad.Bind(this);
                State = FlightState.Parked; landingRequested = false; ReleaseReservations(); return;
            }
            if (landingPad == null || !landingPad.Spawned) { GoAround(); return; }
            taxi = AV.TaxiPath(Map, Position, landingPad.Position, this); taxiIndex = 0;
            if (taxi == null) { GoAround(); return; }
            State = FlightState.TaxiIn;
            return;
        }
        if (State != FlightState.Flying) return;
        if (raid && !raidExiting && (ageTicks >= raidDuration || HitPoints < MaxHitPoints * 0.35f || !mounts.Any(m => m.rounds > 0) && (Heavy || FixedCannon.rounds <= 0))) BeginRaidExit();
        if (raid && (raidEntering || raidExiting)) { TickRaidTransit(); return; }
        if (attackPhase != AttackPhase.None) { TickAttack(); SetPositionSafely(); return; }
        if (landingRequested && Heavy)
        {
            if (activeRunway == null || activeRunway.Map != Map || activeRunway.Error(this) != null || landingPad == null || !landingPad.Spawned || !landingPad.Available(this) || !AV.Clear(landingPad.Position, Map, Clearance, this)) { GoAround(); return; }
            if (FollowExactPath())
            {
                realPos = AV.Flat(activeRunway.LandingStart); yaw = (AV.Flat(activeRunway.LandingEnd) - realPos).AngleFlat();
                State = FlightState.Landing; phaseTicks = 0;
            }
            SetPositionSafely(); return;
        }
        if (loitering) { TickLoiter(); return; }
        if (Heavy && !exactMoveRoute && !landingRequested && routeIndex < route.Count)
        {
            // Upgrade saved point-chasing orders as well as post-takeoff moves.
            // This runs after attack, landing and raid transit have taken priority.
            List<Vector3> path = AttackApproach.PlanMove(realPos, yaw, route[^1], Map);
            if (path == null) { Loiter(); SetPositionSafely(); return; }
            SetRoute(path); exactMoveRoute = true;
        }
        if (exactMoveRoute)
        {
            if (FollowExactPath()) { exactMoveRoute = false; Loiter(); }
            SetPositionSafely(); return;
        }
        if (route.Count == 0 || routeIndex >= route.Count)
        {
            if (landingRequested)
            {
                if (landingPad == null || !landingPad.Available(this) || Heavy && activeRunway?.Error(this) != null) { GoAround(); return; }
                if (!Heavy)
                {
                    if (!landingPad.Spawned || !AV.Clear(landingPad.Position, Map, Clearance, this)) { GoAround(); return; }
                    realPos = Vector3.MoveTowards(realPos, AV.Flat(landingPad.Position), Speed / 60f); SetPositionSafely();
                    yaw = Mathf.MoveTowardsAngle(yaw, landingPad.Heading, 3);
                    if (AV.Distance(realPos, AV.Flat(landingPad.Position)) > 0.001f || Mathf.Abs(Mathf.DeltaAngle(yaw, landingPad.Heading)) > 0.05f) return;
                }
                State = FlightState.Landing; phaseTicks = 0;
                if (Heavy) yaw = (AV.Flat(activeRunway.LandingEnd) - AV.Flat(activeRunway.LandingStart)).AngleFlat();
                return;
            }
            if (raid) { AirRaids.RaidPass(this); }
            else if (Heavy) { Loiter(); return; }
        }
        if (routeIndex < route.Count)
        {
            Vector3 target = route[routeIndex]; float desired = (target - realPos).AngleFlat();
            yaw = Mathf.MoveTowardsAngle(yaw, desired, Heavy ? 2.4f : 5f);
            float step = Speed / 60f;
            if (!Heavy) realPos = Vector3.MoveTowards(realPos, target, step);
            else realPos += AV.Dir(yaw) * step;
            if (AV.Distance(realPos, target) < (Heavy ? 4f : landingRequested ? 0.001f : 0.4f)) routeIndex++;
        }
        // Air routes always stay inside a soft envelope. No normal task may despawn an aircraft.
        if (!AV.SafeAir(realPos.ToIntVec3(), Map, 12))
        {
            CancelAttack(); landingRequested = false; SetRoute(new[] { AV.Flat(Map.Center) });
            yaw = Mathf.MoveTowardsAngle(yaw, (AV.Flat(Map.Center) - realPos).AngleFlat(), 12f);
            realPos.x = Mathf.Clamp(realPos.x, 8, Map.Size.x - 9); realPos.z = Mathf.Clamp(realPos.z, 8, Map.Size.z - 9);
        }
        SetPositionSafely();
    }
    private void EnforceCrewLimit()
    {
        if (!Spawned || State != FlightState.Parked || Cargo?.innerContainer == null) return;
        List<Pawn> occupants = Cargo.innerContainer.OfType<Pawn>()
            .OrderByDescending(p => p.IsFreeColonist && !p.Downed)
            .ThenBy(p => p.thingIDNumber).ToList();
        if (occupants.Count <= Seats) { crewLimitWarned = false; return; }
        foreach (Pawn extra in occupants.Skip(Seats).ToList())
        {
            // A failed drop must retain ownership; never abandon a passenger.
            Cargo.innerContainer.TryDrop(extra, Position, Map, ThingPlaceMode.Near, out _);
        }
        if (!crewLimitWarned)
        {
            crewLimitWarned = true;
            AV.Say($"{LabelCap}的乘员席位已满（{Seats}席），正在将超出的乘员卸载到飞机旁。");
        }
    }
    private void TickTaxi()
    {
        if (taxi == null)
        {
            if (Find.TickManager.TicksGame < nextTaxiRetry) return;
            nextTaxiRetry = Find.TickManager.TicksGame + 180;
            IntVec3 goal = State == FlightState.TaxiOut ? activeRunway?.Start ?? IntVec3.Invalid : landingPad?.Position ?? IntVec3.Invalid;
            if (!goal.IsValid) { AbortGround("滑行目的地已移除。"); return; }
            taxi = AV.TaxiPath(Map, Position, goal, this); taxiIndex = 0;
            if (taxi == null) return;
        }
        if (taxiIndex >= taxi.Count)
        {
            float heading = State == FlightState.TaxiOut && activeRunway != null ? (AV.Flat(activeRunway.End) - AV.Flat(activeRunway.Start)).AngleFlat() : landingPad?.Heading ?? yaw;
            yaw = Mathf.MoveTowardsAngle(yaw, heading, 3.5f);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, heading)) > 0.05f) return;
            if (State == FlightState.TaxiOut)
            {
                if (activeRunway == null) { AbortGround("跑道已移除。"); return; }
                if (activeRunway.Error(this) != null)
                {
                    taxi = null; nextTaxiRetry = Find.TickManager.TicksGame + 180; return;
                }
                if (fuel < TakeoffFuelCost + 3) { AbortGround("燃料不足，已中止起飞。"); return; }
                State = FlightState.TakingOff; fuel -= TakeoffFuelCost; phaseTicks = 0; BoostEngines();
            }
            else { State = FlightState.Parked; landingRequested = false; if (landingPad != null) landingPad.Bind(this); ReleaseReservations(); }
            return;
        }
        IntVec3 next = taxi[taxiIndex];
        if (!AV.Clear(next, Map, Clearance, this))
        {
            if (Find.TickManager.TicksGame >= nextTaxiRetry)
            {
                nextTaxiRetry = Find.TickManager.TicksGame + 180;
                taxi = AV.TaxiPath(Map, Position, State == FlightState.TaxiOut ? activeRunway.Start : landingPad.Position, this); taxiIndex = 0;
            }
            return;
        }
        Vector3 target = AV.Flat(next);
        if (AV.Distance(realPos, target) > 0.02f) yaw = Mathf.MoveTowardsAngle(yaw, (target - realPos).AngleFlat(), 3.5f);
        realPos = Vector3.MoveTowards(realPos, target, 0.14f); SetPositionSafely();
        if (AV.Distance(realPos, target) < 0.02f) { realPos = target; SetPositionSafely(); taxiIndex++; }
    }
    private void SetPositionSafely() { IntVec3 c = realPos.ToIntVec3(); if (c.InBounds(Map)) Position = c; }
    private void AbortGround(string reason) { State = FlightState.Parked; worldDeparture = false; ReleaseReservations(); AV.Say(reason); }
    private void GoAround() { State = FlightState.Flying; landingRequested = false; ReleaseReservations(); Loiter(); AV.Say("降落条件变化，飞机复飞。"); }
    private void ReleaseRunway() { if (activeRunway?.reserved == this) activeRunway.reserved = null; activeRunway = null; }
    public void ReleaseReservations() { ReleaseRunway(); if (landingPad?.reserved == this) landingPad.reserved = null; landingPad = null; }

    public int Ammo(AttackKind kind) => mounts.Where(m => Ordnance.Matches(m.kind, kind)).Sum(m => m.rounds) + (!Heavy && kind == AttackKind.Cannon ? FixedCannon.rounds : 0);
    public int CommandShots(AttackKind kind) => kind == AttackKind.Rocket ? rocketSalvo : Ordnance.IsBomb(kind) ? 4 : kind == AttackKind.Cannon ? 40 : 1;
    public List<Vector3> PlanImpacts(AttackKind kind, Vector3 a, Vector3 b)
    {
        int limit = Ordnance.IsBomb(kind) ? Mathf.Clamp(Mathf.FloorToInt(AV.Distance(a, b) / 3f), 1, 4) : CommandShots(kind);
        int count = Math.Min(Ammo(kind), limit);
        return Enumerable.Range(0, count).Select(i => Vector3.Lerp(a, b, Ordnance.IsBomb(kind) ? (i + 0.5f) / count : count > 1 ? (float)i / (count - 1) : 0f)).ToList();
    }
    public static void DrawAttackPreview(AttackKind kind, Vector3 a, Vector3 b, IEnumerable<Vector3> impacts)
    {
        AV.Line(a, b, SimpleColor.Red);
        foreach (Vector3 p in impacts)
        {
            IntVec3 cell = p.ToIntVec3();
            GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Color.yellow);
            float radius = Ordnance.PreviewRadius(kind);
            if (radius > 0) GenDraw.DrawRadiusRing(cell, radius);
        }
    }
    public void CancelAttack()
    {
        attack = AttackKind.None; attackPhase = AttackPhase.None; attacking = false; shotsRemaining = 0; shotIndex = 0; plannedImpacts.Clear(); lockedTarget = null; requiresTarget = false; targetAttackTicks = 0;
    }
    private bool FollowExactPath()
    {
        float remaining = Speed / 60f;
        while (routeIndex < route.Count && remaining > 0.00001f)
        {
            Vector3 target = route[routeIndex]; float distance = AV.Distance(realPos, target);
            if (distance > 0.0001f) yaw = (target - realPos).AngleFlat();
            float move = Math.Min(distance, remaining);
            realPos = Vector3.MoveTowards(realPos, target, move); remaining -= move;
            if (distance <= move + 0.0001f) routeIndex++; else break;
        }
        return routeIndex >= route.Count;
    }
    private void TickAttack()
    {
        if (attackPhase == AttackPhase.Approach)
        {
            bool arrived;
            if (Heavy) arrived = FollowExactPath();
            else
            {
                float entryYaw = yaw;
                // VTOL translates like a move order, with a bounded turn rate.
                // Align at the final point before beginning the firing run.
                if (routeIndex < route.Count)
                {
                    Vector3 point = route[routeIndex];
                    if (AV.Distance(realPos, point) > 0.001f) yaw = Mathf.MoveTowardsAngle(yaw, (point - realPos).AngleFlat(), 5);
                    realPos = Vector3.MoveTowards(realPos, point, Speed / 60f);
                    if (AV.Distance(realPos, point) < 0.001f) routeIndex++;
                }
                arrived = routeIndex >= route.Count;
                if (arrived)
                {
                    yaw = Mathf.MoveTowardsAngle(yaw, (attackEnd - attackStart).AngleFlat(), 5);
                }
                yaw = Mathf.MoveTowardsAngle(entryYaw, yaw, 5);
                if (arrived) arrived = Mathf.Abs(Mathf.DeltaAngle(yaw, (attackEnd - attackStart).AngleFlat())) < 0.05f;
            }
            if (arrived)
            {
                realPos = attackRunStart; yaw = (attackEnd - attackStart).AngleFlat();
                attackPhase = AttackPhase.Run; attacking = true; nextShot = Find.TickManager.TicksGame;
            }
            return;
        }
        if (attackPhase == AttackPhase.Exit)
        {
            realPos = Vector3.MoveTowards(realPos, attackExit, Speed / 60f);
            if (AV.Distance(realPos, attackExit) < 0.01f)
            {
                CancelAttack(); route.Clear(); routeIndex = 0;
                if (raid) { if (Heavy) BeginRaidExit(); else AirRaids.RaidPass(this); } else Loiter();
            }
            return;
        }
        if (attackPhase is AttackPhase.Hover or AttackPhase.Target) { TickTargetAttack(); return; }
        if (attackPhase != AttackPhase.Run) return;
        yaw = (attackEnd - attackStart).AngleFlat();
        int attempts = attack == AttackKind.Rocket ? 12 : 1;
        if (attack == AttackKind.Cannon)
        {
            // A nose-close capture has fewer firing ticks than a full approach.
            // Compress the finite burst into that window without extending it
            // into the orbit or trying to chase already-passed target cells.
            int firingTicks = Math.Max(1, Mathf.FloorToInt(AV.Distance(realPos, attackRunEnd) / (Speed / 60f)));
            attempts = Mathf.CeilToInt(shotsRemaining / (float)firingTicks);
        }
        bool ready = attack == AttackKind.Rocket || Find.TickManager.TicksGame >= nextShot;
        while (ready && attempts-- > 0 && shotsRemaining > 0 && shotIndex < plannedImpacts.Count)
        {
            float progress = Vector3.Dot(realPos - attackStart, AV.Dir(yaw));
            float release = Vector3.Dot(plannedImpacts[shotIndex] - attackStart, AV.Dir(yaw)) - (Ordnance.IsBomb(attack) ? 8f : ForwardLead);
            if (progress < (Ordnance.IsBomb(attack) ? Mathf.Max(0, release) : release)) break;
            int previous = shotIndex; FireMounted();
            if (previous == shotIndex) break;
        }
        realPos = Vector3.MoveTowards(realPos, attackRunEnd, Speed / 60f);
        if (AV.Distance(realPos, attackRunEnd) < 0.01f)
        {
            // Latch the exit before any generic route/loiter handling can run again.
            attackPhase = AttackPhase.Exit; attack = AttackKind.None; attacking = false; shotsRemaining = 0;
            route.Clear(); routeIndex = 0;
        }
    }
    public void FireMounted()
    {
        if (!attacking || shotsRemaining <= 0 || shotIndex >= plannedImpacts.Count || attackPhase is not (AttackPhase.Run or AttackPhase.Hover or AttackPhase.Target)) return;
        if (requiresTarget && !ValidGroundTarget(lockedTarget)) { FinishTargetAttack(); return; }
        Vector3 direction = attackPhase is AttackPhase.Hover or AttackPhase.Target ? attackEnd - realPos : attackEnd - attackStart;
        if (!Ordnance.IsBomb(attack) && Mathf.Abs(Mathf.DeltaAngle(yaw, direction.AngleFlat())) > 12) return;
        var eligible = Enumerable.Range(0, mounts.Count).Where(i => Ordnance.Matches(mounts[i].kind, attack) && mounts[i].rounds > 0 && CanMount(i, mounts[i].kind)).ToList();
        if (!Heavy && attack == AttackKind.Cannon && FixedCannon.rounds > 0) eligible.Insert(0, -1);
        if (eligible.Count == 0) { shotsRemaining = 0; return; }
        int index = eligible[shotIndex % eligible.Count]; Hardpoint mount = index < 0 ? FixedCannon : mounts[index];
        Vector3 origin = index < 0 ? FixedCannonPosition : MountPosition(index); IntVec3 dest = plannedImpacts[shotIndex].ToIntVec3();
        if (!dest.InBounds(Map)) { shotsRemaining = 0; return; }
        if (!Ordnance.IsBomb(attack) && Vector3.Dot(AV.Flat(dest) - origin, AV.Dir(yaw)) < 1f) return;
        Thing corrected = requiresTarget ? lockedTarget : FindCorrectedTarget(attack, origin, dest);
        LocalTargetInfo target = corrected != null ? new LocalTargetInfo(corrected) : new LocalTargetInfo(dest);
        Projectile shot = (Projectile)ThingMaker.MakeThing(Ordnance.ProjectileDef(attack));
        GenSpawn.Spawn(shot, origin.ToIntVec3(), Map);
        shot.Launch(this, origin, target, target, attack == AttackKind.Cannon ? ProjectileHitFlags.All : ProjectileHitFlags.IntendedTarget, preventFriendlyFire: true);
        mount.rounds--; shotsRemaining--; shotIndex++;
        int interval = Ordnance.IsBomb(attack) ? 28 : attack == AttackKind.Cannon ? 4 : 8;
        if (attackPhase == AttackPhase.Run) interval = Math.Min(interval, Math.Max(1, (int)(AV.Distance(attackRunStart, attackRunEnd) / (Speed / 60f * Math.Max(1, plannedImpacts.Count) * 2))));
        nextShot = Find.TickManager.TicksGame + interval;
        if (!Ordnance.IsBomb(attack)) FleckMaker.Static(origin, Map, FleckDefOf.ShotFlash, attack == AttackKind.Cannon ? 0.8f : 2f);
        Map.GetComponent<AirportMap>().MarkImpact(shot, target.Cell, attack);
    }
    public void BeginRaidExit()
    {
        if (!raid || raidExiting) return;
        CancelAttack(); ReleaseReservations(); raidEntering = false; raidExiting = true; route.Clear(); routeIndex = 0;
        Vector3 d = AV.Dir(yaw); float distance = 10000;
        if (d.x > 0.001f) distance = Math.Min(distance, (Map.Size.x - 5.5f - realPos.x) / d.x);
        if (d.x < -0.001f) distance = Math.Min(distance, (5.5f - realPos.x) / d.x);
        if (d.z > 0.001f) distance = Math.Min(distance, (Map.Size.z - 5.5f - realPos.z) / d.z);
        if (d.z < -0.001f) distance = Math.Min(distance, (5.5f - realPos.z) / d.z);
        attackExit = realPos + d * Mathf.Max(0, distance);
    }
    private void TickRaidTransit()
    {
        Vector3 target = raidEntering ? route[0] : attackExit;
        yaw = (target - realPos).AngleFlat(); realPos = Vector3.MoveTowards(realPos, target, Speed / 60f); SetPositionSafely();
        if (AV.Distance(realPos, target) > 0.05f) return;
        if (raidExiting) { Destroy(DestroyMode.Vanish); return; }
        raidEntering = false; route.Clear(); routeIndex = 0; AirRaids.RaidPass(this);
    }
    public void Unload()
    {
        if (State != FlightState.Parked) return;
        Cargo.CancelLoad(); Cargo.innerContainer.TryDropAll(Position + IntVec3.East * (Clearance + 2), Map, ThingPlaceMode.Near);
    }
    public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
    {
        // Melee verbs tag their tool/body part/weapon; extra melee damage retains
        // the weapon even when it has no Tool. Leave projectiles and AA intact.
        if (dinfo.Tool != null || dinfo.WeaponBodyPartGroup != null || dinfo.WeaponLinkedHediff != null
            || dinfo.Weapon?.IsMeleeWeapon == true && !dinfo.Def.isRanged
            || dinfo.Instigator is Pawn && !dinfo.Def.isRanged && !dinfo.Def.isExplosive && dinfo.Def.ExternalViolenceFor(this)
                && (dinfo.Weapon?.race != null || dinfo.Def == DamageDefOf.Blunt || dinfo.Def.armorCategory == DamageArmorCategoryDefOf.Sharp))
        { absorbed = true; return; }
        // The ground explosion grid has no altitude. Ground bombs must not hit an
        // aircraft passing overhead; AA uses its explicit airframe damage bridge.
        if (Airborne && (dinfo.Def == DamageDefOf.Bomb || dinfo.Def.defName == "BombSuper" || dinfo.Def == DamageDefOf.Flame)) { absorbed = true; return; }
        base.PreApplyDamage(ref dinfo, out absorbed);
        if (!absorbed && Airborne && State != FlightState.Crashing && HitPoints - dinfo.Amount < MaxHitPoints * 0.12f)
        {
            HitPoints = Mathf.Max(1, Mathf.RoundToInt(MaxHitPoints * 0.12f)); absorbed = true; State = FlightState.Crashing; phaseTicks = 0;
        }
    }
    public void Crash()
    {
        if (!Spawned) return;
        Map map = Map; IntVec3 pos = Position;
        foreach (Pawn p in Cargo.innerContainer.OfType<Pawn>().ToList()) p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 25));
        Cargo.innerContainer.TryDropAll(pos, map, ThingPlaceMode.Near);
        ReleaseReservations(); Destroy(DestroyMode.KillFinalize);
        GenExplosion.DoExplosion(pos, map, 4.5f, DamageDefOf.Bomb, null, 65, 0.3f);
    }
    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        if (Spawned && !transferInProgress) { ReleaseReservations(); Cargo?.innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near); }
        base.Destroy(mode);
    }
    public override string GetInspectString() => AV.Inspect(base.GetInspectString(), $"{StateName(State)}  ·  乘员 {Cargo.innerContainer.OfType<Pawn>().Count()}/{Seats}\n货舱 {Cargo.MassUsage:F0}/{CargoCapacity:F0} kg  ·  挂载 {MountWeight:F0} kg\n飞行速度 {Speed:F1} 格/秒  ·  载荷效率 {FlightPerformance:P0}\n航程 {Range}  ·  备弹 {mounts.Sum(m => m.rounds) + (!Heavy ? FixedCannon.rounds : 0)}\n停机坪：{homePad?.LabelCap ?? "未绑定"}");
    public static string StateName(FlightState s) => s switch { FlightState.Parked => "停放", FlightState.TaxiOut => "滑向跑道", FlightState.TakingOff => "起飞", FlightState.Flying => "飞行", FlightState.Landing => "降落", FlightState.TaxiIn => "滑向停机坪", _ => "坠落" };
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo g in base.GetGizmos()) yield return g;
        if (Faction != Faction.OfPlayer) yield break;
        yield return AV.Button("重命名", "修改飞机名称。", () => Find.WindowStack.Add(new Dialog_RenameAircraft(this)), "UI/Buttons/Rename");
        yield return AV.Button("挂载配置", "更换挂载、补充弹药。", () => Find.WindowStack.Add(new LoadoutWindow(this)), AviationArt.Command("CustomPayload"));
        if (HasWeapon(AttackKind.Rocket)) yield return WeaponCommand(AttackKind.Rocket, $"齐射 {rocketSalvo}", "切换火箭齐射数量：2、4、6、8、12枚。", () => { int[] counts = { 2, 4, 6, 8, 12 }; rocketSalvo = counts[(Array.IndexOf(counts, rocketSalvo) + 1) % counts.Length]; }, AviationArt.Command("RocketSalvoCount"));
        yield return AV.Button("抵达：" + (arriveAirborne ? "保持飞行" : "降落"), "设置世界航行抵达地图后的状态。", () => arriveAirborne = !arriveAirborne, AviationArt.ArrivalMode);
        yield return AV.Button("低油量保护：" + (autoReturn ? "开" : "关"), "燃料偏低时优先就近降落；无机场时返回出发地。", () => autoReturn = !autoReturn, AviationArt.Chemfuel);
        if (State is FlightState.Parked or FlightState.Flying)
            yield return AV.Button("世界航行", "选择世界地图目的地。", () => WorldFlight.Choose(this), AviationArt.Command("TakeoffWorld"));
        if (State == FlightState.Parked)
        {
            if (!Heavy) yield return AV.Button("起飞：" + (runwayTakeoff ? "跑道" : "垂直"), "垂直起飞：耗油18，载荷1100 kg；跑道起飞：耗油5，载荷1800 kg。", () => runwayTakeoff = !runwayTakeoff, AviationArt.TakeoffMode(!runwayTakeoff));
            yield return AV.Button("货舱加油", "将货舱中的化合燃料加入油箱。", () => RefuelFromCargo(), "Things/Item/Resource/Chemfuel");
            yield return AV.Button("起飞", UsesRunwayTakeoff ? "检查跑道与通道后自动滑行、起飞。" : "检查净空并垂直起飞，固定消耗18燃料。", () => BeginTakeoff(), AviationArt.Command("Takeoff"));
            yield return AV.Button("卸载全部", "将人员和货物卸到飞机旁。", Unload, "UI/Designators/Cancel");
        }
        if (State == FlightState.Flying)
        {
            yield return AV.Button("移动", "Rukaya_AircraftMoveDescription".Translate(), () => AV.Target(OrderMove), AviationArt.Move);
            yield return AV.Button("降落", "选择可用停机坪并降落。", RequestLanding, AviationArt.Command("Landing"));
            if (returnBaseTile.Valid && returnBaseTile != Map.Tile) yield return AV.Button("返回出发地", "飞回出发机场并降落。", () => ReturnToBase(), AviationArt.ReturnHome);
            foreach (AttackKind k in new[] { AttackKind.Bomb, AttackKind.IncendiaryBomb, AttackKind.ClusterBomb, AttackKind.Rocket, AttackKind.Cannon })
            {
                AttackKind kind = k;
                if (!HasWeapon(kind)) continue;
                yield return WeaponCommand(kind, Ordnance.RouteLabel(kind), "依次选择航线起点、终点。" + (Ordnance.IsBomb(kind) ? "短航线减少投弹；集束弹圆圈表示散布范围。" : "向航线附近的目标射击。"), () => AV.Target(a =>
                {
                    // Schedule the second target after Targeter finishes the first callback.
                    Map.GetComponent<AirportMap>().pending = () => Find.Targeter.BeginTargeting(AV.LocationTargets,
                        t => OrderAttack(kind, a, t.Cell),
                        t => DrawAttackPreview(kind, AV.Flat(a), AV.Flat(t.Cell), PlanImpacts(kind, AV.Flat(a), AV.Flat(t.Cell))),
                        t => t.Cell.InBounds(Map));
                }));
            }
            if (HasWeapon(AttackKind.GroundMissile)) yield return WeaponCommand(AttackKind.GroundMissile, "空对地导弹", "指定单位或建筑，发射一枚制导导弹。直击伤害高、穿甲强。", () => TargetWeapon(AttackKind.GroundMissile));
            if (Heavy && HasWeapon(AttackKind.ActiveMatterMissile)) yield return WeaponCommand(AttackKind.ActiveMatterMissile, "活性物质导弹打击", "指定地面点，发射一枚大范围毁伤导弹。", () => TargetWeapon(AttackKind.ActiveMatterMissile));
            if (!Heavy)
            {
                if (HasWeapon(AttackKind.Rocket)) yield return WeaponCommand(AttackKind.Rocket, "悬停火箭", "指定单位或建筑。按齐射数量发射，目标失效后停火。", () => TargetHover(AttackKind.Rocket));
                if (HasWeapon(AttackKind.Cannon)) yield return WeaponCommand(AttackKind.Cannon, "悬停机炮", "指定单位或建筑。最多射击40发，目标失效后停火。", () => TargetHover(AttackKind.Cannon));
            }
            yield return AV.Button("停止攻击", "停止射击并悬停或盘旋。", () => { CancelAttack(); landingRequested = false; ReleaseReservations(); Loiter(); }, AviationArt.Command("StopTaxing"));
        }
        if (State is FlightState.TaxiOut or FlightState.TaxiIn)
            yield return AV.Button("中止滑行", "停在当前位置并释放跑道预留。", () => AbortGround("滑行已中止。"), AviationArt.Command("StopTaxing"));
    }
    private void TargetHover(AttackKind kind) => TargetWeapon(kind, true);
    public override void ExposeData()
    {
        base.ExposeData(); ExposeOperations(); Scribe_Values.Look(ref runwayTakeoff, "ravRunwayTakeoff"); Scribe_Deep.Look(ref fixedCannon, "ravFixedCannon"); Scribe_Values.Look(ref State, "state"); Scribe_Values.Look(ref realPos, "realPos"); Scribe_Values.Look(ref yaw, "yaw");
        Scribe_Values.Look(ref airPressure, "ravAirPressure"); Scribe_Values.Look(ref lastPressureTick, "ravLastPressureTick");
        Scribe_Collections.Look(ref mounts, "mounts", LookMode.Deep);
        Scribe_Values.Look(ref hardpointLayoutVersion, "ravHardpointLayoutVersion");
        Scribe_Collections.Look(ref retiredMounts, "ravRetiredMounts", LookMode.Deep);
        Scribe_Collections.Look(ref pendingAmmoRefunds, "ravAmmoRefunds", LookMode.Deep);
        Scribe_Values.Look(ref customName, "ravCustomName"); Scribe_Values.Look(ref aircraftNumber, "ravAircraftNumber");
        Scribe_Values.Look(ref pendingFuelUnload, "ravPendingFuelUnload"); Scribe_Values.Look(ref visualLift, "ravVisualLift", -1);
        Scribe_References.Look(ref homePad, "homePad"); Scribe_References.Look(ref landingPad, "landingPad"); Scribe_References.Look(ref activeRunway, "activeRunway");
        Scribe_Collections.Look(ref taxi, "taxi", LookMode.Value); Scribe_Values.Look(ref taxiIndex, "taxiIndex");
        Scribe_Collections.Look(ref route, "route", LookMode.Value); Scribe_Values.Look(ref routeIndex, "routeIndex");
        Scribe_Values.Look(ref exactMoveRoute, "ravExactMoveRoute");
        Scribe_Values.Look(ref attack, "attack"); Scribe_Values.Look(ref attackStart, "attackStart"); Scribe_Values.Look(ref attackEnd, "attackEnd");
        Scribe_References.Look(ref lockedTarget, "ravLockedTarget"); Scribe_Values.Look(ref requiresTarget, "ravRequiresTarget"); Scribe_Values.Look(ref targetAttackTicks, "ravTargetAttackTicks");
        Scribe_Values.Look(ref attacking, "attacking"); Scribe_Values.Look(ref landingRequested, "landingRequested");
        Scribe_Values.Look(ref attackPhase, "attackPhase"); Scribe_Values.Look(ref shotsRemaining, "shotsRemaining"); Scribe_Values.Look(ref shotIndex, "shotIndex");
        Scribe_Values.Look(ref rocketSalvo, "rocketSalvo", 6); Scribe_Collections.Look(ref plannedImpacts, "plannedImpacts", LookMode.Value); Scribe_Values.Look(ref attackExit, "attackExit");
        Scribe_Values.Look(ref attackRunStart, "attackRunStart"); Scribe_Values.Look(ref attackRunEnd, "attackRunEnd"); Scribe_Values.Look(ref attackGeometryVersion, "attackGeometryVersion");
        Scribe_Values.Look(ref loitering, "loitering"); Scribe_Values.Look(ref loiterCenter, "loiterCenter"); Scribe_Values.Look(ref loiterAngle, "loiterAngle"); Scribe_Values.Look(ref loiterDirection, "loiterDirection", 1);
        Scribe_Values.Look(ref raidEntering, "raidEntering"); Scribe_Values.Look(ref raidExiting, "raidExiting"); Scribe_Values.Look(ref raidDuration, "raidDuration", 3600);
        Scribe_Values.Look(ref raid, "raid"); Scribe_Values.Look(ref ageTicks, "ageTicks"); Scribe_Values.Look(ref passes, "passes");
        Scribe_Values.Look(ref phaseTicks, "phaseTicks"); Scribe_Values.Look(ref nextShot, "nextShot");
        Scribe_Values.Look(ref worldDestination, "worldDestination"); Scribe_Values.Look(ref worldDeparture, "worldDeparture");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (!returnBaseTile.Valid && homePad?.Spawned == true) returnBaseTile = homePad.Map.Tile;
            mounts ??= new(); route ??= new(); plannedImpacts ??= new(); NormalizeHardpoints(); RefreshCapacity();
            aircraftNumber = Current.Game.GetComponent<AircraftNames>().Register(this, aircraftNumber);
            if (attackPhase == AttackPhase.None) { attack = AttackKind.None; attacking = false; }
            else if (attackPhase == AttackPhase.Hover && (!requiresTarget || lockedTarget == null)) CancelAttack();
            else if (attackPhase is not (AttackPhase.Hover or AttackPhase.Target) && attackGeometryVersion < 1) CancelAttack();
            if (!Heavy)
            {
                // Legacy saves may retain a runway pointer on a VTOL. Release only
                // its own reservation; a fixed-wing aircraft's reservation is untouched.
                if (!UsesRunwayTakeoff) ReleaseRunway();
                loitering = false;
            }
        }
    }
}
