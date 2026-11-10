using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.AirDefense
{
    // Intercept only storyteller mech-cluster incidents. Quest-owned sketches and boss summons
    // retain their original contents and activation conditions.
    [HarmonyPatch(typeof(IncidentWorker_MechCluster), "TryExecuteWorker")]
    public static class Patch_IncidentWorker_MechCluster_RukayaSAM
    {
        public const float RemainingPointsFactor = 0.2f;
        private static readonly MethodInfo SendLetter = AccessTools.Method(typeof(IncidentWorker),
            "SendStandardLetter", new[] { typeof(IncidentParms), typeof(LookTargets), typeof(NamedArgument[]) });

        [HarmonyPrefix]
        public static bool Prefix(IncidentWorker_MechCluster __instance, IncidentParms parms, ref bool __result)
        {
            if (!(parms?.target is Map map) || !ModsConfig.RoyaltyActive || Faction.OfMechanoids == null)
                return true;
            CompRukayaSAMLauncher launcher = CompRukayaSAMLauncher.FindReady(map);
            if (launcher == null)
                return true;

            MechClusterSketch sketch = MechClusterGenerator.GenerateClusterSketch(
                parms.points * RemainingPointsFactor, map, startDormant: false);
            IntVec3 center = MechClusterUtility.FindClusterPosition(map, sketch, 100, 0.5f);
            if (!center.IsValid || !launcher.TryLaunch())
                return true;

            var things = MechClusterUtility.SpawnCluster(center, map, sketch,
                dropInPods: true, canAssaultColony: true, questTag: parms.questTag);
            foreach (Thing thing in things)
            {
                // Awake turrets must also skip the vanilla multi-day power-up delay.
                CompInitiatable initiation = thing.TryGetComp<CompInitiatable>();
                if (initiation != null)
                    initiation.initiationDelayTicksOverride = 1;
            }
            var targets = things.Where(t => t.def != ThingDefOf.Wall && t.def != ThingDefOf.Barricade);
            SendLetter.Invoke(__instance, new object[] { parms, new LookTargets(targets), Array.Empty<NamedArgument>() });
            __result = true;
            return false;
        }
    }
}
