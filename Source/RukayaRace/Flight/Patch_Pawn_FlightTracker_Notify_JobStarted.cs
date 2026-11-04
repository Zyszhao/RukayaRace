using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Takes over the vanilla job-start flight check for Rukaya pawns only. The comp re-invokes
    /// <see cref="Pawn_FlightTracker.Notify_JobStarted"/> every tick, so this prefix is where the
    /// "drafted + flight stance" decision is actually evaluated, for both take-off and landing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.Notify_JobStarted))]
    public static class Patch_Pawn_FlightTracker_Notify_JobStarted
    {
        [HarmonyPrefix]
        public static bool Prefix(Job job, Pawn_FlightTracker __instance, Pawn ___pawn)
        {
            if (___pawn == null || job == null)
            {
                return true;
            }

            CompRukayaFlightControl comp = ___pawn.GetComp<CompRukayaFlightControl>();
            if (comp == null)
            {
                return true;
            }

            if (comp.ShouldFlyNow)
            {
                if (!__instance.Flying && __instance.CanFlyNow)
                {
                    __instance.StartFlying();
                }

                job.flying = __instance.Flying;
                return false;
            }

            job.flying = false;
            if (__instance.Flying)
            {
                __instance.ForceLand();
            }

            return false;
        }
    }
}
