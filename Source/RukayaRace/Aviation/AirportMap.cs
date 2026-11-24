using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public class ImpactMarker : IExposable
{
    public Projectile projectile;
    public IntVec3 cell;
    public AttackKind kind;
    public void ExposeData() { Scribe_References.Look(ref projectile, "projectile"); Scribe_Values.Look(ref cell, "cell"); Scribe_Values.Look(ref kind, "kind"); }
}

public partial class AirportMap : MapComponent
{
    public Action pending;
    public int raidAt = -1;
    public bool heavyRaid;
    private List<ImpactMarker> impacts = new();
    private List<PendingAirborneTransport> pendingAirborneTransports = new();
    public IEnumerable<ImpactMarker> ActiveImpacts => impacts.Where(p => p.projectile?.Spawned == true && !p.projectile.Destroyed);
    public void MarkImpact(Projectile projectile, IntVec3 cell, AttackKind kind) => impacts.Add(new ImpactMarker { projectile = projectile, cell = cell, kind = kind });
    public AirportMap(Map map) : base(map) { }
    public override void MapComponentUpdate()
    {
        if (pending != null && !Find.Targeter.IsTargeting) { Action a = pending; pending = null; a(); }
    }
    public override void MapComponentTick()
    {
        TickAirDefense();
        TickPendingAirborneTransports();
        if (Find.TickManager.TicksGame % 250 == 0)
        {
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] is RunwayZone z) z.MaintainVegetation();
        }
        if (impacts.Count > 0)
            impacts.RemoveAll(p => p.projectile == null || p.projectile.Destroyed || !p.projectile.Spawned);
    }
    public void QueueAirborneTransport(Aircraft plane, int spawnTick, IntVec3 spawnCell, Vector3 entryPos, float yaw)
    {
        PendingAirborneTransport entry = new PendingAirborneTransport(map)
        {
            spawnTick = spawnTick,
            spawnCell = spawnCell,
            entryPos = entryPos,
            yaw = yaw
        };
        if (entry.contents.TryAdd(plane, false)) pendingAirborneTransports.Add(entry);
    }
    private void TickPendingAirborneTransports()
    {
        if (pendingAirborneTransports == null || pendingAirborneTransports.Count == 0) return;
        int now = Find.TickManager.TicksGame;
        for (int i = pendingAirborneTransports.Count - 1; i >= 0; i--)
        {
            PendingAirborneTransport entry = pendingAirborneTransports[i];
            if (entry == null || entry.spawnTick > now) continue;
            Aircraft plane = entry.contents.OfType<Aircraft>().FirstOrDefault();
            if (plane == null)
            {
                pendingAirborneTransports.RemoveAt(i);
                continue;
            }
            entry.contents.Remove(plane);
            pendingAirborneTransports.RemoveAt(i);
            GenSpawn.Spawn(plane, entry.spawnCell, map);
            plane.realPos = entry.entryPos;
            plane.yaw = entry.yaw;
            plane.airborneStartDelayTicks = 0;
            plane.ResetFlightVisuals();
            plane.Refuelable.SyncGroundAccess();
        }
    }
    public override void MapRemoved()
    {
        if (pendingAirborneTransports == null) return;
        foreach (PendingAirborneTransport entry in pendingAirborneTransports)
        {
            if (entry == null) continue;
            foreach (Aircraft plane in entry.contents.OfType<Aircraft>().ToList())
            {
                entry.contents.Remove(plane);
                plane.DestroyRemainingAirborneOccupants();
                if (!plane.Destroyed) plane.Destroy(DestroyMode.Vanish);
            }
        }
        pendingAirborneTransports.Clear();
    }
    public override void MapComponentDraw()
    {
        if (Find.CurrentMap != map) return;
        if (Find.PlaySettings.showZones)
        {
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] is RunwayZone z) z.DrawMarkers();
        }
        for (int i = 0; i < impacts.Count; i++)
        {
            ImpactMarker p = impacts[i];
            if (p.projectile?.Spawned != true || p.projectile.Destroyed) continue;
            IntVec3 cell = p.projectile is GuidedOrdnance guided ? guided.AimCell : p.cell;
            Aircraft.DrawSingleImpactPreview(p.kind, cell);
        }
    }
    public override void FinalizeInit()
    {
        // Repair legacy many-to-one bindings deterministically without deleting aircraft or cargo.
        foreach (ParkingPad pad in AV.Pads(map).ToList()) if (pad.bound != null && !pad.bound.Destroyed)
        {
            if (pad.Supports(pad.bound) && (pad.bound.homePad == null || pad.bound.homePad == pad)) pad.Bind(pad.bound); else pad.bound = null;
        }
        foreach (Aircraft plane in AV.Planes(map).ToList()) if (plane.homePad?.Spawned == true && plane.homePad.Supports(plane)) plane.homePad.Bind(plane);
        foreach (Aircraft plane in AV.Planes(map).Where(p => p.State == FlightState.Parked && p.homePad == null && p.Faction == Faction.OfPlayer).ToList())
        {
            ParkingPad pad = AV.Pads(map).FirstOrDefault(p => p.Position == plane.Position && p.Faction == plane.Faction && p.Supports(plane) && (p.bound == null || p.bound.Destroyed));
            if (pad != null) pad.Bind(plane);
        }
    }
    public override void ExposeData()
    {
        ExposeAirDefense();
        Scribe_Values.Look(ref raidAt, "raidAt", -1); Scribe_Values.Look(ref heavyRaid, "heavyRaid");
        if (Scribe.mode == LoadSaveMode.Saving) impacts.RemoveAll(p => p.projectile == null || p.projectile.Destroyed || !p.projectile.Spawned);
        Scribe_Collections.Look(ref impacts, "ravImpactMarkers", LookMode.Deep);
        Scribe_Collections.Look(ref pendingAirborneTransports, "ravPendingAirborneTransports", LookMode.Deep, map);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) pendingAirborneTransports ??= new();
    }
}

public class PendingAirborneTransport : IExposable, IThingHolder
{
    public int spawnTick;
    public IntVec3 spawnCell;
    public Vector3 entryPos;
    public float yaw;
    public ThingOwner<Thing> contents;
    private Map map;

    public PendingAirborneTransport() { }
    public PendingAirborneTransport(Map map)
    {
        this.map = map;
        contents = new ThingOwner<Thing>(this);
    }
    public IThingHolder ParentHolder => map;
    public ThingOwner GetDirectlyHeldThings() => contents;
    public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);
    public void ExposeData()
    {
        Scribe_Deep.Look(ref contents, "contents", this);
        Scribe_Values.Look(ref spawnTick, "spawnTick");
        Scribe_Values.Look(ref spawnCell, "spawnCell");
        Scribe_Values.Look(ref entryPos, "entryPos");
        Scribe_Values.Look(ref yaw, "yaw");
        if (Scribe.mode == LoadSaveMode.PostLoadInit) contents ??= new ThingOwner<Thing>(this);
    }
}
