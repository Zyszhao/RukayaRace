using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RukayaRace.Flight;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RukayaAviation;

// Holds airborne raiders aboard a flying An-72 until it reaches the drop zone, then deploys
// each pawn from the rear cargo ramp via Skyfaller_RukayaAirborneDrop.
public class PendingAirborneDrop : IExposable
{
    public Pawn pawn;
    public IntVec3 dropCell;
    public int deployDelayTicks;
    public bool deployed;

    public void ExposeData()
    {
        Scribe_References.Look(ref pawn, "pawn");
        Scribe_Values.Look(ref dropCell, "dropCell");
        Scribe_Values.Look(ref deployDelayTicks, "deployDelayTicks");
        Scribe_Values.Look(ref deployed, "deployed");
    }
}

public partial class Aircraft
{
    public bool airborneRaid;
    public Vector3 airborneDropPoint;
    public bool airborneDropStarted;
    public int airborneDropTicks;
    public bool airborneShotDownHandled;
    public List<PendingAirborneDrop> airborneDrops = new();
    public int airborneStartDelayTicks;
    internal float airborneFormationSpeed;

    private void TickAirborneRaid()
    {
        if (airborneDrops == null) airborneDrops = new();

        if (airborneStartDelayTicks > 0)
        {
            airborneStartDelayTicks--;
            return;
        }

        float step = (airborneFormationSpeed > 0f ? airborneFormationSpeed : Speed) / 60f;
        if (!airborneDropStarted)
        {
            Vector3 target = airborneDropPoint;
            float dist = AV.Distance(realPos, target);
            if (dist > 0.05f)
            {
                realPos = Vector3.MoveTowards(realPos, target, step);
                SetPositionSafely();
            }
            // Start deploying as the transport enters the front edge of the drop zone (~8 cells before drop point).
            if (dist <= 8f)
            {
                airborneDropStarted = true;
                airborneDropTicks = 0;
                if (airborneFormationSpeed > 0f) raidExiting = true;
                else BeginAirborneExit();
            }
            return;
        }

        // Continue straight across the map along the locked formation heading while deploying paratroopers.
        airborneDropTicks++;
        if (airborneDrops.Count > 0)
        {
            bool anyRemaining = false;
            for (int i = 0; i < airborneDrops.Count; i++)
            {
                PendingAirborneDrop entry = airborneDrops[i];
                if (entry == null || entry.deployed) continue;
                if (airborneDropTicks >= entry.deployDelayTicks)
                {
                    DeployAirbornePawn(entry);
                    entry.deployed = true;
                }
                else
                {
                    anyRemaining = true;
                }
            }
            if (!anyRemaining) airborneDrops.Clear();
        }

        Vector3 exitTarget = attackExit;
        realPos = Vector3.MoveTowards(realPos, exitTarget, step);
        SetPositionSafely();
        if (AV.Distance(realPos, exitTarget) <= 0.5f)
        {
            FlushRemainingAirborneDrops();
            Destroy(DestroyMode.Vanish);
        }
    }

    private void BeginAirborneExit()
    {
        Vector3 d = AV.Dir(yaw);
        float distance = 10000f;
        if (d.x > 0.001f) distance = Math.Min(distance, (Map.Size.x - 5.5f - realPos.x) / d.x);
        if (d.x < -0.001f) distance = Math.Min(distance, (5.5f - realPos.x) / d.x);
        if (d.z > 0.001f) distance = Math.Min(distance, (Map.Size.z - 5.5f - realPos.z) / d.z);
        if (d.z < -0.001f) distance = Math.Min(distance, (5.5f - realPos.z) / d.z);
        attackExit = realPos + d * Mathf.Max(0f, distance);
        raidExiting = true;
    }

    private Vector3 AirborneRampOrigin()
    {
        // An-72 rear cargo ramp exit: aft along flight axis plus the aircraft's current visual altitude lift.
        Vector3 rampGround = realPos - AV.Dir(yaw) * (def.graphicData.drawSize.y * 0.36f);
        rampGround.y = 0f;
        return new Vector3(rampGround.x, 0f, rampGround.z + Mathf.Max(0f, visualLift) * 1.5f);
    }

    private Vector3 AirborneRampGroundPos()
    {
        Vector3 rampGround = realPos - AV.Dir(yaw) * (def.graphicData.drawSize.y * 0.36f);
        rampGround.y = 0f;
        return rampGround;
    }

