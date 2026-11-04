using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Storytelling
{
    internal static class GalinaDropRaids
    {
        public const float DropChance = 0.9f;
        public const float PointsFactor = 1.3f;

        public static bool IsDrop(PawnsArrivalModeDef mode)
        {
            if (mode == null || mode.defName.Contains("Debug")) return false;
            PawnsArrivalModeWorker worker = mode.Worker;
            return worker is PawnsArrivalModeWorker_CenterDrop
                || worker is PawnsArrivalModeWorker_EdgeDrop
                || worker is PawnsArrivalModeWorker_EdgeDropGroups
                || worker is PawnsArrivalModeWorker_RandomDrop
                || worker is PawnsArrivalModeWorker_SpecificLocationDrop;
        }

        private static bool IsCenter(PawnsArrivalModeDef mode) => mode?.Worker is PawnsArrivalModeWorker_CenterDrop;

        private static IncidentParms Fresh(IncidentParms original, float points)
        {
            IncidentParms p = original.ShallowCopy();
            p.points = points;
            p.faction = null;
            p.raidStrategy = null;
            p.raidArrivalMode = null;
            p.spawnCenter = IntVec3.Invalid;
            p.pawnGroups = null;
            p.pawnGroupMakerSeed = null;
            return p;
        }

        // Select a compatible strategy first, then an arrival mode using vanilla weights.
        // Neither CanUseWith nor spawn-centre resolution generates any pawns.
        private static bool TryPlan(IncidentParms p, bool dropsOnly, bool excludeCenter, bool groundOnly = false)
        {
            var choices = new Dictionary<RaidStrategyDef, List<PawnsArrivalModeDef>>();
            PawnGroupKindDef kind = p.pawnGroupKind ?? PawnGroupKindDefOf.Combat;
            foreach (RaidStrategyDef strategy in DefDatabase<RaidStrategyDef>.AllDefsListForReading)
            {
                p.raidStrategy = strategy;
                p.raidArrivalMode = null;
                if (!strategy.Worker.CanUseWith(p, kind) || strategy.arriveModes == null) continue;
                var modes = strategy.arriveModes.Where(m => (!dropsOnly || IsDrop(m)) && (!groundOnly || !IsDrop(m))
                    && (!excludeCenter || !IsCenter(m)) && !m.defName.Contains("Debug")
                    && m.Worker.CanUseWith(p) && ModeWeight(p, m) > 0f).ToList();
                if (modes.Count > 0) choices.Add(strategy, modes);
            }
            while (choices.Count > 0)
            {
                RaidStrategyDef strategy = choices.Keys.RandomElementByWeight(s =>
                    s.Worker.SelectionWeightForFaction(p.target as Map, p.faction, p.points));
                p.raidStrategy = strategy;
                List<PawnsArrivalModeDef> modes = choices[strategy];
                while (modes.Count > 0)
                {
                    PawnsArrivalModeDef mode = modes.RandomElementByWeight(m => ModeWeight(p, m));
                    modes.Remove(mode);
                    p.raidArrivalMode = mode;
                    p.spawnCenter = IntVec3.Invalid;
                    p.pawnGroups = null;
                    if (mode.Worker.TryResolveRaidSpawnCenter(p)
                        && (!dropsOnly || IsDrop(p.raidArrivalMode))
                        && (!excludeCenter || !IsCenter(p.raidArrivalMode))
                        && p.raidArrivalMode.Worker.CanUseWith(p)) return true;
                }
                choices.Remove(strategy);
            }
            p.raidStrategy = null;
            p.raidArrivalMode = null;
            return false;
        }

        private static float ModeWeight(IncidentParms p, PawnsArrivalModeDef mode)
        {
            float weight = mode.Worker.GetSelectionWeight(p);
            if (p.target.Tile.Valid && p.target.Tile.LayerDef.isSpace)
                weight = Mathf.Max(weight, mode.minSpaceSelectionWeight);
            return weight;
        }

        private static bool SelectFaction(IncidentParms p, bool dropsOnly, out Faction faction, bool groundOnly = false)
        {
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            return PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroupWeighted(p, out faction,
                f =>
                {
                    IncidentParms probe = p.ShallowCopy();
                    probe.faction = f;
                    if (!worker.FactionCanBeGroupSource(f, probe)) return false;
                    // A group needs a non-center option so all later batches remain legal.
                    return TryPlan(probe, dropsOnly, dropsOnly, groundOnly);
                }, allowNonHostileToPlayer: true, allowHidden: true, allowDefeated: true);
        }

        public static bool TryFire(StorytellerComp source, IncidentParms original)
        {
            if (!GalinaCycleGame.Selected || !(original.target is Map)) return false;
            float basePoints = original.points;
            bool replace = Rand.Chance(DropChance);
            IncidentParms normal = null;
            Faction faction;
            if (!replace)
            {
                normal = Fresh(original, basePoints);
                if (!SelectFaction(normal, false, out faction)) return false;
                normal.faction = faction;
                if (!TryPlan(normal, false, false)) return false;
                if (!IsDrop(normal.raidArrivalMode)) return FireNormal(source, normal, basePoints, "normal");
                // Naturally selected drops also use the same 2-3 batch rule.
            }

            IncidentParms selection = Fresh(original, basePoints * PointsFactor);
            if (normal != null)
            {
                faction = normal.faction;
            }
            else if (!SelectFaction(selection, true, out faction))
            {
                normal = Fresh(original, basePoints);
                // Prefer a genuine ground fallback so an isolated center-only mode cannot
                // create a single drop in violation of the batch rule.
                if (!SelectFaction(normal, false, out faction, true)) return false;
                normal.faction = faction;
                if (!TryPlan(normal, false, false, true)) return false;
                return FireNormal(source, normal, basePoints, "no legal drop group; fallback");
            }

            int count = Rand.RangeInclusive(2, 3);
            var batches = new List<IncidentParms>();
            bool centerUsed = false;
            for (int i = 0; i < count; i++)
            {
                IncidentParms p = Fresh(original, basePoints * PointsFactor);
                p.faction = faction;
                if (!TryPlan(p, true, centerUsed)) return false;
                centerUsed |= IsCenter(p.raidArrivalMode);
                batches.Add(p);
            }
            // Check the incident once for the whole assault. Subsequent waves are part of
            // that same assault, so vanilla minRefireDays must not cancel wave 2 or 3.
            if (!IncidentDefOf.RaidEnemy.Worker.CanFireNow(batches[0])) return false;
            int fired = 0;
            foreach (IncidentParms p in batches)
            {
                float input = p.points;
                var incident = new FiringIncident(IncidentDefOf.RaidEnemy, source, p);
                bool success = fired == 0
                    ? Find.Storyteller.TryFire(incident)
                    : AnnualTrioIncidentHook.ExecuteWithoutRoll(IncidentDefOf.RaidEnemy.Worker, p);
                if (!success)
                {
                    Log.Warning($"[Rukaya Galina] Drop batch {fired + 1}/{count} failed; completed batches will not be repeated.");
                    break;
                }
                if (fired > 0) p.target.StoryState.Notify_IncidentFired(incident);
                fired++;
                Trace($"batch={fired}/{count} faction={p.faction.GetUniqueLoadID()} mode={p.raidArrivalMode.defName} base={basePoints:0.##} input={input:0.##} actual={p.points:0.##}");
            }
            return fired > 0;
        }

        private static bool FireNormal(StorytellerComp source, IncidentParms p, float basePoints, string reason)
        {
            bool result = Find.Storyteller.TryFire(new FiringIncident(IncidentDefOf.RaidEnemy, source, p));
            Trace($"{reason}: success={result} faction={p.faction?.GetUniqueLoadID()} mode={p.raidArrivalMode?.defName} base={basePoints:0.##} actual={p.points:0.##}");
            return result;
        }
        private static void Trace(string message) { if (Prefs.DevMode) Log.Message("[Rukaya Galina] " + message); }
    }
}
