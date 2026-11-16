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
        if (Find.TickManager.TicksGame % 250 == 0) foreach (RunwayZone z in map.zoneManager.AllZones.OfType<RunwayZone>()) z.MaintainVegetation();
        impacts.RemoveAll(p => p.projectile == null || p.projectile.Destroyed || !p.projectile.Spawned);
    }
    public override void MapComponentDraw()
    {
        if (Find.CurrentMap != map) return;
        if (Find.PlaySettings.showZones) foreach (RunwayZone z in map.zoneManager.AllZones.OfType<RunwayZone>()) z.DrawMarkers();
        foreach (ImpactMarker p in ActiveImpacts)
        {
            IntVec3 cell = p.projectile is GuidedOrdnance guided ? guided.AimCell : p.cell;
            Aircraft.DrawAttackPreview(p.kind, AV.Flat(cell), AV.Flat(cell), new[] { AV.Flat(cell) });
        }
    }
    public override void FinalizeInit()
    {
        // Repair legacy many-to-one bindings deterministically without deleting aircraft or cargo.
        foreach (ParkingPad pad in AV.Pads(map).ToList()) if (pad.bound != null && !pad.bound.Destroyed)
        {
            if (pad.bound.homePad == null || pad.bound.homePad == pad) pad.Bind(pad.bound); else pad.bound = null;
        }
        foreach (Aircraft plane in AV.Planes(map).ToList()) if (plane.homePad?.Spawned == true) plane.homePad.Bind(plane);
        foreach (Aircraft plane in AV.Planes(map).Where(p => p.State == FlightState.Parked && p.homePad == null && p.Faction == Faction.OfPlayer).ToList())
        {
            ParkingPad pad = AV.Pads(map).FirstOrDefault(p => p.Position == plane.Position && p.Faction == plane.Faction && (p.bound == null || p.bound.Destroyed));
            if (pad != null) pad.Bind(plane);
        }
    }
    public override void ExposeData()
    {
        ExposeAirDefense();
        Scribe_Values.Look(ref raidAt, "raidAt", -1); Scribe_Values.Look(ref heavyRaid, "heavyRaid");
        if (Scribe.mode == LoadSaveMode.Saving) impacts.RemoveAll(p => p.projectile == null || p.projectile.Destroyed || !p.projectile.Spawned);
        Scribe_Collections.Look(ref impacts, "ravImpactMarkers", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) impacts ??= new();
    }
}
