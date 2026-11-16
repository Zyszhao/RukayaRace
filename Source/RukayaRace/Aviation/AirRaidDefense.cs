using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public partial class Aircraft
{
    public float airPressure;
    public int lastPressureTick;
    private bool applyingAirDefenseDamage;
    public float PressureLimit => Heavy ? 260 : 160;
    public bool CanBeSuppressed => raid && Spawned && Airborne && !raidExiting && State != FlightState.Crashing
        && Faction != null && Faction.HostileTo(Faction.OfPlayer);

    public void ApplyAirDefenseDamage(float amount, Thing launcher)
    {
        // AA should destroy hulls; do not turn it into low-damage morale fire.
        applyingAirDefenseDamage = true;
        try { TakeDamage(new DamageInfo(DamageDefOf.Bullet, amount, 1f, instigator: launcher)); }
        finally { applyingAirDefenseDamage = false; }
    }

    public void AddAirPressure(float amount, Thing source)
    {
        if (!CanBeSuppressed || source?.Faction == null || !Faction.HostileTo(source.Faction) || amount <= 0) return;
        airPressure += amount; lastPressureTick = Find.TickManager.TicksGame;
        if (airPressure < PressureLimit) return;
        BeginRaidExit();
        Messages.Message("RAV_AirRaidDrivenOff".Translate(LabelCap), this, MessageTypeDefOf.PositiveEvent);
    }

    public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
    {
        base.PostApplyDamage(dinfo, totalDamageDealt);
        if (!applyingAirDefenseDamage && (dinfo.Def.isRanged || dinfo.Weapon?.IsRangedWeapon == true))
            AddAirPressure(totalDamageDealt * 1.1f, dinfo.Instigator);
    }
}

public class PendingAirRaid : IExposable
{
    public bool heavy, intercepted;
    public int arrivalTick, resolveTick;
    public Faction faction;
    public void ExposeData()
    {
        Scribe_Values.Look(ref heavy, "heavy"); Scribe_Values.Look(ref intercepted, "intercepted");
        Scribe_Values.Look(ref arrivalTick, "arrivalTick"); Scribe_Values.Look(ref resolveTick, "resolveTick");
        Scribe_References.Look(ref faction, "faction");
    }
    public bool TryIntercept(Map map)
    {
        if (intercepted || faction == null || !faction.HostileTo(Faction.OfPlayer)) return false;
        if (!AirDefenseBridge.TryLaunchStrategicInterceptor(map, out resolveTick)) return false;
        intercepted = true; return true;
    }
}

public partial class AirportMap
{
    public List<PendingAirRaid> pendingRaids = new();
    private List<Projectile> pressureShots = new();
    private HashSet<Projectile> seenPressureShots = new();

    public void QueueRaid(bool heavy, int arrivalTick, Faction faction)
    {
        var raid = new PendingAirRaid { heavy = heavy, arrivalTick = arrivalTick, faction = faction };
        pendingRaids.Add(raid); raid.TryIntercept(map);
    }
    private void TickAirDefense()
    {
        int now = Find.TickManager.TicksGame;
        // Migrate the old single warning without discarding it or duplicating it on load.
        if (raidAt >= 0) { QueueRaid(heavyRaid, raidAt, AirRaids.HostileFaction()); raidAt = -1; }
        foreach (PendingAirRaid raid in pendingRaids.ToList())
        {
            // Also accept a launcher rearmed after the warning, right up to entry.
            if (!raid.intercepted && (now % 30 == 0 || now >= raid.arrivalTick)) raid.TryIntercept(map);
            if (raid.intercepted)
            {
                if (now < raid.resolveTick) continue;
                pendingRaids.Remove(raid);
                Find.LetterStack.ReceiveLetter("RAV_PreEntryInterceptTitle".Translate(),
                    "RAV_PreEntryInterceptBody".Translate(), LetterDefOf.PositiveEvent, new TargetInfo(map.Center, map));
            }
            else if (now >= raid.arrivalTick)
            {
                pendingRaids.Remove(raid);
                if (raid.faction != null && raid.faction.HostileTo(Faction.OfPlayer)) AirRaids.SpawnRaidUnchecked(map, raid.heavy, raid.faction);
            }
        }

        var targets = AV.Planes(map).Where(p => p.CanBeSuppressed).ToList();
        if (now % 60 == 0)
        {
            pressureShots.RemoveAll(p => p == null || !p.Spawned || p.Destroyed);
            seenPressureShots = new HashSet<Projectile>(pressureShots);
        }
        if (targets.Count == 0) return;
        foreach (Aircraft plane in targets)
            if (now - plane.lastPressureTick > 180 && now % 30 == 0) plane.airPressure = Mathf.Max(0, plane.airPressure - 1);
        // Count actual shots, including misses. Normal verb range, LOS and ammunition
        // checks still apply. Interceptor visual rounds must never count as small arms.
        foreach (Projectile shot in map.listerThings.AllThings.OfType<Projectile>())
        {
            if (shot.intendedTarget.Thing is not Aircraft plane || !plane.CanBeSuppressed
                || shot.Launcher is Aircraft || shot.Launcher?.Faction == null || !plane.Faction.HostileTo(shot.Launcher.Faction)
                || !(shot.DamageDef.isRanged || shot.EquipmentDef?.IsRangedWeapon == true)
                || shot.def.defName == "Rukaya_SkyInterceptorAirProjectile" || !seenPressureShots.Add(shot)) continue;
            pressureShots.Add(shot);
            plane.AddAirPressure(Mathf.Clamp(3 + shot.DamageAmount * 0.25f, 3, 12), shot.Launcher);
        }
    }
    private void ExposeAirDefense()
    {
        Scribe_Collections.Look(ref pendingRaids, "ravPendingAirRaids", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.Saving) pressureShots.RemoveAll(p => p == null || !p.Spawned || p.Destroyed);
        Scribe_Collections.Look(ref pressureShots, "ravPressureShots", LookMode.Reference);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            pendingRaids ??= new(); pressureShots ??= new();
            pressureShots.RemoveAll(p => p == null);
            seenPressureShots = new HashSet<Projectile>(pressureShots);
        }
    }
}
