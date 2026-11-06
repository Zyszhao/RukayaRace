using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RukayaRace.Flight
{
    public static class RukayaFlightAIUtility
    {
        private const float MinimumJumpDistance = 6f;
        private const float MinimumProgress = 4f;
        private const float MaximumAIExhaustion = 2f;

        public static bool IsAutonomousPawn(Pawn pawn)
        {
            if (pawn?.Faction == null)
            {
                return false;
            }

            Faction player = Faction.OfPlayerSilentFail;
            return player != null && pawn.Faction != player
                && !pawn.IsPlayerControlled && !pawn.IsColonistPlayerControlled
                && !pawn.IsPrisoner && !pawn.IsSlave && !pawn.InMentalState
                && pawn.Spawned && !pawn.Downed && !pawn.Dead;
        }

        public static bool HasCombatTarget(Pawn pawn)
        {
            Thing target = pawn?.mindState?.enemyTarget;
            return target != null && !target.Destroyed && target.Spawned
                && target.Map == pawn.Map && pawn.HostileTo(target)
                && !(target is Pawn enemy && (enemy.Downed || enemy.Dead));
        }

        public static bool ShouldFlyWhileMoving(Pawn pawn)
        {
            if (!IsAutonomousPawn(pawn) || pawn.InBed())
            {
                return false;
            }

            JobDef job = pawn.CurJobDef;
            if (IsFleeJob(job))
            {
                return true;
            }

            bool moving = pawn.pather?.Moving == true || job == JobDefOf.Goto
                || job == JobDefOf.AttackMelee;
            if (!moving)
            {
                return false;
            }

            if (IsTradingPartyMember(pawn))
            {
                // Combat targets are handled by ShouldFlyNow before this movement-only decision.
                // A normal arrival, patrol, follow or departure must not make traders take off.
                return IsThreatenedExit(pawn, pawn.CurJob);
            }

            return true;
        }

        private static bool IsTradingPartyMember(Pawn pawn)
        {
            LordJob lordJob = pawn.GetLord()?.LordJob;
            return lordJob is LordJob_TradeWithColony
                || (lordJob is LordJob_VisitColony && pawn.TraderKind != null);
        }

        private static bool IsThreatenedExit(Pawn pawn, Job job)
        {
            // Only an actual exit job pays for the nearby-enemy search.
            return job?.def == JobDefOf.Goto && job.exitMapOnArrival
                && GenAI.EnemyIsNear(pawn, 30f, out _);
        }

        private static bool IsFleeJob(JobDef job)
        {
            return job == JobDefOf.Flee || job == JobDefOf.FleeAndCower
                || job == JobDefOf.FleeAndCowerShort;
        }

        /// <summary>
        /// Replace only an AI movement decision, before it starts. Landing lets the think tree choose
        /// the next attack/escape job normally; no current jobs or player orders are interrupted here.
        /// </summary>
        public static Job TryMakeEmergencyFlightJob(Pawn pawn, Job movement, ThinkNode giver)
        {
            if (movement == null || movement.playerForced || !IsAutonomousPawn(pawn)
                || pawn.GetComp<CompRukayaFlightControl>() == null
                || pawn.carryTracker?.CarriedThing != null)
            {
                return null;
            }

            bool melee = movement.def == JobDefOf.AttackMelee;
            bool combatMove = giver is JobGiver_AIFightEnemy
                && (movement.def == JobDefOf.Goto || melee) && HasCombatTarget(pawn);
            bool fleeing = IsFleeJob(movement.def);
            bool retreating = !combatMove && !fleeing && IsThreatenedExit(pawn, movement);
            if (!combatMove && !fleeing && !retreating)
            {
                return null;
            }

            Ability ability = pawn.abilities?.GetAbility(RukayaFlightDefOf.Rukaya_EmergencyFly);
            if (ability == null || !ability.CanCast || ability.Casting
                || pawn.GetLord()?.AbilityAllowed(ability).Accepted == false)
            {
                return null;
            }

            // Do not call Ability.GizmoDisabled: its undrafted UI gate is for player commands.
            // The effect comps own the same wing/resource restrictions used by manual casting.
            foreach (AbilityComp comp in ability.comps)
            {
                if (comp.GizmoDisabled(out _))
                {
                    return null;
                }
            }

            Hediff exhaustion = pawn.health?.hediffSet
                ?.GetFirstHediffOfDef(RukayaFlightDefOf.Rukaya_FlightExhaustion);
            if (exhaustion != null && exhaustion.Severity >= MaximumAIExhaustion)
            {
                return null;
            }

            if (!TryFindLandingCell(pawn, movement, ability, melee, out IntVec3 landing))
            {
                return null;
            }

            return ability.GetJob(landing, LocalTargetInfo.Invalid);
        }

        private static bool TryFindLandingCell(Pawn pawn, Job movement, Ability ability,
            bool melee, out IntVec3 landing)
        {
            landing = IntVec3.Invalid;
            LocalTargetInfo goal = movement.targetA;
            if (!goal.IsValid || !goal.Cell.InBounds(pawn.Map)
                || (goal.HasThing && (!goal.Thing.Spawned || goal.Thing.Map != pawn.Map)))
            {
                return false;
            }

            foreach (IntVec3 cell in LandingCandidates(pawn.Position, goal.Cell, ability.verb.EffectiveRange))
            {
                if (!JumpUtility.ValidJumpTarget(pawn, pawn.Map, cell)
                    || cell.GetFirstPawn(pawn.Map) != null || cell.ContainsStaticFire(pawn.Map)
                    || !pawn.Map.pawnDestinationReservationManager.CanReserve(cell, pawn, false)
                    || !ability.verb.ValidateTarget(cell, false))
                {
                    continue;
                }

                bool valid = true;
                foreach (CompAbilityEffect effect in ability.EffectComps)
                {
                    if (!effect.Valid(cell, false))
                    {
                        valid = false;
                        break;
                    }
                }

                // A jump must not strand the pawn on the wrong side of a wall or in a sealed room.
                if (!valid || !pawn.Map.reachability.CanReach(cell, goal,
                    melee ? PathEndMode.Touch : PathEndMode.OnCell,
                    TraverseParms.For(pawn, Danger.Deadly)))
                {
                    continue;
                }

                landing = cell;
                return true;
            }

            return false;
        }

        private static IEnumerable<IntVec3> LandingCandidates(IntVec3 start, IntVec3 goal, float range)
        {
            float distance = start.DistanceTo(goal);
            float reach = Mathf.Min(range - 0.5f, distance);
            // At most seven distances with nine nearby cells each, even if another mod changes range.
            int samples = 0;
            for (float step = reach; step >= MinimumJumpDistance && samples < 7; step -= 4f, samples++)
            {
                float fraction = step / distance;
                IntVec3 center = new IntVec3(
                    Mathf.RoundToInt(start.x + (goal.x - start.x) * fraction), 0,
                    Mathf.RoundToInt(start.z + (goal.z - start.z) * fraction));
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 1.5f, true))
                {
                    float jumpDistance = start.DistanceTo(cell);
                    if (jumpDistance >= MinimumJumpDistance && jumpDistance <= range
                        && cell.DistanceTo(goal) <= distance - MinimumProgress)
                    {
                        yield return cell;
                    }
                }
            }
        }
    }
}
