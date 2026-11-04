using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Wing gate for emergency flight: a pawn with a wing shot off cannot burst-fly away.
    ///
    /// Shares RukayaWingUtility with ordinary flight: cosmetic prostheses do not grant flight and
    /// badly injured wings cannot support an emergency launch. Rendering retains its presence check.
    ///
    /// The gate reports through <c>GizmoDisabled</c> rather than <c>CanApplyOn</c> for the same reason the cost
    /// comp does (vanilla's base <c>CanApplyOn</c> is unsafe for location targets).
    /// </summary>
    public class CompAbilityEffect_RukayaWingRequirement : CompAbilityEffect
    {
        public new CompProperties_AbilityRukayaWingRequirement Props =>
            (CompProperties_AbilityRukayaWingRequirement)props;

        /// <summary>
        /// Greys the ability out for a pawn who lost a wing.
        ///
        /// Same reasoning as <c>CompAbilityEffect_RukayaFlightCost.GizmoDisabled</c>: vanilla's
        /// <c>CompAbilityEffect.CanApplyOn</c> dereferences <c>target.Pawn.health</c> without a null check, so
        /// it cannot be used by an ability that targets bare locations.
        /// </summary>
        public override bool GizmoDisabled(out string reason)
        {
            if (HasRequiredWings)
            {
                return base.GizmoDisabled(out reason);
            }

            reason = "Rukaya_EmergencyFly_FunctionalWings".Translate(parent.pawn.Named("PAWN"));
            return true;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!HasRequiredWings)
            {
                if (throwMessages && parent?.pawn != null)
                {
                    Messages.Message(
                        "Rukaya_EmergencyFly_FunctionalWings".Translate(parent.pawn.Named("PAWN")),
                        parent.pawn,
                        MessageTypeDefOf.RejectInput,
                        false);
                }

                return false;
            }

            return base.Valid(target, throwMessages);
        }

        private bool HasRequiredWings
        {
            get
            {
                Pawn pawn = parent?.pawn;
                if (pawn == null)
                {
                    return false;
                }

                PawnRenderNodeWorker_RukayaWing.Wings wings =
                    RukayaWingUtility.FlightWings(pawn);

                // Wings is a [Flags] bitmask (None / Left / Right / Both), so "at least one" is just != None.
                return Props.requireBothWings
                    ? wings == PawnRenderNodeWorker_RukayaWing.Wings.Both
                    : wings != PawnRenderNodeWorker_RukayaWing.Wings.None;
            }
        }
    }

    /// <summary>XML tunables for <see cref="CompAbilityEffect_RukayaWingRequirement"/>.</summary>
    public class CompProperties_AbilityRukayaWingRequirement : CompProperties_AbilityEffect
    {
        /// <summary>Wing body part def the requirement is written against (documentation / future use).</summary>
        public BodyPartDef wingBodyPart;

        /// <summary>True = both wings must be intact; false = at least one.</summary>
        public bool requireBothWings = true;

        public CompProperties_AbilityRukayaWingRequirement()
        {
            compClass = typeof(CompAbilityEffect_RukayaWingRequirement);
        }
    }
}
