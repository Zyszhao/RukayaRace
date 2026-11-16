using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public partial class Aircraft
{
    public PlanetTile returnBaseTile = PlanetTile.Invalid;
    public bool arriveAirborne;
    public bool autoReturn = true;
    public int boostTicks;
    private int safetyRetryAt;
    private bool trailPositionKnown;
    private Vector3 lastTrailPosition;
    private float lastTrailYaw, lastTrailLift;
    private readonly List<(Vector3 a, Vector3 b, int tick)> wingTrailSegments = new();
    public int WingTrailSegments => wingTrailSegments.Count;
    public void ClearFlightTrails() { trailPositionKnown = false; wingTrailSegments.Clear(); }
    public float ForwardLead => Heavy ? 18 : 8;
    public Vector3 FixedCannonPosition => realPos + AV.Dir(yaw) * 1.4f;
    public float FlightPerformance => 1f / (1f + mounts.Count(m => m.kind is StoreKind.CargoPod or StoreKind.PassengerPod) * 0.06f
        + mounts.Count(m => m.kind == StoreKind.Tank) * 0.03f + Mathf.Clamp01(((Cargo?.MassUsage ?? 0) + MountWeight) / TakeoffLimit) * 0.15f);
    public int WorldTravelTicks(int distance) => Math.Max(60, Mathf.CeilToInt((distance + 3) * (Heavy ? 24 : 60) / FlightPerformance));
    public float LandingReserve => Heavy ? 18 : 12;
    public float ReturnFuelRequired
    {
        get
        {
            if (!Spawned || !returnBaseTile.Valid || returnBaseTile == Map.Tile) return LandingReserve;
            int distance = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(Map.Tile, returnBaseTile));
            return WorldFlight.FuelCost(this, distance) + LandingReserve;
        }
    }
    public void BoostEngines() => boostTicks = Math.Max(boostTicks, 180);
    public bool EnginesBoosting => State == FlightState.TakingOff || State == FlightState.Flying && !landingRequested
        && (boostTicks > 0 || !loitering && routeIndex < route.Count || attackPhase != AttackPhase.None);
    // Coordinates measured on the 1024px north-facing source textures, not the tail fins.
    public Vector3 AirframePoint(float u, float v) => realPos + Quaternion.AngleAxis(yaw, Vector3.up)
        * new Vector3((u - 0.5f) * def.graphicData.drawSize.x, 0, (0.5f - v) * def.graphicData.drawSize.y);
    public Vector3 ExhaustPosition(float side = 0) => Heavy ? AirframePoint(side < 0 ? 474f / 1024 : 550f / 1024, 989f / 1024) : AirframePoint(0.5f, 799f / 1024);
    private void EmitEngineExhaust()
    {
        if (!EnginesBoosting) return;
        float pulse = 1 + 0.08f * Mathf.Sin(Find.TickManager.TicksGame * 1.9f);
        foreach (float side in Heavy ? new[] { -1f, 1f } : new[] { 0f })
        {
            Vector3 nozzle = ExhaustPosition(side); nozzle.z += Mathf.Max(0, visualLift) * 1.5f;
            for (int i = 5; i >= 0; i--)
            {
                Vector3 pos = nozzle - AV.Dir(yaw) * (0.22f + i * 0.62f * pulse);
                if (!pos.InBounds(Map)) continue;
                FleckCreationData data = FleckMaker.GetDataStatic(pos, Map, DefDatabase<FleckDef>.GetNamed("RAV_EngineJet"));
                data.rotation = yaw;
                data.exactScale = new Vector3((1.15f - i * 0.14f) * pulse, 1, 2.1f * pulse);
                data.instanceColor = i < 2 ? new Color(0.55f, 0.78f, 1, 0.95f) : new Color(1, 0.43f + i * 0.045f, 0.1f, 0.85f - i * 0.1f);
                Map.flecks.CreateFleck(data);
            }
        }
    }
    private void EmitWingTrails()
    {
        wingTrailSegments.RemoveAll(s => Find.TickManager.TicksGame - s.tick >= 60);
        if (!Airborne || State == FlightState.Crashing) { trailPositionKnown = false; return; }
        Vector3 position = realPos;
        float distance = AV.Distance(position, lastTrailPosition);
        if (trailPositionKnown && distance > 0.01f && distance < 2)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float u = Heavy ? (side < 0 ? 206f : 816f) / 1024 : (side < 0 ? 229f : 792f) / 1024;
                float v = Heavy ? 858f / 1024 : 710f / 1024;
                Vector3 end = AirframePoint(u, v); end.z += Mathf.Max(0, visualLift) * 1.5f;
                Vector3 local = new((u - 0.5f) * def.graphicData.drawSize.x, 0, (0.5f - v) * def.graphicData.drawSize.y);
                Vector3 start = lastTrailPosition + Quaternion.AngleAxis(lastTrailYaw, Vector3.up) * local;
                start.z += Mathf.Max(0, lastTrailLift) * 1.5f;
                wingTrailSegments.Add((start, end, Find.TickManager.TicksGame));
            }
        }
        lastTrailPosition = position; lastTrailYaw = yaw; lastTrailLift = visualLift; trailPositionKnown = true;
    }
    private void DrawWingTrails()
    {
        foreach (var segment in wingTrailSegments)
        {
            float fade = Mathf.Clamp01(1 - (Find.TickManager.TicksGame - segment.tick) / 60f);
            if (fade <= 0) continue;
            // Quantize alpha to reuse cached transparent line materials.
            Color color = new(0.88f, 0.94f, 1, Mathf.Ceil(fade * 10) / 10 * 0.65f);
            Vector3 a = segment.a, b = segment.b;
            a.y = b.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.03f;
            GenDraw.DrawLineBetween(a, b, SolidColorMaterials.SimpleSolidColorMaterial(color, true), Mathf.Lerp(0.1f, 0.045f, fade));
        }
    }
    public static void EmitTrail(Map map, string defName, Vector3 start, Vector3 end, float width, Color color)
    {
        Vector3 mid = (start + end) * 0.5f;
        if (!mid.InBounds(map)) return;
        FleckCreationData data = FleckMaker.GetDataStatic(mid, map, DefDatabase<FleckDef>.GetNamed(defName));
        data.rotation = (end - start).AngleFlat();
        data.exactScale = new Vector3(width, 1, Mathf.Max(0.15f, AV.Distance(start, end)) + width * 2);
        data.instanceColor = color; map.flecks.CreateFleck(data);
    }
    public int RefuelFromCargo()
    {
        if (Spawned && State != FlightState.Parked) return 0;
        int used = 0;
        foreach (Thing stack in Cargo.innerContainer.Where(t => t.def == ThingDefOf.Chemfuel).ToList())
        {
            int count = Math.Min(Mathf.CeilToInt(MaxFuel - fuel), stack.stackCount);
            if (count <= 0) break;
            stack.SplitOff(count).Destroy(); fuel += count; used += count;
        }
        return used;
    }
    public bool ReturnToBase()
    {
        if (State != FlightState.Flying || !Spawned) return false;
        Map source = returnBaseTile.Valid ? Current.Game.FindMap(returnBaseTile) : homePad?.Map;
        if (source == null || source == Map) { RequestLanding(); return landingRequested; }
        int distance = Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(Map.Tile, source.Tile));
        if (distance > Range || fuel < WorldFlight.FuelCost(this, distance) + 2) { AV.Say("返航燃料不足。"); return false; }
        CancelAttack(); landingRequested = false; ReleaseReservations(); BoostEngines();
        arriveAirborne = false;
        worldDestination = source.Tile; worldDeparture = true;
        // Already airborne: a departure runway at the visited map is unnecessary.
        WorldFlight.Depart(this); return !Spawned;
    }
    private bool TickFlightSafety()
    {
        if (!autoReturn || raid || Faction != Faction.OfPlayer || State != FlightState.Flying || worldDeparture || landingRequested || Find.TickManager.TicksGame < safetyRetryAt) return false;
        if (fuel > Mathf.Max(MaxFuel * 0.1f, ReturnFuelRequired + 8)) return false;
        safetyRetryAt = Find.TickManager.TicksGame + 300;
        if (WorldFlight.FindArrival(Map, this, out _, out _))
        {
            RequestLanding();
            if (landingRequested) { Messages.Message("燃料偏低，正在降落。", this, MessageTypeDefOf.CautionInput); return false; }
        }
        if (returnBaseTile.Valid && returnBaseTile != Map.Tile && ReturnToBase())
        { Messages.Message("燃料偏低，正在返回出发机场。", MessageTypeDefOf.CautionInput, false); return true; }
        AV.Say("燃料偏低，当前没有可用机场。"); return false;
    }
    private void ExposeOperations()
    {
        Scribe_Values.Look(ref returnBaseTile, "ravReturnBaseTile", PlanetTile.Invalid);
        Scribe_Values.Look(ref arriveAirborne, "ravArriveAirborne"); Scribe_Values.Look(ref autoReturn, "ravAutoReturn", true);
        Scribe_Values.Look(ref boostTicks, "ravBoostTicks");
    }
}
