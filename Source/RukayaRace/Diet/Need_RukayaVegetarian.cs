using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Diet
{
    public class Need_RukayaVegetarian : Need
    {
        private CompRukayaDiet Diet => pawn.TryGetComp<CompRukayaDiet>();

        public Need_RukayaVegetarian(Pawn pawn) : base(pawn)
        {
            threshPercents = new List<float> { 0.5f };
        }

        // This is a view of CompRukayaDiet's clock, not a second resource to decay/save.
        public override float CurLevel
        {
            get => Math.Clamp(1f - (float)(Diet?.TicksSinceVegetarianMeal ?? 0) / RukayaDietUtility.SevereTicks, 0f, 1f);
            set => Diet?.SetElapsedTicks((int)Math.Round((1f - Math.Clamp(value, 0f, 1f)) * RukayaDietUtility.SevereTicks));
        }

        public override bool ShowOnNeedList => base.ShowOnNeedList
            && RukayaDietUtility.IsRukaya(pawn) && !pawn.DevelopmentalStage.Baby() && Diet != null;

        // Called both during base construction and again by Pawn_NeedsTracker.
        // Adding/recreating the display must never reset the clock or grant mood.
        public override void SetInitialLevel() { }

        public override void NeedInterval() { }

        public override void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
        }

        public override string GetTipString()
        {
            int elapsedTicks = Diet?.TicksSinceVegetarianMeal ?? 0;
            int stage = RukayaDietUtility.LackStage(elapsedTicks);
            string state = (stage < 0 ? "Rukaya_DietNeedGrace" : stage == 0 ? "Rukaya_DietNeedLack" : "Rukaya_DietNeedSevere").Translate();
            string next;
            if (stage < 0)
                next = "Rukaya_DietNeedNextMild".Translate(GenDate.ToStringTicksToPeriod(RukayaDietUtility.GraceTicks + 1 - elapsedTicks));
            else if (stage == 0)
                next = "Rukaya_DietNeedNextSevere".Translate(GenDate.ToStringTicksToPeriod(RukayaDietUtility.SevereTicks - elapsedTicks));
            else
                next = "Rukaya_DietNeedAtMinimum".Translate();

            return LabelCap + ": " + CurLevelPercentage.ToStringPercent() + "\n" + def.description
                + "\n\n" + "Rukaya_DietLastVegetarianMeal".Translate(GenDate.ToStringTicksToPeriod(elapsedTicks))
                + "\n" + state + "\n" + next;
        }
    }
}