    private void DeployAirbornePawn(PendingAirborneDrop entry)
    {
        if (entry?.pawn == null || entry.pawn.Dead || entry.pawn.Destroyed) return;
        Pawn pawn = entry.pawn;
        Cargo?.innerContainer?.Remove(pawn);

        Vector3 rampGround = AirborneRampGroundPos();
        Vector3 rampOrigin = AirborneRampOrigin();
        Vector3 forward = AV.Dir(yaw);

        IntVec3 cell = entry.dropCell;
        // Keep each drop cell close ahead/under the transport's flight corridor (4~14 cells from ramp).
        float dropDist = cell.IsValid ? AV.Distance(rampGround, AV.Flat(cell)) : 999f;
        if (!cell.IsValid || !cell.InBounds(Map) || !RukayaAirborneUtility.IsGoodLandingCell(cell, Map) || dropDist > 18f)
        {
            IntVec3 preferredCenter = (rampGround + forward * Rand.Range(4f, 11f)).ToIntVec3().ClampInsideMap(Map);
            if (!RukayaAirborneUtility.TryFindLandingCell(Map, preferredCenter, out cell, radius: 6))
            {
                if (!RukayaAirborneUtility.TryFindLandingCell(Map, airborneDropPoint.ToIntVec3(), out cell, radius: 14))
                    cell = Position.ClampInsideMap(Map);
            }
        }

        CompRukayaFlightControl flightComp = pawn.GetComp<CompRukayaFlightControl>();
        bool canWingGlide = flightComp != null && flightComp.BothWings;

        Vector3 glide = AV.Flat(cell) - rampGround;
        if (glide.sqrMagnitude < 0.25f)
            glide = forward + new Vector3(-forward.z, 0f, forward.x) * Rand.Range(-0.25f, 0.25f);
        Rot4 facing = Mathf.Abs(glide.x) >= Mathf.Abs(glide.z)
            ? (glide.x >= 0f ? Rot4.East : Rot4.West)
            : (glide.z >= 0f ? Rot4.North : Rot4.South);
        ThingDef flyerDef = DefDatabase<ThingDef>.GetNamed("Rukaya_AirborneRaidFlyer");
        Skyfaller_RukayaAirborneDrop flyer = (Skyfaller_RukayaAirborneDrop)SkyfallerMaker.MakeSkyfaller(flyerDef, pawn);
        flyer.InitializeLaunch(rampOrigin, rampGround, !canWingGlide, facing);
        GenSpawn.Spawn(flyer, cell, Map, facing);

        if (canWingGlide)
        {
            AnimationDef flapAnim = Pawn_FlightTracker.GetBestFlyAnimation(pawn, facing);
            if (flapAnim != null && pawn.Drawer?.renderer != null)
            {
                pawn.Drawer.renderer.SetAnimation(flapAnim);
                if (pawn.Drawer.renderer.renderTree != null && flapAnim.durationTicks > 0)
                    pawn.Drawer.renderer.renderTree.animationStartTick -= Rand.Range(0, flapAnim.durationTicks);
            }
        }
    }

    private void FlushRemainingAirborneDrops()
    {
        if (airborneDrops == null || airborneDrops.Count == 0) return;
        for (int i = 0; i < airborneDrops.Count; i++)
        {
            PendingAirborneDrop entry = airborneDrops[i];
            if (entry != null && !entry.deployed)
            {
                DeployAirbornePawn(entry);
                entry.deployed = true;
            }
        }
        airborneDrops.Clear();
    }

