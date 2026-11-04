using RimWorld;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Verb_RukayaSkyInterceptorShoot : Verb_Shoot
    {
        public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo targ)
        {
            CompRukayaSkyInterceptor interceptor = CompRukayaSkyInterceptor.ForWeapon(this);
            if (interceptor?.AirMode == true && CompRukayaSkyInterceptor.CategoryOf(targ.Thing) != RukayaAirTargetCategory.None)
                return interceptor.CanEngage(targ.Thing);
            return base.CanHitTargetFrom(root, targ);
        }

        public void BeginAirBurst(Thing target)
        {
            SetAirBurstTarget(target);
            currentDestination = LocalTargetInfo.Invalid;
            nonInterruptingSelfCast = true;
            WarmupComplete();
        }

        public void SetAirBurstTarget(Thing target)
        {
            currentTarget = target == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(target);
        }

        protected override bool TryCastShot()
        {
            CompRukayaSkyInterceptor interceptor = CompRukayaSkyInterceptor.ForWeapon(this);
            if (interceptor?.AirMode == true && (interceptor.AirBurstActive
                || CompRukayaSkyInterceptor.CategoryOf(currentTarget.Thing) != RukayaAirTargetCategory.None))
            {
                if (!interceptor.FireAirShot(this))
                    return false;
                lastShotTick = Find.TickManager.TicksGame;
                if (CasterIsPawn)
                    CasterPawn.records.Increment(RecordDefOf.ShotsFired);
                return true;
            }
            return base.TryCastShot();
        }
    }
}
