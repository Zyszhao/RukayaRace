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
        private readonly AnnualTrioSchedule schedule = new AnnualTrioSchedule();
        private bool busy;
        internal bool Delivering => busy;
        public AnnualTrioGame(Game game) { }

        public static bool Eligible => Current.Game != null
            && (Find.Storyteller?.def?.defName == GalinaCycleGame.StorytellerName
                || Find.Storyteller?.def?.defName == "Rukaya_Storyteller_ValeriyaDrozdVikhr")
            && Find.Scenario != null && !Find.Scenario.AllParts.Any(p => p is ScenPart_RukayaKinRelations
                || p is ScenPart_IncidentBase incident && incident.Incident?.defName == "Rukaya_TrioArrival");

        private void UpdateYear()
        {
            schedule.RefreshYear(GenDate.YearsPassed);
        }

        public void AfterIncident(IncidentDef source, IncidentParms original, string letterTitle)
        {
            if (!Eligible || busy || source.defName == EventName) return;
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
            IncidentDef arrival = DefDatabase<IncidentDef>.GetNamed(EventName);
            IncidentParms parms = new IncidentParms { target = map };
            string title = letterTitle.NullOrEmpty() ? source.LabelCap.ToString() : letterTitle;
            parms.customLetterText = title;
            busy = true;
            // Reserve before spawning, so reentrant events cannot create a second group.
            schedule.Occurred = true;
            try
            {
                if (!arrival.Worker.TryExecute(parms)) schedule.Occurred = false;
            }
            catch (Exception ex)
            {
                // Spawning may have partially completed. Keep the reservation to avoid duplicates.
                Log.Error("[Rukaya AnnualTrio] Arrival failed; annual reservation retained to prevent duplicate pods.\n" + ex);
            }
            finally { busy = false; }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref schedule.Year, "annualTrioYear", -1);
            Scribe_Values.Look(ref schedule.Attempts, "annualTrioAttempts", 0);
            Scribe_Values.Look(ref schedule.Occurred, "annualTrioOccurred", false);
        }

        [DebugAction("Rukaya", "Annual trio: log status", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogStatus()
        {
            var game = Current.Game.GetComponent<AnnualTrioGame>();
            game.UpdateYear();
            Log.Message($"[Rukaya AnnualTrio] eligible={Eligible} year={game.schedule.Year + 1} attempts={game.schedule.Attempts} occurred={game.schedule.Occurred}");
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
            if (!(parms.target is Map map) || !AnnualTrioGame.Eligible
                || !Current.Game.GetComponent<AnnualTrioGame>().Delivering) return false;
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
                    faction: ChooseArrivalFaction(alliance), context: PawnGenerationContext.NonPlayer, tile: map.Tile,
                    forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                    fixedGender: Gender.Female, allowPregnant: false));
                worker.ConfigureCompanion(pawn, index);
                bool dead = Rand.Chance(deathChance);
                Thing cargo = pawn;
                if (dead)
                {
                    pawn.Kill(null);
                    cargo = pawn.Corpse;
                }
                if (cargo == null) continue;
                contents.Add(cargo);
                names.Add(preset.firstNames[index]);
            }
            if (contents.Count == 0) return false;
            // Pass a fresh list: DropPodUtility's own static tempList must not be shared.
            DropPodUtility.DropThingsNear(spot, map, contents, 110, false, false, true, false, false);
            string sourceTitle = parms.customLetterText.NullOrEmpty() ? def.LabelCap.ToString() : parms.customLetterText;
            // Localize the separator alongside the letter to retain each language's punctuation.
            string passengerNames = string.Join("Rukaya_TrioNameSeparator".Translate().ToString(), names);
            TaggedString text = def.letterText.Formatted(sourceTitle.Named("EVENT"), passengerNames.Named("PAWNS"));
            Find.LetterStack.ReceiveLetter(def.letterLabel, text, def.letterDef, new LookTargets(contents));
            if (Prefs.DevMode) Log.Message($"[Rukaya AnnualTrio] delivered={contents.Count} names={string.Join(",", names)}");
            return true;
        }
    }
}
