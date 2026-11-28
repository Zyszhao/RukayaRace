using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RukayaRace.Scenario;
using UnityEngine;
using Verse;

namespace RukayaRace.Storytelling
{
    public sealed class AnnualTrioGame : GameComponent
    {
        public const string EventName = "Rukaya_AnnualTrioArrival";
        private const int FirstArrivalWindow = 2 * GenDate.TicksPerQuadrum;
        private const int FirstArrivalFallbackDelay = FirstArrivalWindow - GenDate.TicksPerDay;
        private readonly AnnualTrioSchedule schedule = new AnnualTrioSchedule();
        private int firstArrivalStartTick = -1;
        private bool firstArrivalCompleted;
        private int nextFallbackAttemptTick;
        private bool loadedFromSave;
        private bool busy;
        internal bool Delivering => busy;
        internal bool FirstArrivalPending => firstArrivalStartTick >= 0 && !firstArrivalCompleted;
        public AnnualTrioGame(Game game) { }

        public static bool Eligible => Current.Game != null && Find.Scenario != null;
        private static bool IsRukayaStartScenario => Find.GameInitData?.playerFaction?.def?.defName == "Rukaya_PlayerFaction"
            || Find.Scenario?.AllParts.Any(p => p is ScenPart_RukayaKinRelations || p is ScenPart_RukayaAirStation
                || p is ScenPart_IncidentBase incident && incident.Incident?.defName == "Rukaya_TrioArrival") == true;
        private static bool FirstYearGuaranteeEligible => Eligible && !IsRukayaStartScenario;

        private void UpdateYear()
        {
            schedule.RefreshYear(GenDate.YearsPassed);
        }

        public override void StartedNewGame() => ScheduleFirstArrival();

        public override void LoadedGame()
        {
            if (!loadedFromSave) ScheduleFirstArrival();
        }

        private void ScheduleFirstArrival()
        {
            if (FirstYearGuaranteeEligible) firstArrivalStartTick = Find.TickManager.TicksGame;
        }

        public override void GameComponentTick()
        {
            if (!FirstArrivalPending || busy) return;
            int now = Find.TickManager.TicksGame;
            if (now - firstArrivalStartTick < FirstArrivalFallbackDelay || now < nextFallbackAttemptTick) return;
            nextFallbackAttemptTick = now + GenDate.TicksPerHour;
            if (!FirstYearGuaranteeEligible) return;
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            UpdateYear();
            if (!schedule.Occurred) TryDeliver(map, null);
        }

        public void AfterIncident(IncidentDef source, IncidentParms original, string letterTitle)
        {
            if (!Eligible || busy || source.defName == EventName) return;
            if (FirstYearGuaranteeEligible && firstArrivalCompleted && Find.TickManager.TicksGame - firstArrivalStartTick < FirstArrivalWindow) return;
            UpdateYear();
            if (schedule.Occurred) return;
            Map map = original.target as Map;
            if (map == null || !map.IsPlayerHome) map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            RukayaRaceSettings settings = RukayaRaceMod.Settings ?? new RukayaRaceSettings();
            settings.ClampValues();
            if (!schedule.TryBeginAttempt(GenDate.YearsPassed, settings.trioInitialChance,
                settings.trioDecay, settings.trioMinimumChance, out float chance)) return;
            bool passed = Rand.Chance(chance);
            if (Prefs.DevMode) Log.Message($"[Rukaya AnnualTrio] year={schedule.Year + 1} attempt={schedule.Attempts} chance={chance:P2} source={source.defName} roll={passed}");
            if (!passed) return;
            string title = letterTitle.NullOrEmpty() ? source.LabelCap.ToString() : letterTitle;
            TryDeliver(map, title);
        }

        private void TryDeliver(Map map, string sourceTitle)
        {
            IncidentDef arrival = DefDatabase<IncidentDef>.GetNamed(EventName);
            var parms = new IncidentParms { target = map, customLetterText = sourceTitle };
            busy = true;
            try
            {
                arrival.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Error("[Rukaya AnnualTrio] Arrival failed.\n" + ex);
            }
            finally { busy = false; }
        }

        internal void ReserveArrival()
        {
            // Once spawning begins, keep the reservation even if only some pods are spawned.
            schedule.Occurred = true;
            if (FirstArrivalPending) firstArrivalCompleted = true;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) loadedFromSave = true;
            Scribe_Values.Look(ref schedule.Year, "annualTrioYear", -1);
            Scribe_Values.Look(ref schedule.Attempts, "annualTrioAttempts", 0);
            Scribe_Values.Look(ref schedule.Occurred, "annualTrioOccurred", false);
            Scribe_Values.Look(ref firstArrivalStartTick, "annualTrioFirstArrivalStartTick", -1);
            Scribe_Values.Look(ref firstArrivalCompleted, "annualTrioFirstArrivalCompleted", false);
            Scribe_Values.Look(ref nextFallbackAttemptTick, "annualTrioNextFallbackAttemptTick", 0);
        }

