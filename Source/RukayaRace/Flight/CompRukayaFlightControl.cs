using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Owns the drafted flight stance of a Rukaya and hands the wing visuals to vanilla's animation system.
    ///
    /// 1. Movement / altitude - feeds <see cref="Pawn_FlightTracker"/> the current job every tick, because
    ///    vanilla only re-evaluates flight when the pawn *changes* job. The actual take-off decision lives in
    ///    <c>Patch_Pawn_FlightTracker_Notify_JobStarted</c>, and the tether to the active target lives in
    ///    <c>ShouldFlyNow</c>, which is what makes the stance self-cancelling.
    ///
    /// 2. Wings - the wings are ordinary render-tree nodes now (see
    ///    1.6/Defs/PawnRenderTreeDefs/Rukaya_PawnRenderTrees.xml), and the flap cycle is an
    ///    <c>AnimationDef</c> driven entirely by the engine (one graphic state per keyframe tick, zero C#
    ///    work per frame). This comp's remaining job is only to say *when* the flap animation may play
    ///    (<see cref="IsFlapping"/>); the actual <c>SetAnimation</c> call is made by
    ///    <c>Patch_Pawn_FlightTracker_FlightTick</c>, which is the only place the animation is ever changed.
    /// </summary>
    public class CompRukayaFlightControl : ThingComp
    {
        private RukayaFlightMode mode = RukayaFlightMode.Never;
        private bool airborneHediffApplied;

        /// <summary>Drafted state as of the last tick, used to detect the undraft edge.</summary>
        private bool lastDrafted;

        /// <summary>Airborne state as of the last tick, used to detect the take-off edge (one phase roll per flight).</summary>
        private bool wasFlapLoopActive;

        /// <summary>
        /// Phase offset in ticks applied to <c>PawnRenderTree.animationStartTick</c> for the current flight, so
        /// that pawns drafted in a batch do not all flap in lock-step. Rolled once on the take-off edge.
        /// </summary>
        private int flutterPhase;

        public CompProperties_RukayaFlightControl Props => (CompProperties_RukayaFlightControl)props;

        public Pawn Pawn => parent as Pawn;

        public RukayaFlightMode Mode => mode;

        /// <summary>True when vanilla's flight tracker is present and the pawn could ever fly.</summary>
        public bool CanEverFlyNow
        {
            get
            {
                Pawn_FlightTracker tracker = Pawn?.flight;
                return tracker != null && tracker.CanEverFly;
            }
        }

        /// <summary>
        /// True whenever the wing-flap loop should be playing. A pawn plays the flap for the whole airborne
        /// phase - take-off, stable flight and landing alike.
        ///
        /// Restricting it to the stable <c>Flying</c> state (via <c>PositionOffsetFactor == 1</c>) made the
        /// wings sit motionless through both 50-tick transitions, which reads as the animation "not having
        /// started yet" while the pawn is already visibly climbing. <c>Pawn_FlightTracker.Flying</c> is true
        /// for all three phases (it is <c>flightState &gt; Grounded</c>), so that single check is the gate.
        ///
        /// The loop is deliberately **not** gated on <see cref="ShouldFlyNow"/>: that flag is already false
        /// throughout a descent (it is what starts the descent), so requiring it here stopped the flap for the
        /// entire landing and left the pawn gliding down with folded wings. The two reasons to hold the flap
        /// back are handled where they belong instead - the render worker refuses to paint a frame over a
        /// missing wing, and this getter still requires at least one wing so a wingless pawn never enters the
        /// airborne flap state at all.
        /// </summary>
        public bool IsFlapLoopActive
        {
            get
            {
                Pawn pawn = Pawn;
                if (pawn == null || !pawn.Spawned || pawn.Map == null)
                {
                    return false;
                }

                Pawn_FlightTracker tracker = pawn.flight;
                return tracker != null && tracker.Flying && BothWings;
            }
        }

        /// <summary>
        /// Both wings must be flight-rated and retain at least half of their effective function.
        /// Ordinary prosthetic wings remain visible but cannot support flight. Re-evaluate health here
        /// so surgery, replacement and injury immediately affect the flight switch and landing logic.
        /// </summary>
        public bool BothWings => RukayaWingUtility.FlightWings(Pawn)
            == PawnRenderNodeWorker_RukayaWing.Wings.Both;

        /// <summary>
        /// True when the render worker should paint the flap **frames** right now.
        ///
        /// This is the frame source's own availability, not the flight decision: the anchor nodes carry no
        /// texPath, so their graphic comes from the animation's keyframes, and with no animation playing they
        /// would have nothing to draw. The worker uses it to keep static wings and flap frames strictly
        /// complementary, and the reference comparison (never a name comparison) means a portrait or any other
        /// animation that is not ours leaves the static wings switched on.
        ///
        /// <see cref="IsFlapLoopActive"/> is the intent; this is "the intent AND the frames are loaded".
        /// </summary>
        public static bool IsPlayingFlapAnimation(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            CompRukayaFlightControl comp = pawn.GetComp<CompRukayaFlightControl>();
            if (comp == null || !comp.IsFlapLoopActive)
            {
                return false;
            }

            AnimationDef current = pawn.Drawer?.renderer?.CurAnimation;
            return current != null
                && (current == RukayaWingRenderDefOf.Rukaya_WingFlap_South
                    || current == RukayaWingRenderDefOf.Rukaya_WingFlap_North
                    || current == RukayaWingRenderDefOf.Rukaya_WingFlap_East
                    || current == RukayaWingRenderDefOf.Rukaya_WingFlap_West);
        }

        /// <summary>Satiety gate. A pawn without a food need is never blocked by it.</summary>
        public bool IsWellFed
        {
            get
            {
                if (Props.hungerPctThresholdCanFly <= 0f)
                {
                    return true;
                }

                Need_Food food = Pawn?.needs?.food;
                return food == null || food.CurLevelPercentage >= Props.hungerPctThresholdCanFly;
            }
        }

        /// <summary>
        /// Player flight follows the drafted switch or an active combat target. Autonomous visitors,
        /// allies and enemies also fly while moving, except peaceful trading-party movement.
        /// The player's stance is never rewritten by the AI policy.
        /// </summary>
        public bool ShouldFlyNow
        {
            get
            {
                Pawn pawn = Pawn;
                if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead)
                {
                    return false;
                }

                if (!IsWellFed)
                {
                    return false;
                }

                // Check intent before walking the health/wing data. Peaceful traders and idle
                // pawns need no wing-function calculation merely to remain on the ground.
                bool wantsFlight = (pawn.Drafted && mode == RukayaFlightMode.Always)
                    || RukayaFlightAIUtility.HasCombatTarget(pawn)
                    || RukayaFlightAIUtility.ShouldFlyWhileMoving(pawn);
                return wantsFlight && BothWings;
            }
        }

        public void SetMode(RukayaFlightMode newMode)
        {
            if (mode == newMode)
            {
                return;
            }

            mode = newMode;
        }

        public override void CompTick()
        {
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return;
            }

            UpdateFlightModeLatch(pawn);
            UpdateFlight(pawn);
            AlignWingAnimation(pawn);
        }

        /// <summary>
        /// Keeps the playing wing animation equal to what <see cref="IsFlapLoopActive"/> currently wants, from
        /// the comp's own tick rather than from a Harmony hook.
        ///
        /// This is the **only** place the animation is ever set. The render worker treats "static wings" and
        /// "flap frames" as strict complements, so if the pawn leaves the ground while <c>CurAnimation</c> is
        /// still null, the static wings keep drawing and the flap frames never appear; deriving it here makes
        /// that impossible and keeps <c>CurAnimation</c> consistent with what the worker computed during the
        /// same tick. Having a single owner also matters now that the loop spans take-off and landing: a second
        /// writer that cleared the animation on the tracker's own hook would fight this one and dirty the render
        /// tree on every tick of both 50-tick transitions.
        ///
        /// The four flap defs declare <c>loopMode = Loop</c> explicitly, so a running cycle wraps on its own and
        /// never reports finished. That is what keeps this cheap: once the right def is playing the comparison
        /// below is the only work per tick, and <c>SetAnimation</c> - which dirties the whole tree - runs only
        /// when the def actually changes (leaving the ground, landing, turning, or a cycle that something else
        /// cleared).
        /// </summary>
        private void AlignWingAnimation(Pawn pawn)
        {
            if (pawn.flight == null || pawn.Drawer?.renderer == null)
            {
                return;
            }

            bool flapActive = IsFlapLoopActive;

            // One phase roll per flight, taken on the false -> true edge, so a rotation change or a restart
            // reuses the same offset and the wing keeps its own rhythm instead of snapping back into step.
            if (flapActive && !wasFlapLoopActive)
            {
                AnimationDef def = Pawn_FlightTracker.GetBestFlyAnimation(pawn, null);
                flutterPhase = def != null && def.durationTicks > 0 ? Rand.Range(0, def.durationTicks) : 0;
            }

            wasFlapLoopActive = flapActive;

            AnimationDef wanted = flapActive ? Pawn_FlightTracker.GetBestFlyAnimation(pawn, null) : null;
            PawnRenderer renderer = pawn.Drawer.renderer;

            if (renderer.CurAnimation == wanted)
            {
                return;
            }

            renderer.SetAnimation(wanted);

            // SetAnimation seeds animationStartTick from the pawn's thingIDNumber, which is *contiguous* across
            // pawns spawned together (dev-mode spawns, a batch of starting colonists), so Rand.RangeSeeded hands
            // neighbouring pawns nearly the same phase and a group drafted together flaps in lock-step. Rewinding
            // by our own roll desynchronises them; applying it after every SetAnimation keeps the offset when the
            // def changes mid-flight (a turn).
            if (wanted != null && flutterPhase != 0 && renderer.renderTree != null)
            {
                renderer.renderTree.animationStartTick -= flutterPhase;
            }
        }

        /// <summary>
        /// Why the flight switch cannot be used right now, or null when it can.
        ///
        /// The reasons are the same predicates the flight decision itself uses (<see cref="BothWings"/>,
        /// <see cref="IsWellFed"/>), so the tooltip can never disagree with the behaviour. Order matters only
        /// for which single reason is shown first when several apply.
        ///
        /// It does not report "she is not drafted" - that is the player's own choice, not a blocked ability - and
        /// it does not report the stance being switched off, or the switch could never be switched back on.
        /// </summary>
        private string FlightBlockedReason(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            if (!BothWings)
            {
                return "Rukaya_FlightDisabledFunctionalWings".Translate();
            }

            if (pawn.Downed)
            {
                return "Rukaya_FlightDisabledDowned".Translate();
            }

            if (!IsWellFed)
            {
                return "Rukaya_FlightDisabledHungry".Translate(
                    (Props.hungerPctThresholdCanFly * 100f).ToString("0"));
            }

            return null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.Drafted || !pawn.IsColonistPlayerControlled || !CanEverFlyNow)
            {
                yield break;
            }

            RukayaFlightMode current = mode;
            string blockedReason = FlightBlockedReason(pawn);

            Command_Action flightCommand = new Command_Action
            {
                defaultLabel = RukayaFlightUtility.CommandLabelKey(current).Translate(),
                defaultDesc = RukayaFlightUtility.CommandDescKey(current).Translate(),
                icon = RukayaFlightUtility.IconFor(current),
                action = delegate
                {
                    RukayaFlightMode next = current == RukayaFlightMode.Always
                        ? RukayaFlightMode.Never
                        : RukayaFlightMode.Always;

                    SetMode(next);
                    RukayaFlightUtility.PlaySwitchSound(next);
                    RukayaFlightUtility.ShowSwitchMessage(pawn, next);

                    // Feedback must be immediate; the tick handler would otherwise only react next tick.
                    UpdateFlight(pawn);
                    AlignWingAnimation(pawn);
                }
            };

            // A control the player cannot use is still shown, greyed out, and says why on hover. Hiding it reads
            // as the ability having vanished, which is exactly the confusion a shot-off wing used to cause.
            // Gizmo.Disable feeds Command's own disabled path: the button is drawn greyed out and
            // "DisabledCommand: <reason>" is appended to the tooltip. The reason is also prefixed onto the
            // description so it stays legible in the merged multi-pawn tooltip, where the disabled line is easy
            // to miss.
            if (!blockedReason.NullOrEmpty())
            {
                flightCommand.Disable(blockedReason);
                flightCommand.defaultDesc = blockedReason + "\n\n" + flightCommand.defaultDesc;
            }

            yield return flightCommand;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            Pawn pawn = Pawn;
            if (pawn == null)
            {
                return;
            }

            lastDrafted = pawn.Drafted;

            // Loading a save while airborne with the stance off must not leave the pawn hovering. The
            // animation itself needs no attention here: the flight tick hook re-derives it every tick.
            if (!ShouldFlyNow)
            {
                CancelFlightAndLand(pawn);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "rukayaFlightMode", RukayaFlightMode.Never);
            Scribe_Values.Look(ref airborneHediffApplied, "rukayaFlightHediff", false);
        }

        // ------------------------------------------------------------ flight stance

        /// <summary>
        /// Drafting is the only entry point for flight, so undrafting also switches the flight option back
        /// off (requirement R5). The option is a per-deployment switch, not a permanent character setting.
        ///
        /// The latch also releases when the pawn can no longer honour the switch at all - a wing shot off, a
        /// downed pawn, starvation. Without that the switch stays lit at "on" for a pawn that is standing on
        /// the ground and will never take off, which reads as the switch having silently stopped working.
        /// Restricting the reset to the case where the switch is actually lit keeps the disabled state while
        /// the pawn is un-drafted (the switch cannot be turned on then anyway, since the gizmo only exists
        /// while drafted).
        /// </summary>
        private void UpdateFlightModeLatch(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            lastDrafted = pawn.Drafted;

            if (mode == RukayaFlightMode.Never)
            {
                return;
            }

            // Home only on real reasons, never on the switch's own state.
            if (!pawn.Drafted || !BothWings || !IsWellFed)
            {
                mode = RukayaFlightMode.Never;
            }
        }

        // ---------------------------------------------------------------- flight

        private void UpdateFlight(Pawn pawn)
        {
            if (pawn.flight == null)
            {
                return;
            }

            if (ShouldFlyNow)
            {
                if (pawn.Flying)
                {
                    ApplyAirborneHediff(pawn);

                    if (Props.hungerPctCostPerSecondFly > 0f
                        && pawn.needs?.food != null
                        && pawn.IsHashIntervalTick(60))
                    {
                        pawn.needs.food.CurLevelPercentage -= AirborneHungerCost(pawn);
                    }
                }
                else if (pawn.CurJob != null)
                {
                    // Continuous re-evaluation: re-feed the current job to the vanilla tracker.
                    pawn.flight.Notify_JobStarted(pawn.CurJob);
                }
            }
            else
            {
                CancelFlightAndLand(pawn);
            }
        }

        private void CancelFlightAndLand(Pawn pawn)
        {
            if (pawn.flight == null)
            {
                return;
            }

            if (pawn.CurJob != null && pawn.CurJob.flying)
            {
                pawn.CurJob.flying = false;
            }

            if (pawn.Flying)
            {
                pawn.flight.ForceLand();
            }

            RemoveAirborneHediff(pawn);
        }

        /// <summary>
        /// Food eaten this second while airborne, including the flight-exhaustion surcharge.
        ///
        /// Exhaustion is the price of emergency flight, and this is what makes sustained hovering part of the
        /// same economy: severity 3.0 with the default 0.25 factor costs 1.75x the base rate. Keeping the
        /// multiplier here rather than in the exhaustion hediff means the hediff stays purely "how tired the
        /// wings are" and every consumer decides for itself what that is worth.
        /// </summary>
        private float AirborneHungerCost(Pawn pawn)
        {
            float cost = Props.hungerPctCostPerSecondFly;

            if (Props.exhaustedHungerCostFactor <= 0f || RukayaFlightDefOf.Rukaya_FlightExhaustion == null)
            {
                return cost;
            }

            Hediff_FlightExhaustion exhaustion = pawn.health?.hediffSet
                ?.GetFirstHediffOfDef(RukayaFlightDefOf.Rukaya_FlightExhaustion) as Hediff_FlightExhaustion;

            if (exhaustion != null)
            {
                cost *= 1f + Props.exhaustedHungerCostFactor * exhaustion.Severity;
            }

            return cost;
        }

        private void ApplyAirborneHediff(Pawn pawn)
        {
            if (airborneHediffApplied || Props.inFlightHediff == null)
            {
                return;
            }

            if (pawn.health?.hediffSet != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(Props.inFlightHediff) == null)
            {
                pawn.health.AddHediff(Props.inFlightHediff);
            }

            airborneHediffApplied = true;
        }

        private void RemoveAirborneHediff(Pawn pawn)
        {
            if (!airborneHediffApplied || Props.inFlightHediff == null)
            {
                return;
            }

            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(Props.inFlightHediff);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }

            airborneHediffApplied = false;
        }
    }
}