    internal void HandleAirborneTransportShotDown()
    {
        if (!airborneRaid || airborneShotDownHandled || !Spawned || Map == null) return;
        airborneShotDownHandled = true;

        List<Pawn> onboard = new();
        if (airborneDrops != null)
        {
            for (int i = 0; i < airborneDrops.Count; i++)
            {
                PendingAirborneDrop entry = airborneDrops[i];
                if (entry == null || entry.deployed || entry.pawn == null || entry.pawn.Dead || entry.pawn.Destroyed) continue;
                onboard.Add(entry.pawn);
                entry.deployed = true;
            }
            airborneDrops.Clear();
        }
        if (Cargo?.innerContainer != null)
        {
            for (int i = 0; i < Cargo.innerContainer.Count; i++)
            {
                if (Cargo.innerContainer[i] is Pawn p && !p.Dead && !p.Destroyed && !onboard.Contains(p))
                    onboard.Add(p);
            }
        }

        if (onboard.Count == 0)
        {
            Messages.Message("敌方空降运输机已被击落！", this, MessageTypeDefOf.PositiveEvent);
            return;
        }

        // Only a small fraction (~25%, between 20% and 35%; at least 1 if >=3 onboard) manage to bail out in time.
        int minSurvivors = onboard.Count >= 3 ? 1 : 0;
        int survivorCount = Mathf.RoundToInt(onboard.Count * Rand.Range(0.20f, 0.35f));
        if (survivorCount < minSurvivors) survivorCount = minSurvivors;
        survivorCount = Mathf.Clamp(survivorCount, 0, onboard.Count);

        // Prefer winged Rukayas who can bail out and flap to safety, shuffling within groups.
        List<Pawn> winged = onboard.Where(p => p.GetComp<CompRukayaFlightControl>()?.BothWings == true).InRandomOrder().ToList();
        List<Pawn> others = onboard.Where(p => !winged.Contains(p)).InRandomOrder().ToList();
        List<Pawn> bailoutOrder = new(onboard.Count);
        bailoutOrder.AddRange(winged);
        bailoutOrder.AddRange(others);

        int escaped = 0;
        IntVec3 crashCenter = Position.ClampInsideMap(Map);
        Vector3 forward = AV.Dir(yaw);
        for (int i = 0; i < survivorCount; i++)
        {
            Pawn pawn = bailoutOrder[i];
            if (pawn == null || pawn.Dead || pawn.Destroyed) continue;
            // Bailout shock/bruising before launching out of the burning cabin.
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(8f, 16f)));
            if (pawn.Dead || pawn.Destroyed) continue;

            IntVec3 nearCrash = (realPos + forward * Rand.Range(2f, 12f)).ToIntVec3().ClampInsideMap(Map);
            if (!RukayaAirborneUtility.TryFindLandingCell(Map, nearCrash, out IntVec3 cell, radius: 12))
                cell = crashCenter;

            PendingAirborneDrop emergencyDrop = new PendingAirborneDrop
            {
                pawn = pawn,
                dropCell = cell,
                deployDelayTicks = 0,
                deployed = true
            };
            DeployAirbornePawn(emergencyDrop);
            escaped++;
        }

        Messages.Message(escaped > 0
            ? $"敌方空降运输机已被击落，仅有 {escaped} 名空降兵成功跳伞逃生！"
            : "敌方空降运输机已被击落，机上空降兵全数坠毁！",
            this, MessageTypeDefOf.PositiveEvent);
    }

    internal void DestroyRemainingAirborneOccupants()
    {
        airborneDrops?.Clear();
        if (Cargo?.innerContainer == null || Cargo.innerContainer.Count == 0) return;
        List<Thing> remaining = Cargo.innerContainer.ToList();
        for (int i = 0; i < remaining.Count; i++)
        {
            Thing t = remaining[i];
            if (t == null || t.Destroyed) continue;
            Cargo.innerContainer.Remove(t);
            if (t is Pawn pawn)
            {
                if (!pawn.Dead)
                    pawn.Kill(new DamageInfo(DamageDefOf.Bomb, 200f, 1f));
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
            else
            {
                t.Destroy(DestroyMode.Vanish);
            }
        }
    }

    private void ExposeAirborneRaid()
    {
        Scribe_Values.Look(ref airborneRaid, "ravAirborneRaid");
        Scribe_Values.Look(ref airborneDropPoint, "ravAirborneDropPoint");
        Scribe_Values.Look(ref airborneDropStarted, "ravAirborneDropStarted");
        Scribe_Values.Look(ref airborneDropTicks, "ravAirborneDropTicks");
        Scribe_Values.Look(ref airborneShotDownHandled, "ravAirborneShotDownHandled");
        Scribe_Values.Look(ref airborneStartDelayTicks, "ravAirborneStartDelayTicks");
        Scribe_Values.Look(ref airborneFormationSpeed, "ravAirborneFormationSpeed");
        Scribe_Collections.Look(ref airborneDrops, "ravAirborneDrops", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) airborneDrops ??= new();
    }
}

