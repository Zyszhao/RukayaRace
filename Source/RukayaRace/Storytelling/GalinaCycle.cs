using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using LudeonTK;

namespace RukayaRace.Storytelling
{
    public sealed class StorytellerCompProperties_GalinaCycle : StorytellerCompProperties
    {
        public FloatRange restDays = new FloatRange(6f, 9f);
        public StorytellerCompProperties_GalinaCycle() { compClass = typeof(StorytellerComp_GalinaCycle); }
    }

    public sealed class StorytellerComp_GalinaCycle : StorytellerComp
    {
        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            Current.Game.GetComponent<GalinaCycleGame>().Advance(this,
                ((StorytellerCompProperties_GalinaCycle)props).restDays);
            yield break;
        }
    }

    public sealed class GalinaCycleGame : GameComponent
    {
        public const string StorytellerName = "Rukaya_Storyteller_GalinaRadugaUdar";
        private int nextRaidTick = -1;
        private bool awaitingClear;
        private int earliestClearTick;
        private int lastCheckTick = -1;
        public static bool Selected => Find.Storyteller?.def?.defName == StorytellerName;
        public GalinaCycleGame(Game game) { }

        public void Advance(StorytellerComp source, FloatRange restDays)
        {
            int now = Find.TickManager.TicksGame;
            if (!Selected || lastCheckTick == now) return;
            lastCheckTick = now;
            List<Map> homes = Find.Maps.Where(m => m.IsPlayerHome).ToList();
            if (homes.Count == 0) return;
            if (nextRaidTick < 0) StartRest(now, restDays);
            bool danger = homes.Any(m => m.dangerWatcher.DangerRating != StoryDanger.None);
            if (awaitingClear)
            {
                if (now >= earliestClearTick && !danger) { awaitingClear = false; StartRest(now, restDays); }
                return;
            }
            if (now < nextRaidTick || danger || !Find.Storyteller.difficulty.allowBigThreats) return;
            Map map = homes.RandomElement();
            IncidentParms parms = source.GenerateParms(IncidentCategoryDefOf.ThreatBig, map);
            if (GalinaDropRaids.TryFire(source, parms))
            {
                awaitingClear = true;
                earliestClearTick = now + 2500;
            }
            else
            {
                // A temporarily unavailable faction/map must not cause a tight retry loop.
                nextRaidTick = now + 2500;
            }
        }

        private void StartRest(int now, FloatRange days)
        {
            nextRaidTick = now + (int)(days.RandomInRange * 60000f);
            if (Prefs.DevMode) Log.Message($"[Rukaya Galina] Rest until tick {nextRaidTick} (now {now}).");
        }

        [DebugAction("Rukaya", "Galina: expire rest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ExpireRest()
        {
            if (!Selected) return;
            GalinaCycleGame cycle = Current.Game.GetComponent<GalinaCycleGame>();
            cycle.nextRaidTick = Find.TickManager.TicksGame;
            Log.Message("[Rukaya Galina] Rest expired by debug action; active threats still block the next assault.");
        }

        [DebugAction("Rukaya", "Galina: log cycle status", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogStatus()
        {
            GalinaCycleGame cycle = Current.Game.GetComponent<GalinaCycleGame>();
            Log.Message($"[Rukaya Galina] selected={Selected} nextRaidTick={cycle.nextRaidTick} awaitingClear={cycle.awaitingClear} now={Find.TickManager.TicksGame}");
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextRaidTick, "galinaNextRaidTick", -1);
            Scribe_Values.Look(ref awaitingClear, "galinaAwaitingClear", false);
            Scribe_Values.Look(ref earliestClearTick, "galinaEarliestClearTick", 0);
        }
    }

    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.TryFire))]
    public static class GalinaNaturalThreatGate
    {
        public static bool Prefix(FiringIncident fi)
        {
            if (!GalinaCycleGame.Selected || fi.def.category != IncidentCategoryDefOf.ThreatBig) return true;
            if (fi.source is StorytellerComp_GalinaCycle) return true;
            // Quest/script incidents retain their own explicit schedule.
            if (fi.sourceQuestPart != null || fi.parms.quest != null || fi.parms.questScriptDef != null || fi.parms.forced) return true;
            return false;
        }
    }
}
