using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.AirDefense
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    public static class Patch_IncidentWorker_Raid_TryGenerateRaidInfo_RukayaSAM
    {
        [HarmonyPrefix]
        public static void Prefix(IncidentWorker_Raid __instance, IncidentParms parms, bool debugTest, out int __state)
        {
            __state = Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM.Begin(__instance, parms, debugTest);
        }

        [HarmonyPostfix]
        public static void Postfix(IncidentParms parms, bool __result, int __state)
        {
            Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM.Complete(parms, __result, __state);
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, IncidentParms parms, int __state)
        {
            if (__exception != null && __state > 0
                && Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM.HasActiveDepth(__state))
                Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM.Complete(parms, false, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.AdjustedRaidPoints))]
    public static class Patch_IncidentWorker_Raid_AdjustedRaidPoints_RukayaSAM
    {
        [HarmonyPostfix]
        public static void Postfix(PawnsArrivalModeDef raidArrivalMode, Faction faction,
            IIncidentTarget target, ref float __result)
        {
            if (activeRaids == null || activeRaids.Count == 0)
                return;
            RaidState state = activeRaids.Peek();
            if (state.Applied || state.Parms.target != target
                || faction == null || !faction.HostileTo(Faction.OfPlayer)
                || (!state.Prepaid && !state.Launcher.Ready) || !IsDropArrival(raidArrivalMode))
                return;
            state.OriginalAdjustedPoints = __result;
            state.Applied = true;
            activeRaids.Pop();
            activeRaids.Push(state);
            __result *= 0.3f;
        }

        public static int Begin(IncidentWorker_Raid worker, IncidentParms parms, bool debugTest)
        {
            if (debugTest || !(worker is IncidentWorker_RaidEnemy) || !(parms?.target is Map map))
                return 0;
            bool prepaid = MapComponent_RukayaSAMRaids.IsReplaying(parms);
            CompRukayaSAMLauncher launcher = prepaid ? null : CompRukayaSAMLauncher.FindReady(map);
            if (!prepaid && launcher == null)
                return 0;
            if (activeRaids == null)
                activeRaids = new Stack<RaidState>();
            activeRaids.Push(new RaidState { Parms = parms, Launcher = launcher, Prepaid = prepaid });
            return activeRaids.Count;
        }

        public static bool HasActiveDepth(int depth)
        {
            return activeRaids != null && activeRaids.Count >= depth;
        }

        public static void Complete(IncidentParms parms, bool generated, int depth)
        {
            if (depth == 0)
                return;
            if (activeRaids == null || activeRaids.Count != depth || activeRaids.Peek().Parms != parms)
            {
                Log.Error("[Rukaya] SAM raid patch state did not match the completed raid.");
                return;
            }
            RaidState state = activeRaids.Pop();
            if (!state.Applied)
                return;
            if (!generated)
            {
                parms.points = state.OriginalAdjustedPoints;
                return;
            }
            if (state.Prepaid)
                return;
            try
            {
                if (!state.Launcher.TryLaunch())
                    Log.Error($"[Rukaya] SAM launcher became unavailable after reducing raid points on {state.Launcher.parent.def.defName}.");
            }
            catch
            {
                parms.points = state.OriginalAdjustedPoints;
                throw;
            }
        }

        internal static bool IsDropArrival(PawnsArrivalModeDef mode)
        {
            return mode == PawnsArrivalModeDefOf.CenterDrop || mode == PawnsArrivalModeDefOf.EdgeDrop
                || mode == PawnsArrivalModeDefOf.RandomDrop || mode == PawnsArrivalModeDefOf.SpecificDropDebug
                || mode?.defName == "EdgeDropGroups";
        }

        private struct RaidState
        {
            public IncidentParms Parms;
            public CompRukayaSAMLauncher Launcher;
            public bool Applied;
            public bool Prepaid;
            public float OriginalAdjustedPoints;
        }

        [ThreadStatic]
        private static Stack<RaidState> activeRaids;
    }
}
