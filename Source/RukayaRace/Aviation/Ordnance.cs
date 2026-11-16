using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

// Explicit mapping preserves the serialized values of both original enums.
public static class Ordnance
{
    public static StoreKind Store(AttackKind kind) => kind switch
    {
        AttackKind.Bomb => StoreKind.Bomb, AttackKind.Rocket => StoreKind.Rocket,
        AttackKind.Cannon => StoreKind.Cannon, AttackKind.IncendiaryBomb => StoreKind.IncendiaryBomb,
        AttackKind.ClusterBomb => StoreKind.ClusterBomb, AttackKind.GroundMissile => StoreKind.GroundMissile,
        AttackKind.ActiveMatterMissile => StoreKind.ActiveMatterMissile, _ => StoreKind.Empty
    };
    public static bool Matches(StoreKind store, AttackKind attack) => attack != AttackKind.None && store == Store(attack);
    public static bool IsBomb(AttackKind kind) => kind is AttackKind.Bomb or AttackKind.IncendiaryBomb or AttackKind.ClusterBomb;
    public static ThingDef ProjectileDef(AttackKind kind) => AV.Def(kind + "Projectile");
    public static float PreviewRadius(AttackKind kind) => kind == AttackKind.ClusterBomb
        ? ProjectileDef(kind).GetModExtension<ClusterProjectileExtension>().fragmentSearchRadius
        : ProjectileDef(kind).projectile.explosionRadius;
    public static string RouteLabel(AttackKind kind) => kind switch
    {
        AttackKind.Bomb => "轰炸航线", AttackKind.IncendiaryBomb => "燃烧弹航线",
        AttackKind.ClusterBomb => "集束轰炸航线", AttackKind.Rocket => "火箭航线", _ => "机炮航线"
    };
    public static List<(ThingDef def, int count)> RearmCost(StoreKind kind, int count) => kind switch
    {
        StoreKind.Bomb => new() { (ThingDefOf.Shell_HighExplosive, count) },
        StoreKind.IncendiaryBomb => new() { (DefDatabase<ThingDef>.GetNamed("Shell_Incendiary"), count) },
        StoreKind.ClusterBomb => new() { (ThingDefOf.Shell_HighExplosive, count * 2), (ThingDefOf.ComponentIndustrial, count) },
        StoreKind.Rocket => new() { (ThingDefOf.Steel, count * 2), (ThingDefOf.Chemfuel, count) },
        StoreKind.Cannon => new() { (ThingDefOf.Steel, Mathf.CeilToInt(count / 20f)) },
        StoreKind.GroundMissile => new() { (ThingDefOf.Shell_HighExplosive, count), (ThingDefOf.ComponentIndustrial, count * 2) },
        StoreKind.ActiveMatterMissile => new() { (DefDatabase<ThingDef>.GetNamed("Shell_AntigrainWarhead"), count), (ThingDefOf.ComponentIndustrial, count * 4) },
        _ => new()
    };
}

public class AviationWarheadExtension : DefModExtension
{
    public float directDamage;
    public float directArmorPenetration;
}

public partial class Aircraft
{
    public Thing lockedTarget;
    public bool requiresTarget;
    private int targetAttackTicks;
    public bool ValidGroundTarget(Thing target) => target != null && target != this && target is not Aircraft
        && target.Spawned && !target.Destroyed && target.Map == Map && !target.Position.Fogged(Map)
        && (target is Pawn p ? !p.Dead && !p.Downed : target is Building && target.def.useHitPoints);

