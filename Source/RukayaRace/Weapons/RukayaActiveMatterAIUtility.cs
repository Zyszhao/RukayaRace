using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public static class RukayaActiveMatterAIUtility
    {
        public static bool IsAutonomousPawn(Pawn pawn)
        {
            if (pawn?.Faction == null)
            {
                return false;
            }

            // Faction leaders receive equipment before the player faction exists during world generation.
            // Defer AI supply until it exists; CompTickRare and the AI hooks retry initialization.
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null || pawn.Faction == playerFaction)
            {
                return false;
            }

            return !pawn.IsPlayerControlled
                && !pawn.IsColonistPlayerControlled
                && (pawn.Faction.HostileTo(playerFaction) || pawn.kindDef?.isFighter == true);
        }

        public static Thing GetCombatTarget(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            Thing target = pawn.CurJob?.targetA.Thing;
            if (IsValidHostileTarget(pawn, target))
            {
                return target;
            }

            target = pawn.mindState?.enemyTarget;
            return IsValidHostileTarget(pawn, target) ? target : null;
        }

        public static bool IsValidHostileTarget(Pawn pawn, Thing target)
        {
            return target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn?.Map
                && target != pawn
                && pawn.HostileTo(target);
        }

        public static bool ShouldUseActiveMode(Pawn pawn, Thing target, CompRukayaActiveMatterWeapon weapon)
        {
            if (pawn == null || target == null || weapon == null || !weapon.SupportsActiveMode
                || !weapon.CanUseActiveModeNow())
            {
                return false;
            }

            CompProperties_RukayaActiveMatterWeapon props = weapon.Props;
            bool policySatisfied;
            switch (props.aiModePolicy)
            {
                case RukayaActiveMatterAIPolicy.Never:
                    policySatisfied = false;
                    break;
                case RukayaActiveMatterAIPolicy.AnyHostileTarget:
                    policySatisfied = true;
                    break;
                case RukayaActiveMatterAIPolicy.GroupedTargets:
                    policySatisfied = CountHostilesNearTarget(pawn, target, props.aiGroupRadius)
                        >= UnityEngine.Mathf.Max(1, props.aiMinimumGroupTargets);
                    break;
                case RukayaActiveMatterAIPolicy.ArmoredOrGrouped:
                    policySatisfied = IsArmoredTarget(target, props.aiArmorThreshold)
                        || CountHostilesNearTarget(pawn, target, props.aiGroupRadius)
                        >= UnityEngine.Mathf.Max(1, props.aiMinimumGroupTargets);
                    break;
                default:
                    policySatisfied = IsArmoredTarget(target, props.aiArmorThreshold)
                        || pawn.Position.DistanceTo(target.Position) <= props.aiCloseTargetRange;
                    break;
            }

            if (!policySatisfied || !HasSafeImpactArea(pawn, target, weapon))
            {
                return false;
            }

            return true;
        }

        public static bool IsArmoredTarget(Thing target, float threshold)
        {
            if (target == null || threshold <= 0f)
            {
                return false;
            }

            StatDef sharpArmor = DefDatabase<StatDef>.GetNamedSilentFail("ArmorRating_Sharp");
            return sharpArmor != null && target.GetStatValue(sharpArmor, true) >= threshold;
        }

        public static int CountHostilesNearTarget(Pawn pawn, Thing target, float radius)
        {
            if (pawn?.Map == null || target == null || radius <= 0f)
            {
                return target != null && IsValidHostileTarget(pawn, target) ? 1 : 0;
            }

            int count = 0;
            foreach (Pawn candidate in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (candidate != null && !candidate.Dead && candidate != pawn
                    && candidate.Position.InHorDistOf(target.Position, radius)
                    && pawn.HostileTo(candidate))
                {
                    count++;
                }
            }

            return count;
        }

        public static bool HasSafeImpactArea(Pawn pawn, Thing target, CompRukayaActiveMatterWeapon weapon)
        {
            float radius = weapon.Props.aiFriendlyFireRadius;
            if (weapon.ActiveProjectile?.projectile != null)
            {
                radius = UnityEngine.Mathf.Max(radius, weapon.ActiveProjectile.projectile.explosionRadius);
            }
            if (radius <= 0f || pawn?.Map == null || target == null)
            {
                return true;
            }

            foreach (Pawn candidate in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (candidate == null || candidate == pawn || candidate == target || candidate.Dead)
                {
                    continue;
                }

                if (!pawn.HostileTo(candidate) && candidate.Position.InHorDistOf(target.Position, radius))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsCombatActive(Pawn pawn)
        {
            return GetCombatTarget(pawn) != null;
        }

        public static bool HasHostileNear(Pawn pawn, float radius)
        {
            if (pawn?.Map == null || radius <= 0f)
            {
                return false;
            }

            foreach (Pawn candidate in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (candidate != null && candidate != pawn && !candidate.Dead
                    && candidate.Position.InHorDistOf(pawn.Position, radius)
                    && pawn.HostileTo(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