// Excludes mechanoid groups when generating a Rukaya_Airborne raid, and zeroes out vanilla drop-pod
// arrival mode weights for both Rukaya factions on surface maps so their drop raids use An-72 transports.
[HarmonyPatch(typeof(PawnsArrivalModeWorker), nameof(PawnsArrivalModeWorker.GetSelectionWeight))]
public static class Patch_PawnsArrivalModeWorker_GetSelectionWeight_RukayaAirborne
{
    private static bool IsVanillaDropPodMode(PawnsArrivalModeDef def)
    {
        if (def == null) return false;
        return def.defName == "EdgeDrop"
            || def.defName == "EdgeDropGroups"
            || def.defName == "CenterDrop"
            || def.defName == "RandomDrop";
    }

    [HarmonyPostfix]
    public static void Postfix(PawnsArrivalModeWorker __instance, IncidentParms parms, ref float __result)
    {
        if (__result <= 0f || parms?.faction == null) return;
        if (!RukayaAirborneUtility.IsRukayaFaction(parms.faction)) return;
        if (!IsVanillaDropPodMode(__instance?.def)) return;
        // On surface maps, both Rukaya factions use An-72 airborne drops instead of vanilla drop pods.
        if (parms.target is Map map && map.Tile.Valid && map.Tile.LayerDef.isSpace) return;
        __result = 0f;
    }
}

public static class RukayaAirborneUtility
{
    public const string FactionDefName = "Rukaya_DrifterAlliance";
    public const string BrokenStarFactionDefName = "Rukaya_BrokenStarCorps";

    public static bool IsRukayaFaction(Faction faction) => faction?.def?.defName == FactionDefName
        || faction?.def?.defName == BrokenStarFactionDefName;

    public static bool IsGoodLandingCell(IntVec3 cell, Map map)
    {
        return cell.InBounds(map)
            && cell.Standable(map)
            && !cell.Roofed(map)
            && !cell.Fogged(map)
            && !cell.ContainsStaticFire(map)
            && cell.GetEdifice(map) == null;
    }

    public static bool TryFindLandingCell(Map map, IntVec3 center, out IntVec3 cell, int radius = 14)
    {
        if (CellFinder.TryFindRandomCellNear(center, map, radius,
            c => IsGoodLandingCell(c, map) && c.GetFirstPawn(map) == null, out cell))
            return true;
        if (CellFinder.TryFindRandomCellNear(center, map, radius + 12,
            c => IsGoodLandingCell(c, map), out cell))
            return true;
        if (DropCellFinder.TryFindDropSpotNear(center, map, out cell, allowFogged: false, canRoofPunch: false, allowIndoors: false))
            return true;
        cell = center.ClampInsideMap(map);
        return cell.InBounds(map);
    }

