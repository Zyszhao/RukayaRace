using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Makes <c>Pawn_FlightTracker.ForceLand</c> idempotent *and* smooths the start of the landing curve.
    ///
    /// Two vanilla behaviours interact badly, and this mod triggers both:
    ///
    /// 1. **Repeated calls restart the descent.** <c>ForceLand</c> unconditionally rewinds the transition to
    ///    <c>lerpTick = 25</c> and adds 50 to the flight cooldown. Normally it is only reached once per
    ///    landing, but this comp re-asserts the flight decision every tick (see
    ///    <c>CompRukayaFlightControl.UpdateFlight</c>), so while the pawn is descending it calls
    ///    <c>ForceLand</c> again on every tick. Each call rewinds the landing lerp back to 25, so the pawn
    ///    never reaches <c>lerpTick &gt;= 50</c> and never reaches the ground: it descends a slight amount and
    ///    snaps back, forever. The cooldown also grows by 50 every tick.
    ///
    /// 2. **Starting mid-curve is a visible jump.** <c>lerpTick = 25</c> is the middle of the 50-tick landing
    ///    curve, so <c>PositionOffsetFactor</c> drops from <c>1.0</c> (flying) to its half-way value in one
    ///    step. That factor drives <c>Pawn_DrawTracker.FlyingOffset</c> (<c>0.6 * factor</c> along Z) and
    ///    <c>FlightYOffset</c> (<c>0.03658 * factor</c> along Y), so the pawn is drawn noticeably further back
    ///    on the single tick the switch is flipped. Take-off has no such jump because it starts at
    ///    <c>lerpTick = 0</c>, exactly where the grounded state left off.
    ///
    /// The fix is one postfix that detects a *fresh* landing - the state was <c>TakingOff</c> or <c>Flying</c>
    /// just before the call - and in that case starts it at <c>lerpTick = 0</c>. A call that arrives while the
    /// pawn is already landing is a no-op, so the descent runs once, in order, over 50 ticks, with the factor
    /// continuous across the switch (1.0 -&gt; 1.0). Landing therefore takes the same time as take-off, and the
    /// pawn actually gets back on the ground.
    ///
    /// The cost of getting this wrong is worth stating: an earlier version of this patch reset <c>lerpTick</c>
    /// on *every* call, which combined with the repeated <c>ForceLand</c> above to freeze the pawn in the air.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.ForceLand))]
    public static class Patch_Pawn_FlightTracker_ForceLand
    {
        /// <summary>Grounded / Flying / TakingOff / Landing, in the vanilla enum's own order.</summary>
        private const int StateGrounded = 0;
        private const int StateFlying = 1;
        private const int StateTakingOff = 2;
        private const int StateLanding = 3;

        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, int> LerpTickRef =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, int>("lerpTick");

        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, int> FlightStateRef =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, int>("flightState");

        /// <summary>Set once the failure has been reported, so the log is not spammed per call.</summary>
        private static bool resolveWarned;

        /// <summary>
        /// Records whether this call actually starts a landing. <c>ForceLand</c> returns early unless the state
        /// is <c>TakingOff</c> or <c>Flying</c>, so anything else means the paired <c>lerpTick = 25</c> write did
        /// not happen and must not be "corrected". This is also what stops the repeated per-tick calls from
        /// rewinding a descent that is already under way.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(Pawn_FlightTracker __instance, ref bool __state)
        {
            __state = false;
            if (__instance == null)
            {
                return;
            }

            try
            {
                int state = FlightStateRef(__instance);
                __state = state == StateTakingOff || state == StateFlying;
            }
            catch (Exception ex)
            {
                ReportAccessFailure(ex, "flightState", "landing will restart every tick and the pawn will never "
                                                       + "reach the ground");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn_FlightTracker __instance, bool __state)
        {
            if (!__state || __instance == null)
            {
                return;
            }

            try
            {
                LerpTickRef(__instance) = 0;
            }
            catch (Exception ex)
            {
                ReportAccessFailure(ex, "lerpTick", "landing will start mid-curve and the pawn will visibly jump "
                                                    + "when flight is switched off");
            }
        }

        private static void ReportAccessFailure(Exception ex, string field, string consequence)
        {
            if (resolveWarned)
            {
                return;
            }

            resolveWarned = true;
            Log.ErrorOnce("[Rukaya] Could not access Pawn_FlightTracker." + field + ", so " + consequence
                          + ".\n" + ex, Gen.HashCombine<int>(194372, field.GetHashCode()));
        }
    }
}
