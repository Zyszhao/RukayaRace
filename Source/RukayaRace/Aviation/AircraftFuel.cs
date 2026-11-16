using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaAviation;

public class AircraftFuelProps : CompProperties_Refuelable
{
    public AircraftFuelProps() { compClass = typeof(AircraftFuel); }
    public AircraftFuelProps Copy() => (AircraftFuelProps)MemberwiseClone();
}

// Uses the actual pod-launcher fuel slider, hauling jobs and developer commands.
public class AircraftFuel : CompRefuelable
{
    private float parkedTarget = -1;
    private int fuelSystemVersion = 1;
    public override void Initialize(CompProperties properties) => base.Initialize(((AircraftFuelProps)properties).Copy());
    public void RefreshCapacity()
    {
        if (parent is not Aircraft plane) return;
        float old = Props.fuelCapacity, target = parkedTarget >= 0 ? parkedTarget : TargetFuelLevel;
        Props.fuelCapacity = plane.MaxFuel;
        target = Math.Abs(target - old) < 0.01f ? plane.MaxFuel : Math.Min(target, plane.MaxFuel);
        if (parkedTarget >= 0) parkedTarget = target; else TargetFuelLevel = target;
    }
    public void SyncGroundAccess()
    {
        if (((Aircraft)parent).State != FlightState.Parked)
        {
            if (parkedTarget < 0) parkedTarget = TargetFuelLevel;
            // Both automatic and forced vanilla refueling reject an already full target.
            TargetFuelLevel = 0;
        }
        else if (parkedTarget >= 0) { TargetFuelLevel = parkedTarget; parkedTarget = -1; }
    }
    public override void CompTick() { SyncGroundAccess(); base.CompTick(); }
    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        // Do not expose an editable fueling target while the aircraft is moving.
        foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            if (((Aircraft)parent).State == FlightState.Parked || DebugSettings.ShowDevGizmos && gizmo is Command_Action command && command.defaultLabel.StartsWith("DEV:")) yield return gizmo;
    }
    public override void PostExposeData()
    {
        // Comp save nodes are on the Thing itself. Vanilla's "fuel" node reads
        // the old demo's fuel field directly, without copying or losing a tank.
        base.PostExposeData();
        Scribe_Values.Look(ref parkedTarget, "ravParkedFuelTarget", -1);
        Scribe_Values.Look(ref fuelSystemVersion, "ravFuelSystemVersion");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (fuelSystemVersion == 0) allowAutoRefuel = true;
            fuelSystemVersion = 1;
        }
    }
}

public class AircraftNames : GameComponent
{
    private int nextYak = 1, nextMig = 1;
    public AircraftNames(Game game) { }
    public int Register(Aircraft plane, int number)
    {
        ref int next = ref (plane.Heavy ? ref nextMig : ref nextYak);
        if (number <= 0) number = next++;
        else next = Math.Max(next, number + 1);
        return number;
    }
    public override void ExposeData() { Scribe_Values.Look(ref nextYak, "ravNextYak", 1); Scribe_Values.Look(ref nextMig, "ravNextMig", 1); }
}

public class Dialog_RenameAircraft : Dialog_Rename<Aircraft>
{
    public Dialog_RenameAircraft(Aircraft plane) : base(plane) { }
}