    public static void LaunchAirborneTransports(Map map, Faction faction, IntVec3 dropCenter, List<Pawn> pawns)
    {
        if (map == null || pawns == null || pawns.Count == 0) return;

        // Split large raids into multiple An-72 transports (up to 14 raiders per plane) flying in strict parallel formation.
        const int maxPerPlane = 14;
        int planeCount = Math.Max(1, Mathf.CeilToInt(pawns.Count / (float)maxPerPlane));

        // Pick a cardinal edge so every plane shares an identical axis-aligned heading across the entire map.
        int edge = Rand.RangeInclusive(0, 3); // 0=West->East, 1=East->West, 2=South->North, 3=North->South
        Vector3 forward = edge switch
        {
            0 => new Vector3(1f, 0f, 0f),
            1 => new Vector3(-1f, 0f, 0f),
            2 => new Vector3(0f, 0f, 1f),
            _ => new Vector3(0f, 0f, -1f)
        };
        Vector3 lateral = new Vector3(-forward.z, 0f, forward.x);
        float formationYaw = forward.AngleFlat();

        bool horizontalFlight = edge == 0 || edge == 1;
        float lateralMinBound = 9f;
        float lateralMaxBound = (horizontalFlight ? map.Size.z : map.Size.x) - 10f;
        float axialMinBound = 16f;
        float axialMaxBound = (horizontalFlight ? map.Size.x : map.Size.z) - 17f;

        Vector3 dropVec = AV.Flat(dropCenter);
        float baseLateral = horizontalFlight
            ? Mathf.Clamp(dropVec.z, lateralMinBound, lateralMaxBound)
            : Mathf.Clamp(dropVec.x, lateralMinBound, lateralMaxBound);
        float baseDropAxial = horizontalFlight
            ? Mathf.Clamp(dropVec.x, axialMinBound, axialMaxBound)
            : Mathf.Clamp(dropVec.z, axialMinBound, axialMaxBound);

        const float slotLateralSpacing = 10f;
        const float rowLag = 11.5f;
        const float pairLateralSpacing = 11.5f;
        const float pairLag = 7.5f;
        float minSlot = planeCount == 2 ? -0.5f : -((planeCount - 1) / 2);
        float maxSlot = planeCount == 2 ? 0.5f : planeCount / 2;
        float lateralSign = horizontalFlight ? lateral.z : lateral.x;
        float minMapSlot = Mathf.Min(minSlot * lateralSign, maxSlot * lateralSign);
        float maxMapSlot = Mathf.Max(minSlot * lateralSign, maxSlot * lateralSign);
        float availableSpan = Mathf.Max(0f, lateralMaxBound - lateralMinBound);
        float spacing = planeCount > 1
            ? Mathf.Min(planeCount == 2 ? pairLateralSpacing : slotLateralSpacing, availableSpan / (maxMapSlot - minMapSlot))
            : 0f;
        float formationCenterLateral = Mathf.Clamp(baseLateral, lateralMinBound - minMapSlot * spacing, lateralMaxBound - maxMapSlot * spacing);

        float entryAxial = edge switch
        {
            0 => -6f,
            1 => map.Size.x + 5f,
            2 => -6f,
            _ => map.Size.z + 5f
        };
        float exitAxial = edge switch
        {
            0 => map.Size.x + 6f,
            1 => -7f,
            2 => map.Size.z + 6f,
            _ => -7f
        };

        Vector3 baseEntry = horizontalFlight
            ? new Vector3(entryAxial, 0f, formationCenterLateral)
            : new Vector3(formationCenterLateral, 0f, entryAxial);
        Vector3 baseDrop = horizontalFlight
            ? new Vector3(baseDropAxial, 0f, formationCenterLateral)
            : new Vector3(formationCenterLateral, 0f, baseDropAxial);
        Vector3 baseExit = horizontalFlight
            ? new Vector3(exitAxial, 0f, formationCenterLateral)
            : new Vector3(formationCenterLateral, 0f, exitAxial);

        float formationSpeed = 0f;

        for (int pIdx = 0; pIdx < planeCount; pIdx++)
        {
            int startIdx = pIdx * maxPerPlane;
            int count = Math.Min(maxPerPlane, pawns.Count - startIdx);
            if (count <= 0) break;

            int row = (pIdx + 1) / 2;
            float slot = planeCount == 2 ? pIdx - 0.5f : pIdx % 2 == 1 ? row : -row;
            float axialLag = planeCount == 2 ? pIdx * pairLag : row * rowLag;
            float sideOffset = slot * spacing;
            Vector3 lateralShift = lateral * sideOffset;

            // Every plane uses the exact same lateralShift on entry, drop, and exit + identical formationYaw.
            Vector3 planeEntry = baseEntry + lateralShift;
            Vector3 planeDrop = baseDrop + lateralShift;
            Vector3 planeExit = baseExit + lateralShift;

            Aircraft plane = (Aircraft)ThingMaker.MakeThing(AV.Def("An72"));
            plane.SetFaction(faction);
            int halfSize = Mathf.Max(plane.def.size.x, plane.def.size.z) / 2 + 1;
            IntVec3 spawnCell = new IntVec3(Mathf.RoundToInt(planeEntry.x), 0, Mathf.RoundToInt(planeEntry.z));
            spawnCell.x = edge switch
            {
                0 => halfSize,
                1 => map.Size.x - 1 - halfSize,
                _ => Mathf.Clamp(spawnCell.x, halfSize, map.Size.x - 1 - halfSize)
            };
            spawnCell.z = edge switch
            {
                2 => halfSize,
                3 => map.Size.z - 1 - halfSize,
                _ => Mathf.Clamp(spawnCell.z, halfSize, map.Size.z - 1 - halfSize)
            };
            if (!GenAdj.OccupiedRect(spawnCell, Rot4.North, plane.def.size).InBounds(map))
                spawnCell = map.Center;
            plane.realPos = planeEntry;
            plane.yaw = formationYaw;
            plane.Rotation = Rot4.FromAngleFlat(formationYaw);
            plane.State = FlightState.Flying;
            plane.raid = true;
            plane.airborneRaid = true;
            plane.airborneDropPoint = planeDrop;
            plane.attackExit = planeExit;
            plane.airborneDropStarted = false;
            plane.airborneDropTicks = 0;
            plane.airborneShotDownHandled = false;
            plane.fuel = plane.MaxFuel;
            plane.raidDuration = 12000;
            plane.ResetFlightVisuals();
            plane.Refuelable.SyncGroundAccess();

            plane.airborneDrops.Clear();
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = pawns[startIdx + i];
                if (pawn != null) plane.Cargo.innerContainer.TryAddOrTransfer(pawn, canMergeWithExistingStacks: false);
            }
            if (pIdx == 0) formationSpeed = plane.Speed;
            plane.airborneFormationSpeed = formationSpeed;
            float speedPerTick = formationSpeed / 60f;
            int spawnTick = Find.TickManager.TicksGame + Mathf.RoundToInt(axialLag / speedPerTick);
            plane.airborneStartDelayTicks = 0;
            float rampAft = plane.def.graphicData.drawSize.y * 0.36f;
            float distanceToDrop = AV.Distance(planeEntry, planeDrop);
            int ticksToDropStart = Mathf.CeilToInt(Mathf.Max(0f, distanceToDrop - 8f) / speedPerTick) + 1;

            for (int i = 0; i < count; i++)
            {
                Pawn pawn = pawns[startIdx + i];
                if (pawn == null) continue;
                int delayTicks = i * 4;
                // Predict transport ramp position at release tick and place each landing cell 4~12 cells ahead/underneath.
                Vector3 predictedPlanePos = planeEntry + forward * ((ticksToDropStart + Mathf.Max(0, delayTicks - 1)) * speedPerTick);
                Vector3 predictedRampGround = predictedPlanePos - forward * rampAft;
                Vector3 targetSpot = predictedRampGround
                    + forward * Rand.Range(4f, 12f)
                    + lateral * Rand.Range(-3.5f, 3.5f);
                IntVec3 preferredCenter = targetSpot.ToIntVec3().ClampInsideMap(map);
                if (!TryFindLandingCell(map, preferredCenter, out IntVec3 cell, radius: 5))
                    TryFindLandingCell(map, planeDrop.ToIntVec3().ClampInsideMap(map), out cell, radius: 12);

                plane.airborneDrops.Add(new PendingAirborneDrop
                {
                    pawn = pawn,
                    dropCell = cell,
                    deployDelayTicks = delayTicks,
                    deployed = false
                });
            }
            if (pIdx == 0)
            {
                GenSpawn.Spawn(plane, spawnCell, map, Rot4.FromAngleFlat(formationYaw));
                plane.realPos = planeEntry;
                plane.yaw = formationYaw;
                plane.ResetFlightVisuals();
                plane.Refuelable.SyncGroundAccess();
            }
            else
            {
                map.GetComponent<AirportMap>().QueueAirborneTransport(plane, spawnTick, spawnCell, planeEntry, formationYaw);
            }
        }
    }
}

