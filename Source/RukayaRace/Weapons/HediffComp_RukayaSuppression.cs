using Verse;

namespace RukayaRace.Weapons
{
    public class HediffComp_RukayaSuppression : HediffComp
    {
        private int lastRefreshTick = -1;

        public HediffCompProperties_RukayaSuppression Props => (HediffCompProperties_RukayaSuppression)this.props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            this.lastRefreshTick = Find.TickManager.TicksGame;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref this.lastRefreshTick, "lastRefreshTick", -1);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            int currentTick = Find.TickManager.TicksGame;
            if (this.lastRefreshTick < 0)
            {
                this.lastRefreshTick = currentTick;
            }

            if (currentTick - this.lastRefreshTick < this.Props.decayDelayTicks)
            {
                return;
            }

            this.parent.Severity -= this.Props.severityDecayPerTick;
            if (this.parent.Severity <= 0f)
            {
                this.parent.pawn.health.RemoveHediff(this.parent);
            }
        }

        public void NotifyRefreshed()
        {
            this.lastRefreshTick = Find.TickManager.TicksGame;
        }
    }
}
