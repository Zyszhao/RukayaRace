using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public class HediffComp_RukayaATGMGuiding : HediffComp
    {
        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            Pawn pawn = this.Pawn;
            if (pawn?.pather?.MovingNow == true)
            {
                pawn.pather.StopDead();
            }
        }
    }
}
