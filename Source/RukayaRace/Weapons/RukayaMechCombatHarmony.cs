using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb))]
    public static class Patch_Pawn_TryGetAttackVerb_RukayaMech
    {
        public static void Prefix(Pawn __instance, ref bool allowTurrets)
        {
            // Mechanoid patrol, escort and lord AI normally ignore CompTurretGun and
            // see only the ram plate. Expose this mod's ground turret to those jobs.
            // Select it explicitly in Postfix so an AA companion is never selected.
            if (__instance.GetComp<CompRukayaMechTurretGun>() != null)
                allowTurrets = false;
        }

        public static void Postfix(Pawn __instance, ref Verb __result)
        {
            CompRukayaMechTurretGun turret = __instance.GetComp<CompRukayaMechTurretGun>();
            if (turret != null && turret.HasWeapon && !turret.TurretDestroyed && turret.AttackVerb.Available()
                && (__result == null || __result.verbProps.IsMeleeAttack))
                __result = turret.AttackVerb;
        }
    }

    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    public static class Patch_JobGiver_AIFightEnemy_RukayaMech
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            CompRukayaMechTurretGun turret = pawn.GetComp<CompRukayaMechTurretGun>();
            Thing target = pawn.mindState.enemyTarget;
            if (turret == null || !turret.CanOperate || pawn.InMentalState
                || __result?.def != JobDefOf.Goto || target == null
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
