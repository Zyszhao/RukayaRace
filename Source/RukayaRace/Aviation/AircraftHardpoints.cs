using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public partial class Aircraft
{
    private int hardpointLayoutVersion = 3;
    private List<Hardpoint> retiredMounts = new();
    public int RetiredMountCount => retiredMounts.Count;
    public void NormalizeHardpoints()
    {
        mounts ??= new(); retiredMounts ??= new(); pendingAmmoRefunds ??= new();
        if (hardpointLayoutVersion == 0)
        {
            // Old indexing alternated left/right. Preserve inner stores first;
            // a sixth store becomes recoverable cargo, never silently deleted.
            int[] order = Heavy ? new[] { 0, 2, 4, 5, 3, 1 } : new[] { 0, 2, 3, 1 };
            List<Hardpoint> old = order.Where(i => i < mounts.Count).Select(i => mounts[i]).ToList();
            old.AddRange(mounts.Skip(order.Length));
            mounts = Enumerable.Range(0, 5).Select(_ => new Hardpoint()).ToList();
            int inner = 1;
            foreach (Hardpoint store in old.Where(m => m.kind is StoreKind.Tank or StoreKind.CargoPod or StoreKind.PassengerPod))
            {
                if (inner <= 3) mounts[inner++] = store; else retiredMounts.Add(store);
            }
            foreach (Hardpoint store in old.Where(m => m.kind is not (StoreKind.Empty or StoreKind.Tank or StoreKind.CargoPod or StoreKind.PassengerPod)))
            {
                int free = mounts.FindIndex(m => m.kind == StoreKind.Empty);
                if (free >= 0) mounts[free] = store; else retiredMounts.Add(store);
            }
            CancelAttack(); hardpointLayoutVersion = 1;
        }
        while (mounts.Count < (hardpointLayoutVersion < 3 ? 5 : HardpointCount)) mounts.Add(new Hardpoint());
        if (hardpointLayoutVersion < 2)
        {
            if (!Heavy && !CanMount(2, mounts[2].kind))
            {
                Hardpoint store = mounts[2]; mounts[2] = new Hardpoint();
                int free = Enumerable.Range(0, 5).FirstOrDefault(i => mounts[i].kind == StoreKind.Empty && CanMount(i, store.kind), -1);
                if (free >= 0) mounts[free] = store; else retiredMounts.Add(store);
                CancelAttack();
            }
            hardpointLayoutVersion = 2;
        }
        if (hardpointLayoutVersion < 3)
        {
            if (Heavy && mounts.Count == 5)
            {
                // Preserve the center and both old wing pairs; add empty outer pylons.
                mounts.Insert(0, new Hardpoint()); mounts.Add(new Hardpoint());
            }
            CancelAttack(); hardpointLayoutVersion = 3;
        }
        while (mounts.Count < HardpointCount) mounts.Add(new Hardpoint());
        if (mounts.Count > HardpointCount) { retiredMounts.AddRange(mounts.Skip(HardpointCount)); mounts.RemoveRange(HardpointCount, mounts.Count - HardpointCount); }
        for (int i = 0; i < mounts.Count; i++)
        {
            int excess = mounts[i].rounds - StoreCapacity(i, mounts[i].kind);
            if (excess > 0) { pendingAmmoRefunds.Add(new Hardpoint { kind = mounts[i].kind, rounds = excess }); mounts[i].rounds -= excess; }
        }
        if (!Heavy) FixedCannon.kind = StoreKind.Cannon;
        if (Refuelable != null && fuel > MaxFuel)
        {
            float excess = fuel - MaxFuel; Refuelable.ConsumeFuel(excess); pendingFuelUnload += excess;
        }
    }
    private void RecoverRetiredMounts()
    {
        if (!Spawned || State != FlightState.Parked || Cargo?.innerContainer == null) return;
        foreach (Hardpoint store in retiredMounts.ToList())
        {
            if (store.kind != StoreKind.Empty)
            {
                Refund(ThingDefOf.Steel, 10); Refund(ThingDefOf.ComponentIndustrial, 1);
                foreach (var cost in Ordnance.RearmCost(store.kind, store.rounds)) Refund(cost.def, cost.count);
            }
            retiredMounts.Remove(store);
        }
        foreach (Hardpoint ammo in pendingAmmoRefunds.ToList())
        {
            foreach (var cost in Ordnance.RearmCost(ammo.kind, ammo.rounds)) Refund(cost.def, cost.count);
            pendingAmmoRefunds.Remove(ammo);
        }
    }
    private void Refund(ThingDef def, int count)
    {
        while (count > 0)
        {
            Thing item = ThingMaker.MakeThing(def); item.stackCount = Math.Min(count, def.stackLimit); count -= item.stackCount;
            if (!GenPlace.TryPlaceThing(item, Position, Map, ThingPlaceMode.Near)) Cargo.innerContainer.TryAdd(item);
        }
    }
    private List<Hardpoint> pendingAmmoRefunds = new();
    public bool HasWeapon(AttackKind kind) => !Heavy && kind == AttackKind.Cannon || mounts.Any(m => Ordnance.Matches(m.kind, kind));
    private Command_Action WeaponCommand(AttackKind kind, string label, string description, Action action, string icon = null)
    {
        Command_Action command = AV.Button($"{label}（{Ammo(kind)}）", description, action,
            icon ?? AviationArt.WeaponIcon(kind));
        if (!HasWeapon(kind)) command.Disable("未携带对应武器挂载。");
        else if (Ammo(kind) <= 0) command.Disable("对应武器备弹已用尽。");
        return command;
    }
    public Thing FindCorrectedTarget(AttackKind kind, Vector3 origin, IntVec3 nominal)
    {
        if (kind is not (AttackKind.Rocket or AttackKind.Cannon) || Map == null) return null;
        float radius = kind == AttackKind.Rocket ? 4 : 3;
        Vector3 center = AV.Flat(nominal), forward = AV.Dir(yaw), segment = attackEnd - attackStart;
        float lengthSquared = segment.sqrMagnitude;
        return Map.listerThings.AllThings.Where(t => t != this && t is not Aircraft && t.Spawned && !t.Position.Fogged(Map) && t.HostileTo(this)
                && (t is Pawn pawn && !pawn.Dead && !pawn.Downed || t is Building && t.def.useHitPoints && t.def.fillPercent > 0.1f))
            .Where(t =>
            {
                Vector3 point = AV.Flat(t.Position), to = point - origin;
                if (AV.Distance(point, center) > radius || Vector3.Dot(to, forward) < 1 || Mathf.Abs(Mathf.DeltaAngle(yaw, to.AngleFlat())) > 18) return false;
                if (attackPhase == AttackPhase.Hover) return true;
                float along = lengthSquared > 0 ? Vector3.Dot(point - attackStart, segment) / lengthSquared : 0;
                if (along < 0 || along > 1) return false;
                return AV.Distance(point, attackStart + segment * along) <= radius;
            })
            .OrderBy(t => t.Position.DistanceToSquared(nominal)).ThenBy(t => t is Pawn ? 0 : 1).ThenBy(t => t.thingIDNumber).FirstOrDefault();
    }
}