public class Skyfaller_RukayaAirborneDrop : Skyfaller_FlyingPawn
{
    private Vector3 launchOrigin;
    private Vector3 launchGroundPos;
    private bool hasLaunchOrigin;
    private int initialTicks;
    private bool hardLanding;
    private Rot4 facing = Rot4.South;

    public void InitializeLaunchOrigin(Vector3 origin)
    {
        InitializeLaunch(origin, new Vector3(origin.x, 0f, origin.z), false);
    }

    public void InitializeLaunch(Vector3 origin, Vector3 groundOrigin, bool nonWingedHardLanding)
        => InitializeLaunch(origin, groundOrigin, nonWingedHardLanding, Rot4.South);

    public void InitializeLaunch(Vector3 origin, Vector3 groundOrigin, bool nonWingedHardLanding, Rot4 launchFacing)
    {
        launchOrigin = origin;
        launchGroundPos = new Vector3(groundOrigin.x, 0f, groundOrigin.z);
        hasLaunchOrigin = true;
        hardLanding = nonWingedHardLanding;
        facing = launchFacing;
        Rotation = facing;
        if (Pawn is Pawn pawn) pawn.Rotation = facing;
        // ticksToImpact is rolled inside Skyfaller.SpawnSetup; capture it there when <= 1.
        initialTicks = ticksToImpact > 1 ? ticksToImpact : 0;
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        Rotation = facing;
        if (!respawningAfterLoad || initialTicks <= 1)
            initialTicks = Mathf.Max(1, ticksToImpact);
    }

