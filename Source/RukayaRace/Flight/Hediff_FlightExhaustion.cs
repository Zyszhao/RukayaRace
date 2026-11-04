using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Accumulating flight exhaustion, carried by the <c>Rukaya_FlightExhaustion</c> hediff.
    ///
    /// Two independent layers live here, and keeping them separate is the whole point of this class:
    ///
    ///   * **Layer A - one-shot cost.** Every emergency flight sets <see cref="ShortPenaltyActive"/> for
    ///     <c>ShortPenaltyDurationTicks</c>, which costs extra move speed. It is fixed and does not scale with
    ///     how tired the pawn already is, so a single panic-jump always reads the same way.
    ///   * **Layer B - accumulation.** <c>Severity</c> is "how overworked the wings are"; each use adds
    ///     <c>exhaustionPerUse</c> and it decays slowly. The actual penalties are **not** written here - they
    ///     come from the stage list in <c>1.7/Defs/HediffDefs/Rukaya_FlightExhaustion.xml</c>, which maps
    ///     severity to stat offsets and capacity modifiers.
    ///
    /// Why layer A is not modelled as severity: severity is an accumulator, so folding a fixed short-term
    /// penalty into it couples the two - one use would have to jump the severity a lot, which then takes the
    /// long-term stage penalties with it, and the timing of the short penalty would be decided by the decay
    /// rate instead of by this class. Two fields, two meanings.
    ///
    /// Decay is applied on a fixed cadence but credited by elapsed ticks, from the interval hook. A hediff is
    /// ticked by the pawn's health tracker, and doing the subtraction there per tick would be both wasteful and
    /// prone to float drift, so the accumulator counts down and applies one step per
    /// <see cref="DecayIntervalTicks"/>. The rate is deliberately slow enough that the top tier is a real
    /// state a pawn spends time in rather than a flicker; see <see cref="DecayPerInterval"/>.
    /// </summary>
    public class Hediff_FlightExhaustion : HediffWithComps
    {
        /// <summary>Tick after which the short one-shot move penalty stops applying. Negative = never armed.</summary>
        private int shortPenaltyUntilTick = -1;

        /// <summary>
        /// Ticks still owed before the next decay step. Counts down by <c>delta</c> in
        /// <see cref="TickInterval"/>, so elapsed time is always credited in full.
        /// </summary>
        private int ticksUntilDecay = DecayIntervalTicks;

        /// <summary>
        /// Upper bound on decay steps resolved in a single interval call. Guards the loop in
        /// <see cref="TickInterval"/> against a pathological delta; a normal delta is 1 or a small batch,
        /// so one or two steps is the realistic maximum.
        /// </summary>
        private const int MaxDecayStepsPerInterval = 64;

        /// <summary>Decay cadence in ticks. 250 ticks ~= 4.17 seconds.</summary>
        private const int DecayIntervalTicks = 250;

        /// <summary>
        /// Severity removed per decay step.
        ///
        /// 0.005 per 250 ticks = 0.02 per 1000 ticks = 1.2 per game day (60 000 ticks), i.e. a full point
        /// takes 50 000 ticks ~= 20.8 game hours. See the note on <see cref="TickInterval"/> for why this is
        /// deliberately slow: at the original 0.01 the whole track drained in a bit over a day, so severity
        /// 3.0 ("severe") existed only for the handful of seconds right after a burst and the top penalty
        /// tiers were effectively unreachable in normal play.
        /// </summary>
        private const float DecayPerInterval = 0.005f;

        /// <summary>How long the one-shot move penalty lasts. 600 ticks = 10 seconds.</summary>
        private const int ShortPenaltyDurationTicks = 600;

        /// <summary>Move-speed offset contributed while the one-shot window is open.</summary>
        public const float ShortPenaltyMoveSpeed = -0.30f;

        /// <summary>True while the one-shot post-flight move penalty still applies.</summary>
        public bool ShortPenaltyActive =>
            shortPenaltyUntilTick > 0 && GenTicks.TicksGame < shortPenaltyUntilTick;

        /// <summary>
        /// Records one emergency flight: bumps the accumulated severity by <paramref name="severityPerUse"/>
        /// and re-arms the short move penalty window.
        ///
        /// Called from <see cref="CompAbilityEffect_RukayaFlightCost.Apply"/> - i.e. the instant the ability is
        /// cast, while the pawn is still on the map. It deliberately touches nothing else: no flight mode, no
        /// drafted flag and no flight state, because the pawn is about to be despawned into a PawnFlyer and any
        /// state written here is exactly the kind that would get stuck (see the notes on that comp).
        /// </summary>
        public void Notify_UsedEmergencyFlight(float severityPerUse)
        {
            Severity += severityPerUse;
            shortPenaltyUntilTick = GenTicks.TicksGame + ShortPenaltyDurationTicks;
        }

        /// <summary>
        /// Applies decay from the interval hook rather than from <see cref="Hediff.Tick"/>.
        ///
        /// This matters for correctness, not just style. <c>Pawn.Tick</c> calls <c>HealthTick</c> only while
        /// the pawn is spawned and not suspended, whereas <c>Pawn.TickInterval(delta)</c> reaches the health
        /// tracker in every case, including world pawns, caravans and map transitions - and it hands over the
        /// exact number of ticks that elapsed. Driving decay off <c>delta</c> therefore means the pawn is
        /// charged exactly the time that passed no matter how RimWorld chose to batch the ticks, instead of
        /// relying on <c>Tick</c> being called once per tick at a particular moment.
        ///
        /// The accumulator is kept as ticks-owed rather than "last time we ran": that way a long delta (a
        /// lag spike, a fast-forward, a pawn that spent time off-map) is credited in full instead of being
        /// silently swallowed by the interval check, which is what let severity sit at a tier longer than
        /// the numbers suggested.
        /// </summary>
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (delta <= 0)
            {
                return;
            }

            ticksUntilDecay -= delta;

            // A loop rather than a single subtraction: if a huge delta arrives (or the interval is ever
            // shortened to below 60), one pass would leave the accumulator negative and the next step would
            // come due early. Guarded so a pathological delta cannot spin here.
            int steps = 0;
            while (ticksUntilDecay <= 0 && steps < MaxDecayStepsPerInterval)
            {
                ticksUntilDecay += DecayIntervalTicks;
                steps++;
            }

            if (steps <= 0)
            {
                return;
            }

            // If even the step cap could not drain the accumulator, resync instead of carrying a huge debt.
            if (ticksUntilDecay <= 0)
            {
                ticksUntilDecay = DecayIntervalTicks;
            }

            float decayed = Severity - DecayPerInterval * steps;

            // Remove at zero instead of leaving a 0-severity shell: the first stage has minSeverity 0 and would
            // keep applying its small penalty forever while the icon stays on the health tab.
            if (decayed <= 0f)
            {
                pawn?.health?.RemoveHediff(this);
                return;
            }

            Severity = decayed;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shortPenaltyUntilTick, "rukayaShortPenaltyUntilTick", -1);
            Scribe_Values.Look(ref ticksUntilDecay, "rukayaTicksUntilDecay", DecayIntervalTicks);
        }
    }
}
