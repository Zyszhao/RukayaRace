using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Takes over flying-animation selection for Rukaya pawns.
    ///
    /// Vanilla's <c>Pawn_FlightTracker.GetBestFlyAnimation</c> returns null immediately for
    /// <c>RaceProps.Humanlike</c> pawns - the stock flying animations come from
    /// <c>PawnKindLifeStage.flyingAnimation*</c>, which humanlikes never populate. So a humanlike race
    /// that wants a wing-flap cycle has to answer that question itself; this prefix is that answer.
    ///
    /// Behaviour:
    ///   * Only pawns carrying <see cref="CompRukayaFlightControl"/> are handled - identification is by
    ///     comp, not by hard-coding a race defName.
    ///   * The animation is returned for the whole airborne phase (take-off, flight and landing), and null
    ///     the moment the pawn is back on the ground. <see cref="CompRukayaFlightControl.IsFlapLoopActive"/>
    ///     owns that decision, and it is driven purely by "airborne + still has a wing" - deliberately not by
    ///     <c>ShouldFlyNow</c>, which is already false throughout a descent and would stop the flap for the
    ///     whole landing.
    ///   * West has its own animation and its own <c>{1..8}_west</c> frame set - the frames are real
    ///     paintings on the west half of the canvas, not the east ones mirrored at runtime, so the west
    ///     anchors only cancel the mesh set's pre-mirrored West slot
    ///     (<c>drawData/dataWest/flip</c>) and sample their own art.
    ///
    /// Note that <c>Pawn_FlightTracker.CheckFlyAnimation</c> compares this result with
    /// <c>PawnRenderer.CurAnimation</c> by reference and only calls <c>SetAnimation</c> when it differs, and
    /// the actual per-tick reconciliation lives in <c>CompRukayaFlightControl.AlignWingAnimation</c>. Having
    /// two writers would be harmless while they agree, but this patch is what makes the tracker's own
    /// bookkeeping (<c>playing</c>, the landing transition) consistent with the animation.
    ///
    /// The injected <c>___pawn</c> is valid: the field is <c>Pawn_FlightTracker.pawn</c>.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.GetBestFlyAnimation))]
    public static class Patch_Pawn_FlightTracker_GetBestFlyAnimation
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref AnimationDef __result)
        {
            if (pawn == null)
            {
                return true;
            }

            CompRukayaFlightControl comp = pawn.GetComp<CompRukayaFlightControl>();
            if (comp == null)
            {
                return true;
            }

            // Full override from here on: the vanilla body only ever considers animal life stages.
            __result = comp.IsFlapLoopActive ? AnimationFor(pawn.Rotation) : null;

            return false;
        }

        private static AnimationDef AnimationFor(Rot4 facing)
        {
            if (facing == Rot4.North)
            {
                return RukayaWingRenderDefOf.Rukaya_WingFlap_North;
            }

            if (facing == Rot4.South)
            {
                return RukayaWingRenderDefOf.Rukaya_WingFlap_South;
            }

            if (facing == Rot4.East)
            {
                return RukayaWingRenderDefOf.Rukaya_WingFlap_East;
            }

            if (facing == Rot4.West)
            {
                return RukayaWingRenderDefOf.Rukaya_WingFlap_West;
            }

            return RukayaWingRenderDefOf.Rukaya_WingFlap_South;
        }
    }
}