    private float Progress => Mathf.Clamp01(1f - (float)ticksToImpact / Mathf.Max(1, initialTicks));

    private Vector3 CurrentGroundPos()
    {
        Vector3 dest = Position.ToVector3Shifted();
        dest.y = 0f;
        if (!hasLaunchOrigin) return dest;
        float t = Progress;
        float horizT = 1f - (1f - t) * (1f - t);
        Vector3 ground = Vector3.Lerp(launchGroundPos, dest, horizT);
        ground.y = 0f;
        return ground;
    }

    protected override void HitRoof() { }

    protected override void GetDrawPositionAndRotation(ref Vector3 drawLoc, out float extraRotation)
    {
        if (!hasLaunchOrigin)
        {
            base.GetDrawPositionAndRotation(ref drawLoc, out extraRotation);
            return;
        }

        float t = Progress;
        Vector3 dest = Position.ToVector3Shifted();
        float horizT = 1f - (1f - t) * (1f - t);

        float x = Mathf.Lerp(launchOrigin.x, dest.x, horizT);
        // Smooth descent from aircraft rear ramp altitude (launchOrigin.z includes aircraft visualLift * 1.5f)
        // down to ground destination z, with a subtle glide arc that decays smoothly to 0 at touchdown.
        float heightArc = Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.9f;
        float z = Mathf.Lerp(launchOrigin.z, dest.z, horizT) + heightArc;

        // Draw below the transport aircraft when leaving the rear ramp, then settle onto Skyfaller/Pawn layer.
        float highAlt = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) - 0.01f;
        float lowAlt = Altitudes.AltitudeFor(AltitudeLayer.Skyfaller);
        float y = Mathf.Lerp(highAlt, lowAlt, Mathf.SmoothStep(0f, 1f, t));

        drawLoc = new Vector3(x, y, z);
        extraRotation = 0f;
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        Pawn pawn = Pawn;
        if (pawn == null) return;
        pawn.Rotation = facing;

        if (!hardLanding && pawn.Drawer?.renderer != null)
        {
            AnimationDef wanted = Pawn_FlightTracker.GetBestFlyAnimation(pawn, facing);
            if (wanted != null && pawn.Drawer.renderer.CurAnimation != wanted)
                pawn.Drawer.renderer.SetAnimation(wanted);
        }

        Vector3 pos = drawLoc;
        GetDrawPositionAndRotation(ref pos, out _);
        pawn.Drawer.renderer.RenderPawnAt(pos, facing, neverAimWeapon: true);
        DrawDropSpotShadow();
    }

    protected override void DrawDropSpotShadow()
    {
        if (!hasLaunchOrigin)
        {
            base.DrawDropSpotShadow();
            return;
        }

        Material shadowMat = ShadowMaterial;
        if (shadowMat == null) return;

        Vector3 ground = CurrentGroundPos();
        ground.y = Altitudes.AltitudeFor(AltitudeLayer.Shadows);
        Skyfaller.DrawDropSpotShadow(ground, Rotation, shadowMat, def.skyfaller.shadowSize, ticksToImpact);
    }

    protected override void Impact()
    {
        if (!RukayaAirborneUtility.IsGoodLandingCell(Position, Map))
        {
            if (RukayaAirborneUtility.TryFindLandingCell(Map, Position, out IntVec3 cell, radius: 10))
                Position = cell;
        }
        base.Impact();
    }

    protected override void SpawnThings()
    {
        Pawn pawn = Pawn;
        if (pawn == null) return;
        innerContainer.Remove(pawn);
        pawn.Drawer?.renderer?.SetAnimation(null);
        GenSpawn.Spawn(pawn, Position, Map, facing);
        if (hardLanding && !pawn.Dead && !pawn.Destroyed)
        {
            // Non-winged or wing-damaged troop made a rough low-altitude drop from the transport ramp.
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(6f, 12f)));
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref launchOrigin, "ravLaunchOrigin");
        Scribe_Values.Look(ref launchGroundPos, "ravLaunchGroundPos");
        Scribe_Values.Look(ref hasLaunchOrigin, "ravHasLaunchOrigin");
        Scribe_Values.Look(ref initialTicks, "ravInitialTicks");
        Scribe_Values.Look(ref hardLanding, "ravHardLanding");
        Scribe_Values.Look(ref facing, "ravAirborneFacing", Rot4.South);
    }
}

