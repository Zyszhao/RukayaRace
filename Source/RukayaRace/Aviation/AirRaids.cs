using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public static class AirRaids
{
    public static void ScheduleRaid(Map map, bool heavy)
    {
        Faction faction = HostileFaction(); if (faction == null) return;
        map.GetComponent<AirportMap>().QueueRaid(heavy, Find.TickManager.TicksGame + 600, faction);
        Find.LetterStack.ReceiveLetter("空袭预警", "侦测到敌机接近，即将进入地图。请准备防空并疏散跑道、仓库附近的人员。", LetterDefOf.ThreatBig, new TargetInfo(map.Center, map));
    }
    public static Faction HostileFaction()
    {
        return Find.FactionManager.AllFactionsVisible.FirstOrDefault(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer)
            && f.def.defName == "Rukaya_DrifterAlliance");
    }
    public static Aircraft SpawnRaid(Map map, bool heavy)
    {
        Faction faction = HostileFaction(); if (faction == null) return null;
        var pending = new PendingAirRaid { heavy = heavy, faction = faction, arrivalTick = Find.TickManager.TicksGame };
        if (pending.TryIntercept(map)) { map.GetComponent<AirportMap>().pendingRaids.Add(pending); return null; }
        return SpawnRaidUnchecked(map, heavy, faction);
    }
    internal static Aircraft SpawnRaidUnchecked(Map map, bool heavy, Faction faction)
    {
        Aircraft plane = (Aircraft)ThingMaker.MakeThing(AV.Def(heavy ? "Mig31" : "Yak141")); plane.SetFaction(faction);
        bool west = Rand.Bool;
        GenSpawn.Spawn(plane, new IntVec3(west ? 5 : map.Size.x - 6, 0, map.Center.z), map);
        plane.raid = true; plane.raidEntering = true; plane.State = FlightState.Flying; plane.fuel = plane.MaxFuel; plane.yaw = west ? 90 : 270;
        plane.ResetFlightVisuals(); plane.Refuelable.SyncGroundAccess();
        plane.raidDuration = heavy ? 12000 : 3600;
        plane.SetRoute(new[] { AV.Flat(new IntVec3(west ? 35 : map.Size.x - 36, 0, map.Center.z)) });
        for (int i = 0; i < plane.mounts.Count; i++) { plane.mounts[i].kind = heavy ? StoreKind.Bomb : i == 2 ? StoreKind.Empty : i < 2 ? StoreKind.Rocket : StoreKind.Cannon; plane.mounts[i].rounds = plane.StoreCapacity(i, plane.mounts[i].kind); }
        return plane;
    }
    public static void RaidPass(Aircraft p)
    {
        if (p.raidExiting) return;
        if ((p.Heavy && p.passes >= 1) || !p.Heavy && p.ageTicks >= p.raidDuration) { p.BeginRaidExit(); return; }
        p.passes++;
        IntVec3 center = p.Map.listerThings.AllThings.Where(t => t.Faction == Faction.OfPlayer && t is Building && !(t is Aircraft))
            .Select(t => t.Position).RandomElementWithFallback(p.Map.Center);
        center.x = Mathf.Clamp(center.x, 55, p.Map.Size.x - 56); center.z = Mathf.Clamp(center.z, 55, p.Map.Size.z - 56);
        IntVec3 direction = p.passes % 2 == 0 ? IntVec3.West : IntVec3.East;
        AttackKind kind = p.mounts.Any(m => m.kind == StoreKind.Bomb && m.rounds > 0) ? AttackKind.Bomb
            : p.mounts.Any(m => m.kind == StoreKind.Rocket && m.rounds > 0) ? AttackKind.Rocket : AttackKind.Cannon;
        if (!p.OrderAttack(kind, center - direction * 14, center + direction * 14)) p.BeginRaidExit();
    }
}
public class IncidentWorker_AirRaid : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map map && map.IsPlayerHome && map.Size.x >= 140 && AirRaids.HostileFaction() != null;
    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        if (!(parms.target is Map map) || AirRaids.HostileFaction() == null) return false;
        AirRaids.ScheduleRaid(map, parms.points > 1800); return true;
    }
}
