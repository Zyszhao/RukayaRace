using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Cast cost and gating for emergency flight.
    ///
    /// This is **not** the same thing as the continuous flight upkeep: that is a per-second hunger drain owned
    /// by <see cref="CompRukayaFlightControl"/>. This comp charges the one-shot price of casting the ability
    /// (hunger + rest) and refuses to cast at all below a threshold, then hands the exhaustion hediff its
    /// "one more use" notification.
    ///
    /// The threshold is enforced in <see cref="GizmoDisabled"/>, which vanilla calls every frame to decide
    /// whether the ability button is usable, so an underfed pawn sees a greyed-out button **with the reason**
    /// rather than a cast that silently fails. It is not done in <c>CanApplyOn</c> because vanilla's base
    /// implementation there dereferences <c>target.Pawn.health</c> unguarded and this ability mostly targets
    /// bare locations; see the note on <see cref="GizmoDisabled"/>.
    /// <see cref="Valid"/> repeats the check for the targeting path, which is where a message can be shown.
    ///
    /// What <see cref="Apply"/> must never touch, and why (see 设计文档/飞行系统_浮动与紧急飞行实装.md §2.5):
    ///   · <c>pawn.drafter.Drafted</c>            - PawnFlyer saves and restores it itself
    ///   · <see cref="CompRukayaFlightControl.SetMode"/> - the mode must survive the flight untouched
    ///   · <c>CompRukayaFlightControl.CancelFlightAndLand</c> - at Apply time the pawn is still on the map, so
    ///     this would set the flight state to Landing and then the flyer's DeSpawn would freeze it there,
    ///     leaving the pawn standing on the ground while the flap animation keeps playing
    ///
    /// Charging is written as <c>CurLevelPercentage -=</c> rather than <c>CurLevel -=</c> so the XML knobs read
    /// as percentages, matching <c>CompProperties_RukayaFlightControl.hungerPctCostPerSecondFly</c>.
    /// </summary>
    public class CompAbilityEffect_RukayaFlightCost : CompAbilityEffect
    {
        public new CompProperties_AbilityRukayaFlightCost Props =>
            (CompProperties_AbilityRukayaFlightCost)props;

        /// <summary>
        /// The gate that greys the ability button out and explains why.
        ///
        /// This is deliberately <c>GizmoDisabled</c> and **not** <c>CanApplyOn</c>. Vanilla's
        /// <c>CompAbilityEffect.CanApplyOn</c> starts with <c>target.Pawn.health.hediffSet...</c> with no null
        /// check, and our ability targets **locations** for most casts, so <c>target.Pawn</c> is routinely
        /// null - calling the base implementation there would throw. Vanilla's own cost comp for a
        /// location-targeting ability (<c>CompAbilityEffect_HemogenCost</c>, used by the jump-granting
        /// sanguophage gene) does exactly the same thing: it leaves <c>CanApplyOn</c> alone and reports
        /// through <c>GizmoDisabled</c>.
        /// </summary>
        public override bool GizmoDisabled(out string reason)
        {
            if (HasEnoughResources)
            {
                return base.GizmoDisabled(out reason);
            }

            reason = "Rukaya_EmergencyFly_NotEnoughResources".Translate(parent.pawn.Named("PAWN"));
            return true;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!HasEnoughResources)
            {
                if (throwMessages && parent?.pawn != null)
                {
                    Messages.Message(
                        "Rukaya_EmergencyFly_NotEnoughResources".Translate(parent.pawn.Named("PAWN")),
                        parent.pawn,
                        MessageTypeDefOf.RejectInput,
                        false);
                }

                return false;
            }

            return base.Valid(target, throwMessages);
        }

        /// <summary>
        /// Charges the cast and records the exhaustion. Runs once per successful cast, before the pawn is
        /// despawned into the flyer.
        /// </summary>
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = parent?.pawn;
            if (pawn == null)
            {
                return;
            }

            if (Props.hungerCost > 0f && pawn.needs?.food != null)
            {
                pawn.needs.food.CurLevelPercentage -= Props.hungerCost;
            }

            if (Props.restCost > 0f && pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevelPercentage -= Props.restCost;
            }

            NotifyExhaustion(pawn);
        }

        /// <summary>
        /// Adds (or tops up) the exhaustion hediff. Misses are silent rather than fatal: a pawn with no health
        /// tracker has bigger problems than being tired, and the cast itself has already succeeded.
        /// </summary>
        private void NotifyExhaustion(Pawn pawn)
        {
            HediffDef exhaustionDef = Props.exhaustionHediff;
            if (exhaustionDef == null || Props.exhaustionPerUse <= 0f || pawn.health?.hediffSet == null)
            {
                return;
            }

            Hediff_FlightExhaustion exhaustion =
                pawn.health.hediffSet.GetFirstHediffOfDef(exhaustionDef) as Hediff_FlightExhaustion;

            if (exhaustion == null)
            {
                exhaustion = pawn.health.AddHediff(exhaustionDef) as Hediff_FlightExhaustion;
            }

            exhaustion?.Notify_UsedEmergencyFlight(Props.exhaustionPerUse);
        }

        /// <summary>
        /// True when the pawn can pay for a cast. A missing need never blocks the cast, so a race without a rest
        /// need (or a pawn whose need is temporarily absent) is not silently forbidden from flying.
        /// </summary>
        private bool HasEnoughResources
        {
            get
            {
                Pawn pawn = parent?.pawn;
                if (pawn == null)
                {
                    return false;
                }

                // A threshold of 0 (or less) means "do not check this resource".
                if (Props.hungerThreshold > 0f && pawn.needs?.food != null
                    && pawn.needs.food.CurLevelPercentage < Props.hungerThreshold)
                {
                    return false;
                }

                if (Props.restThreshold > 0f && pawn.needs?.rest != null
                    && pawn.needs.rest.CurLevelPercentage < Props.restThreshold)
                {
                    return false;
                }

                return true;
            }
        }
    }

    /// <summary>
    /// XML tunables for <see cref="CompAbilityEffect_RukayaFlightCost"/>. All costs and thresholds are
    /// fractions of a full need (0.04 = 4%), matching the hunger terminology used by
    /// <c>CompProperties_RukayaFlightControl</c>.
    /// </summary>
    public class CompProperties_AbilityRukayaFlightCost : CompProperties_AbilityEffect
    {
        /// <summary>Hunger percentage charged per cast (0.04 = 4%).</summary>
        public float hungerCost = 0.04f;

        /// <summary>Refuse to cast below this hunger percentage. 0 disables the check.</summary>
        public float hungerThreshold = 0.2f;

        /// <summary>Rest percentage charged per cast.</summary>
        public float restCost = 0.02f;

        /// <summary>Refuse to cast below this rest percentage. 0 disables the check.</summary>
        public float restThreshold = 0.2f;

        /// <summary>Hediff that accumulates flight exhaustion. Null disables the whole exhaustion layer.</summary>
        public HediffDef exhaustionHediff;

        /// <summary>Severity added to <see cref="exhaustionHediff"/> per cast.</summary>
        public float exhaustionPerUse = 1.0f;

        public CompProperties_AbilityRukayaFlightCost()
        {
            compClass = typeof(CompAbilityEffect_RukayaFlightCost);
        }
    }
}