public class PawnsArrivalModeWorker_RukayaAirborne : PawnsArrivalModeWorker
{
    public override bool CanUseWith(IncidentParms parms)
    {
        if (!base.CanUseWith(parms)) return false;
        if (parms?.faction == null || !RukayaAirborneUtility.IsRukayaFaction(parms.faction)) return false;
        if (parms.target is Map map && map.Tile.Valid && map.Tile.LayerDef.isSpace) return false;
        return true;
    }

    public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
    {
        Map map = (Map)parms.target;
        if (parms.spawnCenter.IsValid) return true;

        // Modelled on vanilla drop raids: prefer an open drop zone near the colony, falling back to distant drop center.
        if (Rand.Chance(0.65f) && DropCellFinder.TryFindRaidDropCenterClose(out IntVec3 closeSpot, map,
            canRoofPunch: false, allowIndoors: false, closeWalk: true, maxRadius: 45))
        {
            parms.spawnCenter = closeSpot;
            return true;
        }

        parms.spawnCenter = DropCellFinder.FindRaidDropCenterDistant(map, allowRoofed: false);
        return parms.spawnCenter.IsValid;
    }

    public override void Arrive(List<Pawn> pawns, IncidentParms parms)
    {
        Map map = (Map)parms.target;
        if (map == null || pawns == null || pawns.Count == 0) return;

        IntVec3 center = parms.spawnCenter;
        if (!center.IsValid)
        {
            if (!TryResolveRaidSpawnCenter(parms))
                center = DropCellFinder.RandomDropSpot(map, false);
            else
                center = parms.spawnCenter;
        }

        RukayaAirborneUtility.LaunchAirborneTransports(map, parms.faction, center, pawns);
    }
}

// Tracks active Rukaya_Airborne raid generation so PawnGroupMaker selection excludes mechanoid groups,
// ensuring An-72 airborne raids deploy winged Rukaya troops rather than heavy ground mechanoids.
[HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
public static class Patch_IncidentWorker_Raid_TryGenerateRaidInfo_RukayaAirborne
{
    [ThreadStatic]
    private static int activeAirborneDepth;

    internal static bool IsGeneratingAirborneRaid => activeAirborneDepth > 0;

    [HarmonyPrefix]
    public static void Prefix(IncidentParms parms, out bool __state)
    {
        __state = parms?.raidArrivalMode?.defName == "Rukaya_Airborne"
            && RukayaAirborneUtility.IsRukayaFaction(parms.faction);
        if (__state) activeAirborneDepth++;
    }

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state && activeAirborneDepth > 0) activeAirborneDepth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.TryGetRandomPawnGroupMaker))]
public static class Patch_PawnGroupMakerUtility_TryGetRandomPawnGroupMaker_RukayaAirborne
{
    private static bool ContainsMechOption(PawnGroupMaker maker)
    {
        if (maker?.options == null) return false;
        for (int i = 0; i < maker.options.Count; i++)
        {
            PawnKindDef kind = maker.options[i]?.kind;
            if (kind == null) continue;
            if (kind.defName != null && kind.defName.StartsWith("Rukaya_MechChassis_", StringComparison.Ordinal))
                return true;
            if (kind.RaceProps != null && kind.RaceProps.IsMechanoid)
                return true;
        }
        return false;
    }

    [HarmonyPrefix]
    public static bool Prefix(PawnGroupMakerParms parms, ref PawnGroupMaker pawnGroupMaker, bool ignoreCommonality, ref bool __result)
    {
        if (!Patch_IncidentWorker_Raid_TryGenerateRaidInfo_RukayaAirborne.IsGeneratingAirborneRaid)
            return true;
        if (parms?.faction == null || !RukayaAirborneUtility.IsRukayaFaction(parms.faction))
            return true;
        if (parms.faction.def?.pawnGroupMakers == null)
            return true;

        List<PawnGroupMaker> wingedMakers = parms.faction.def.pawnGroupMakers
            .Where(gm => gm.kindDef == parms.groupKind && gm.CanGenerateFrom(parms) && !ContainsMechOption(gm))
            .ToList();
        if (wingedMakers.Count == 0)
            return true;

        if (ignoreCommonality)
            pawnGroupMaker = wingedMakers.RandomElement();
        else
            pawnGroupMaker = wingedMakers.RandomElementByWeight(gm => gm.commonality);

        __result = pawnGroupMaker != null;
        return false;
    }
}
