using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb))]
    public static class Patch_Pawn_TryGetAttackVerb_RukayaMech
    {
        public static void Prefix(Pawn __instance, ref bool allowTurrets, out CompRukayaMechTurretGun __state)
        {
            // Mechanoid patrol, escort and lord AI normally ignore CompTurretGun and
            // see only the ram plate. Expose this mod's ground turret to those jobs.
            // Select it explicitly in Postfix so an AA companion is never selected.
            if (!__instance.RaceProps.IsMechanoid)
            {
                __state = null;
                return;
            }

            __state = __instance.GetComp<CompRukayaMechTurretGun>();
            if (__state != null)
                allowTurrets = false;
        }

        public static void Postfix(CompRukayaMechTurretGun __state, ref Verb __result)
        {
            if (__state != null && __state.HasWeapon && !__state.TurretDestroyed && __state.AttackVerb.Available()
                && (__result == null || __result.verbProps.IsMeleeAttack))
                __result = __state.AttackVerb;
        }
    }

    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    public static class Patch_JobGiver_AIFightEnemy_RukayaMech
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result?.def != JobDefOf.Goto || !pawn.RaceProps.IsMechanoid)
                return;
            CompRukayaMechTurretGun turret = pawn.GetComp<CompRukayaMechTurretGun>();
            Thing target = pawn.mindState.enemyTarget;
            if (turret == null || !turret.CanOperate || pawn.InMentalState
                || target == null
                || !turret.AttackVerb.Available() || !turret.AttackVerb.CanHitTarget(target))
                return;

            // A valid firing position already exists: keep range instead of walking
            // toward another cover cell while the independent turret is shooting.
            pawn.pather?.StopDead();
            __result = JobMaker.MakeJob(JobDefOf.Wait_Combat,
                JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange,
                checkOverrideOnExpiry: true);
        }
    }
}
