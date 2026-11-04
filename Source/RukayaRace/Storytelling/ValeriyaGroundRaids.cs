using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Storytelling
{
    // Scope the preference to natural incidents currently executing through this storyteller.
    // No persistent state, faction replacement, pawn generation or points adjustment is needed.
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.TryFire))]
    public static class ValeriyaRaidScope
    {
        private static readonly ConditionalWeakTable<IncidentParms, object> Active = new ConditionalWeakTable<IncidentParms, object>();
        internal static bool Contains(IncidentParms parms) => Active.TryGetValue(parms, out _);

        public static void Prefix(Storyteller __instance, FiringIncident fi, out bool __state)
        {
            __state = false;
            if (__instance.def.defName != "Rukaya_Storyteller_ValeriyaDrozdVikhr"
                || !(fi.def.Worker is IncidentWorker_RaidEnemy) || fi.source == null
                || fi.sourceQuestPart != null || fi.parms.quest != null || fi.parms.questScriptDef != null
                || fi.parms.forced || fi.parms.bypassStorytellerSettings || !(fi.parms.target is Map)
                || fi.parms.raidStrategy != null || fi.parms.raidArrivalMode != null || Contains(fi.parms)) return;
            Active.Add(fi.parms, new object());
            __state = true;
        }

        public static Exception Finalizer(Exception __exception, FiringIncident fi, bool __state)
        {
            if (__state) Active.Remove(fi.parms);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), nameof(IncidentWorker_RaidEnemy.ResolveRaidStrategy))]
    public static class ValeriyaGroundStrategy
    {
        private static bool IsAssault(RaidStrategyDef strategy) => strategy.defName == "Siege"
            || strategy.defName == "ImmediateAttackSappers" || strategy.defName == "ImmediateAttackBreaching";

        public static bool Prefix(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            if (!ValeriyaRaidScope.Contains(parms) || parms.faction == null
                || parms.raidStrategy != null || parms.raidArrivalMode != null) return true;
            var choices = new Dictionary<RaidStrategyDef, List<PawnsArrivalModeDef>>();
            foreach (RaidStrategyDef strategy in DefDatabase<RaidStrategyDef>.AllDefsListForReading)
            {
                IncidentParms probe = parms.ShallowCopy();
                probe.raidStrategy = strategy;
                if (!strategy.Worker.CanUseWith(probe, groupKind) || strategy.arriveModes == null) continue;
                List<PawnsArrivalModeDef> modes = strategy.arriveModes.Where(m =>
                    m.Worker is PawnsArrivalModeWorker_EdgeWalkIn && !m.defName.Contains("Debug")
                    && m.Worker.CanUseWith(probe) && m.Worker.GetSelectionWeight(probe) > 0f).ToList();
                if (modes.Count > 0) choices.Add(strategy, modes);
            }
            bool preferAssault = Rand.Chance(0.5f);
            List<RaidStrategyDef> pool = choices.Keys.Where(s => preferAssault && IsAssault(s)).ToList();
            if (pool.Count == 0) pool = choices.Keys.Where(s => !IsAssault(s)).ToList();
            // A restricted faction may support only an assault strategy. Keep it on foot
            // rather than discarding a legal ground option for an unrelated arrival mode.
            if (pool.Count == 0) pool = choices.Keys.ToList();
            if (pool.Count == 0)
            {
                if (Prefs.DevMode) Log.Message("[Rukaya Valeriya] No legal preferred ground strategy; using vanilla selection.");
                return true;
            }
            RaidStrategyDef selected = pool.RandomElementByWeight(s =>
                s.Worker.SelectionWeightForFaction(parms.target as Map, parms.faction, parms.points));
            IncidentParms selection = parms.ShallowCopy();
            selection.raidStrategy = selected;
            PawnsArrivalModeDef mode = choices[selected].RandomElementByWeight(m => m.Worker.GetSelectionWeight(selection));
            parms.raidStrategy = selected;
            parms.raidArrivalMode = mode;
            if (Prefs.DevMode) Log.Message($"[Rukaya Valeriya] preferAssault={preferAssault} faction={parms.faction.GetUniqueLoadID()} strategy={selected.defName} mode={mode.defName} input={parms.points:0.##}");
            return false;
        }
    }
}