        [DebugAction("Rukaya", "Annual trio: log status", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogStatus()
        {
            var game = Current.Game.GetComponent<AnnualTrioGame>();
            game.UpdateYear();
            Log.Message($"[Rukaya AnnualTrio] eligible={Eligible} firstYearGuarantee={FirstYearGuaranteeEligible} year={game.schedule.Year + 1} attempts={game.schedule.Attempts} occurred={game.schedule.Occurred} firstArrivalPending={game.FirstArrivalPending}");
        }
    }

    // Hook the shared execution endpoint, not a threat/category selector: even a helpful,
    // quest, world or manually triggered incident can carry this deliberately absurd meme.
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class AnnualTrioIncidentHook
    {
        public sealed class Frame
        {
            public Frame Previous;
            public string LetterTitle;
            public bool Active;
        }
        [ThreadStatic] internal static Frame CurrentFrame;
        [ThreadStatic] private static bool suppressed;
        public static bool ExecuteWithoutRoll(IncidentWorker worker, IncidentParms parms)
        {
            bool previous = suppressed;
            suppressed = true;
            try { return worker.TryExecute(parms); }
            finally { suppressed = previous; }
        }

        public static void Prefix(IncidentWorker __instance, IncidentParms parms, out Frame __state)
        {
            __state = new Frame { Previous = CurrentFrame,
                Active = !suppressed && AnnualTrioGame.Eligible && __instance.def.defName != AnnualTrioGame.EventName
                    && !(__instance.def.requireColonistsPresent && parms.target is Map map
                        && map.mapPawns.FreeColonistsSpawnedCount == 0) };
            CurrentFrame = __state;
        }

        public static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result, Frame __state)
        {
            if (!__result || !__state.Active) return;
            try { Current.Game.GetComponent<AnnualTrioGame>().AfterIncident(__instance.def, parms, __state.LetterTitle); }
            catch (Exception ex) { Log.Error("[Rukaya AnnualTrio] Optional event hook failed.\n" + ex); }
        }

        public static Exception Finalizer(Exception __exception, Frame __state)
        {
            CurrentFrame = __state?.Previous;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
        new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
    public static class AnnualTrioLetterHook
    {
        public static void Prefix(Letter let)
        {
            var frame = AnnualTrioIncidentHook.CurrentFrame;
            if (frame?.Active == true) frame.LetterTitle = let.Label.ToString();
        }
    }

    public sealed class IncidentWorker_AnnualTrioArrival : IncidentWorker
    {
        // Roll per passenger. If the alliance was removed at world creation, its
        // passengers also arrive factionless rather than creating a new faction.
        internal static Faction ChooseArrivalFaction(Faction alliance)
            => Rand.Chance(0.75f) ? null : alliance;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !AnnualTrioGame.Eligible) return false;
            var game = Current.Game.GetComponent<AnnualTrioGame>();
            if (!game.Delivering) return false;
            var preset = (IncidentDef_RukayaTrioArrival)DefDatabase<IncidentDef>.GetNamed("Rukaya_TrioArrival");
            var worker = (IncidentWorker_RukayaTrioArrival)preset.Worker;
            IntVec3 spot = DropCellFinder.TradeDropSpot(map);
            if (!spot.IsValid) spot = DropCellFinder.RandomDropSpot(map, false);
            if (!spot.IsValid || preset.firstNames.NullOrEmpty()) return false;
            var indices = Enumerable.Range(0, preset.firstNames.Count).ToList();
            indices.Shuffle();
            int count = Rand.RangeInclusive(1, Math.Min(3, indices.Count));
            var contents = new List<Thing>();
            var names = new List<string>();
            float deathChance = RukayaRaceMod.Settings?.trioDeathChance ?? 0.35f;
            Faction alliance = Find.FactionManager.FirstFactionOfDef(RukayaScenarioDefOf.Rukaya_DrifterAlliance);
            for (int i = 0; i < count; i++)
            {
                int index = indices[i];
                // Decide faction before generation and death, independently for each passenger.
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(preset.pawnKind,
                    faction: ChooseArrivalFaction(alliance),
                    context: PawnGenerationContext.NonPlayer, tile: map.Tile,
                    forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                    fixedGender: Gender.Female, allowPregnant: false));
                worker.ConfigureCompanion(pawn, index);
                Thing cargo = pawn;
                if (Rand.Chance(deathChance))
                {
                    pawn.Kill(null);
                    cargo = pawn.Corpse;
                }
                if (cargo == null) continue;
                contents.Add(cargo);
                names.Add(preset.firstNames[index]);
            }
            if (contents.Count == 0) return false;
            game.ReserveArrival();
            // Pass a fresh list: DropPodUtility's own static tempList must not be shared.
            DropPodUtility.DropThingsNear(spot, map, contents, 110, false, false, true, false, false);
            // Localize the separator alongside the letter to retain each language's punctuation.
            string passengerNames = string.Join("Rukaya_TrioNameSeparator".Translate().ToString(), names);
            TaggedString text = parms.customLetterText.NullOrEmpty()
                ? "Rukaya_TrioStandaloneLetter".Translate(passengerNames.Named("PAWNS"))
                : def.letterText.Formatted(parms.customLetterText.Named("EVENT"), passengerNames.Named("PAWNS"));
            Find.LetterStack.ReceiveLetter(def.letterLabel, text, def.letterDef, new LookTargets(contents));
            if (Prefs.DevMode) Log.Message($"[Rukaya AnnualTrio] delivered={contents.Count} names={string.Join(",", names)}");
            return true;
        }
    }
}
