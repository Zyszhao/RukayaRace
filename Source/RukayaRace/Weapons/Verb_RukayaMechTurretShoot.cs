using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    // AI jobs and the autonomous turret share one cooldown and one warmup.
    public class Verb_RukayaMechTurretShoot : Verb_Shoot
    {
        private CompRukayaLaserHeat LaserHeat => CasterPawn?.TryGetComp<CompRukayaLaserHeat>();

        public override bool TryStartCastOn(LocalTargetInfo castTarg, LocalTargetInfo destTarg,
            bool surpriseAttack = false, bool canHitNonTargetPawns = true,
            bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
        {
            CompRukayaLaserHeat heat = LaserHeat;
            if (heat != null && !heat.CanFire)
                return false;

            CompRukayaMechTurretGun turret = CasterPawn?.GetComp<CompRukayaMechTurretGun>();
            if (turret != null && turret.AttackVerb == this)
            {
                bool manualOrder = CasterPawn.CurJob?.def == JobDefOf.AttackStatic;
                if (!turret.CanStartCast || !manualOrder && !turret.AutoFireEnabled)
                    return false;
            }

            bool started = base.TryStartCastOn(castTarg, destTarg, surpriseAttack,
                canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast: true);
            if (started && turret != null && turret.AttackVerb == this)
                turret.NotifyCastStarting(castTarg);
            return started;
        }

        // Every real shot of a burst passes through here, so heat is charged per
        // projectile actually fired and a mid-burst overheat aborts the burst.
        protected override bool TryCastShot()
        {
            CompRukayaLaserHeat heat = LaserHeat;
            if (heat != null && !heat.CanFire)
                return false;

            if (!base.TryCastShot())
                return false;

            heat?.NotifyGroundShot();
            return true;
        }
    }
}
