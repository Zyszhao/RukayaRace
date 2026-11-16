using System;
using RimWorld;
using RukayaRace.AirDefense;
using Verse;

namespace RukayaAviation;

// Native integration: keep the launcher's own readiness, ammunition and firing
// logic. Aircraft have hull HP instead of the one-hit skyfaller resolution.
public static class AirDefenseBridge
{
    public static bool TryHit(Aircraft plane, Thing launcher, Faction faction)
    {
        if (!plane.Spawned || !plane.Airborne || plane.State == FlightState.Crashing
            || faction == null || plane.Faction == null || !plane.Faction.HostileTo(faction)) return false;
        var weapon = (launcher as ThingWithComps)?.GetComp<CompRukayaSkyInterceptor>()?.WeaponVerb;
        ThingDef projectile = weapon?.verbProps.defaultProjectile;
        float damage = projectile?.defName switch
        {
            "Bullet_Rukaya_MechAA_Gun" => 180,
            "Bullet_Rukaya_MechAA_Laser" => 240,
            "Bullet_Rukaya_MechAA_SAMLauncher" => 650,
            "Bullet_Rukaya_InterceptorMissile" => 900,
            _ => Math.Max(120, projectile?.projectile?.GetDamageAmount(launcher) ?? 0)
        };
        plane.ApplyAirDefenseDamage(damage, launcher);
        if (plane.Spawned) FleckMaker.ThrowMicroSparks(plane.DrawPos, plane.Map);
        return true;
    }

    public static bool TryLaunchStrategicInterceptor(Map map, out int resolveTick)
    {
        resolveTick = 0;
        CompRukayaSAMLauncher launcher = CompRukayaSAMLauncher.FindReady(map);
        if (launcher == null || !launcher.TryLaunch(out Thing_RukayaSAMMissile missile)) return false;
        resolveTick = Find.TickManager.TicksGame + missile.RemainingFlightTicks + 180;
        return true;
    }
}
