using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public class HediffComp_RukayaRadarExpose : HediffComp
    {
        public HediffCompProperties_RukayaRadarExpose Props => (HediffCompProperties_RukayaRadarExpose)this.props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            this.DisruptInvisibility();
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            if (this.Props.disruptIntervalTicks <= 0)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick % this.Props.disruptIntervalTicks == 0)
            {
                this.DisruptInvisibility();
            }
        }

        private void DisruptInvisibility()
        {
            HediffComp_Invisibility invisibility = this.Pawn?.GetInvisibilityComp();
            if (invisibility != null)
            {
                invisibility.DisruptInvisibility();
            }
        }
    }
}