    public bool OrderTargetAttack(AttackKind kind, Thing target, IntVec3 point, bool hover = false)
    {
        bool needsThing = kind != AttackKind.ActiveMatterMissile;
        if (State != FlightState.Flying || hover && Heavy || !HasWeapon(kind) || Ammo(kind) <= 0)
        { AV.Say("需要起飞并携带对应弹药；固定翼无法悬停。"); return false; }
        if (kind is not (AttackKind.GroundMissile or AttackKind.ActiveMatterMissile) && (!hover || kind is not (AttackKind.Rocket or AttackKind.Cannon))) return false;
        if (needsThing && !ValidGroundTarget(target) || !needsThing && (target != null || !point.InBounds(Map)))
        { AV.Say("请选择有效地面单位或建筑；活性物质导弹只能指定地面点。"); return false; }
        point = needsThing ? target.Position : point;
        if (kind == AttackKind.ActiveMatterMissile && (!Heavy || mounts[CenterHardpoint].kind != StoreKind.ActiveMatterMissile || mounts[CenterHardpoint].rounds <= 0)) return false;
        float distance = AV.Distance(realPos, AV.Flat(point));
        if (!Heavy && (distance < 6 || distance > (hover ? 40 : 100)))
        { AV.Say(hover ? "悬停攻击距离为6至40格。" : "导弹攻击距离为6至100格。"); return false; }
        CancelAttack(); ReleaseReservations(); landingRequested = false; loitering = false;
        BoostEngines();
        route.Clear(); routeIndex = 0; attack = kind; lockedTarget = target; requiresTarget = needsThing;
        attackStart = realPos; attackEnd = AV.Flat(point); attacking = true;
        attackPhase = hover ? AttackPhase.Hover : AttackPhase.Target;
        shotsRemaining = Math.Min(CommandShots(kind), Ammo(kind));
        plannedImpacts = Enumerable.Repeat(attackEnd, shotsRemaining).ToList(); nextShot = Find.TickManager.TicksGame;
        return true;
    }
    private void TickTargetAttack()
    {
        if (requiresTarget && !ValidGroundTarget(lockedTarget) || ++targetAttackTicks > 1800) { FinishTargetAttack(); return; }
        if (requiresTarget) attackEnd = AV.Flat(lockedTarget.Position);
        for (int i = shotIndex; i < plannedImpacts.Count; i++) plannedImpacts[i] = attackEnd;
        float distance = AV.Distance(realPos, attackEnd);
        float desired = (attackEnd - realPos).AngleFlat();
        if (Heavy)
        {
            // Fixed wings continue moving throughout alignment. Turn inward early,
            // never let a targeting order cross the player's map safety envelope.
            Vector3 ahead = realPos + AV.Dir(yaw) * 16;
            if (!AV.SafeAir(ahead.ToIntVec3(), Map, 16))
            { FinishTargetAttack(); return; }
            yaw = Mathf.MoveTowardsAngle(yaw, desired, 2.4f);
            realPos += AV.Dir(yaw) * (Speed / 60f);
        }
        else yaw = Mathf.MoveTowardsAngle(yaw, desired, 4f);
        if (distance < 6 || !Heavy && distance > (attackPhase == AttackPhase.Hover ? 40 : 100)) { FinishTargetAttack(); return; }
        if (distance <= 100 && Find.TickManager.TicksGame >= nextShot) FireMounted();
        if (shotsRemaining <= 0) FinishTargetAttack();
    }
    private void FinishTargetAttack()
    {
        CancelAttack();
        if (raid) { if (Heavy) BeginRaidExit(); else AirRaids.RaidPass(this); }
        else Loiter();
    }
    private void TargetWeapon(AttackKind kind, bool hover = false)
    {
        bool point = kind == AttackKind.ActiveMatterMissile;
        TargetingParameters parameters = point ? AV.LocationTargets : new TargetingParameters
        {
            canTargetLocations = false, canTargetPawns = true, canTargetBuildings = true,
            mapObjectTargetsMustBeAutoAttackable = false, neverTargetIncapacitated = true,
            validator = t => ValidGroundTarget(t.Thing)
        };
        Find.Targeter.BeginTargeting(parameters,
            t => OrderTargetAttack(kind, point ? null : t.Thing, t.Cell, hover),
            t => { if (t.Cell.InBounds(Map)) DrawAttackPreview(kind, realPos, AV.Flat(t.Cell), new[] { AV.Flat(t.Cell) }); },
            t => point ? t.Cell.InBounds(Map) : ValidGroundTarget(t.Thing));
    }
}

// Rebase each tick on the current position and target. Retain native interception,
// shields, hit resolution and explosions rather than teleporting damage to a target.
public class GuidedOrdnance : Projectile_Explosive
{
    private int segmentTicks;
    private bool launched;
    public IntVec3 AimCell => destination.ToIntVec3();
    public int TrailPuffs { get; private set; }
    protected override int MinTickIntervalRate => 1;
    public override int UpdateRateTicks => 1;
    public override Vector3 ExactPosition => !launched ? base.ExactPosition : Vector3.MoveTowards(origin.Yto0(), destination.Yto0(),
        Math.Max(0, segmentTicks - ticksToImpact) * def.projectile.SpeedTilesPerTick) + Vector3.up * def.Altitude;
    public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
        ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
    {
        base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        // A point-strike missile never acquires units along the way.
        if (def == AV.Def("ActiveMatterMissileProjectile")) destination = usedTarget.Cell.ToVector3Shifted();
        segmentTicks = ticksToImpact; launched = true;
    }
    protected override void TickInterval(int delta)
    {
        if (!launched || landed) { base.TickInterval(delta); return; }
        Vector3 current = ExactPosition.Yto0();
        Thing target = intendedTarget.Thing;
        if (target != null)
        {
            if (target.Spawned && !target.Destroyed && target.Map == Map && (target is not Pawn pawn || !pawn.Dead && !pawn.Downed))
                destination = target.Position.ToVector3Shifted();
            else { intendedTarget = usedTarget = new LocalTargetInfo(AimCell); }
        }
        origin = current;
        ticksToImpact = segmentTicks = Math.Max(1, Mathf.CeilToInt(AV.Distance(current, destination) / def.projectile.SpeedTilesPerTick));
        Vector3 next = Vector3.MoveTowards(current, destination.Yto0(), def.projectile.SpeedTilesPerTick * delta);
        int count = Math.Max(1, Mathf.CeilToInt(AV.Distance(current, next) / 0.9f));
        for (int i = 0; i < count; i++)
        {
            Vector3 start = Vector3.Lerp(current, next, (float)i / count), end = Vector3.Lerp(current, next, (float)(i + 1) / count);
            Aircraft.EmitTrail(Map, "RAV_MissileTrail", start, end, 0.30f, new Color(0.94f, 0.96f, 1, 0.8f)); TrailPuffs++;
        }
        base.TickInterval(delta);
    }
    protected override void Impact(Thing hitThing, bool blockedByShield = false)
    {
        AviationWarheadExtension warhead = def.GetModExtension<AviationWarheadExtension>();
        if (!blockedByShield && hitThing != null && warhead?.directDamage > 0)
            hitThing.TakeDamage(new DamageInfo(DamageDefOf.Bullet, warhead.directDamage, warhead.directArmorPenetration, ExactRotation.eulerAngles.y, launcher,
                weapon: equipmentDef ?? def, intendedTarget: intendedTarget.Thing));
        base.Impact(hitThing, blockedByShield);
    }
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_Values.Look(ref segmentTicks, "ravSegmentTicks"); Scribe_Values.Look(ref launched, "ravGuidedLaunched");
    }
}
