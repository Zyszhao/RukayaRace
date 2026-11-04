using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.AirDefense
{
    // Delay the whole raid worker, so neither pods, letters nor lords are created early.
    // Storyteller accounting remains on the original execution; replay calls only its worker.
    public sealed class MapComponent_RukayaSAMRaids : MapComponent
    {
        public const int PostExitDelayTicks = 180;
        private List<PendingSAMRaid> pendingRaids = new List<PendingSAMRaid>();
        [ThreadStatic] private static IncidentParms replayingParms;

        public MapComponent_RukayaSAMRaids(Map map) : base(map) { }

        internal static bool IsReplaying(IncidentParms parms) => ReferenceEquals(replayingParms, parms);

        internal void Enqueue(IncidentWorker_Raid worker, IncidentParms parms, Thing_RukayaSAMMissile missile)
        {
            int arrivalTick = Find.TickManager.TicksGame + missile.RemainingFlightTicks + PostExitDelayTicks;
            pendingRaids.Add(new PendingSAMRaid
            {
                incident = worker.def,
                parms = parms.ShallowCopy(),
                arrivalTick = arrivalTick
            });
            if (Prefs.DevMode)
                Log.Message($"[Rukaya SAM] Intercepted raid queued: mode={parms.raidArrivalMode.defName}, arrivalTick={arrivalTick}, postExitDelay={PostExitDelayTicks}.");
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < pendingRaids.Count;)
            {
                PendingSAMRaid pending = pendingRaids[i];
                if (now < pending.arrivalTick)
                {
                    i++;
                    continue;
                }
                // Remove before execution: partial arrivals must never be replayed twice.
                pendingRaids.RemoveAt(i);
                if (!(pending.incident?.Worker is IncidentWorker_Raid worker) || pending.parms?.target != map)
                {
                    Log.Error("[Rukaya SAM] Delayed raid lost its worker or target map.");
                    continue;
                }
                IncidentParms previous = replayingParms;
                replayingParms = pending.parms;
                try
                {
                    bool arrived = (bool)AccessTools.Method(worker.GetType(), "TryExecuteWorker")
                        .Invoke(worker, new object[] { pending.parms });
                    if (!arrived)
                        Log.Warning("[Rukaya SAM] Delayed raid could not arrive.");
                }
                catch (Exception ex)
                {
                    Log.Error("[Rukaya SAM] Delayed raid execution failed.\n" + ex);
                }
                finally { replayingParms = previous; }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pendingRaids, "rukayaSAMRaids", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pendingRaids == null)
                pendingRaids = new List<PendingSAMRaid>();
        }
    }

    public sealed class PendingSAMRaid : IExposable
    {
        public IncidentDef incident;
        public IncidentParms parms;
        public int arrivalTick;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref incident, "incident");
            Scribe_Deep.Look(ref parms, "parms");
            Scribe_Values.Look(ref arrivalTick, "arrivalTick");
        }
    }

    // Intercept the enemy override, including its raid statistics and speed signal.
    // Deferring only the base worker would execute that wrapper twice.
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_IncidentWorker_Raid_TryExecuteWorker_RukayaSAMDelay
    {
        [HarmonyPrefix]
        public static bool Prefix(IncidentWorker_Raid __instance, IncidentParms parms, ref bool __result)
        {
            if (!(__instance is IncidentWorker_RaidEnemy) || !(parms?.target is Map map)
                || __instance.def == null || MapComponent_RukayaSAMRaids.IsReplaying(parms))
                return true;
            CompRukayaSAMLauncher launcher = CompRukayaSAMLauncher.FindReady(map);
            if (launcher == null)
                return true;

            // These are vanilla's idempotent planning steps; use the original parms so
            // storyteller strategy hooks keep their current execution scope.
            AccessTools.Method(__instance.GetType(), "ResolveRaidPoints").Invoke(__instance, new object[] { parms });
            if (!(bool)AccessTools.Method(__instance.GetType(), "TryResolveRaidFaction")
                .Invoke(__instance, new object[] { parms }))
                return true;
            __instance.ResolveRaidStrategy(parms, parms.pawnGroupKind ?? PawnGroupKindDefOf.Combat);
            if (parms.raidArrivalMode == null && !__instance.TryResolveRaidArriveMode(parms))
                return true;
            if (parms.faction == null || !parms.faction.HostileTo(Faction.OfPlayer)
                || !Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM.IsDropArrival(parms.raidArrivalMode))
                return true;

            MapComponent_RukayaSAMRaids queue = map.GetComponent<MapComponent_RukayaSAMRaids>();
            if (queue == null || !launcher.TryLaunch(out Thing_RukayaSAMMissile missile))
                return true;
            queue.Enqueue(__instance, parms, missile);
            __result = true;
            return false;
        }
    }
}
