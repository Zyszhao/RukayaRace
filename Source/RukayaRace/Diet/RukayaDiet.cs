using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Diet
{
    public static class RukayaDietUtility
    {
        public const float MeatNutritionFactor = 0.6f;
        public const int GraceTicks = 3 * GenDate.TicksPerDay;
        public const int SevereTicks = 6 * GenDate.TicksPerDay;

        public static bool IsRukaya(Pawn pawn) => pawn?.def?.defName == "Alien_Rukaya";

        // Any includes milk/eggs. We deliberately have only two dietary categories.
        // GetFoodKind examines CompIngredients, so any meat makes a mixed meal meat food.
        public static bool IsMeat(Thing food) => food != null && FoodUtility.GetFoodKind(food) == FoodKind.Meat;

        public static bool SatisfiesVegetarianNeed(Thing food)
        {
            return food?.def?.IsNutritionGivingIngestible == true
                && !food.def.IsDrug
                && !(ModsConfig.BiotechActive && food.def == ThingDefOf.HemogenPack)
                && !IsMeat(food);
        }

        public static int LackStage(int elapsedTicks)
        {
            if (elapsedTicks >= SevereTicks)
                return 1;
            return elapsedTicks > GraceTicks ? 0 : -1;
        }
    }

    public class CompProperties_RukayaDiet : CompProperties
    {
        public CompProperties_RukayaDiet() => compClass = typeof(CompRukayaDiet);
    }

    public class CompRukayaDiet : ThingComp
    {
        private int lastVegetarianMealTick;

        public int TicksSinceVegetarianMeal => System.Math.Max(0, Find.TickManager.TicksGame - lastVegetarianMealTick);

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // Start with a grace period, but do not award a meal that was never eaten.
            lastVegetarianMealTick = Find.TickManager?.TicksGame ?? 0;
        }

        public void Notify_VegetarianMeal()
        {
            lastVegetarianMealTick = Find.TickManager.TicksGame;
            Pawn pawn = parent as Pawn;
            pawn?.needs?.mood?.thoughts.memories.TryGainMemory(RukayaDietDefOf.Rukaya_AteVegetarian);
            pawn?.needs?.mood?.thoughts.situational.Notify_SituationalThoughtsDirty();
        }

        // Need-bar debug controls edit the same clock without fabricating an eaten meal.
        public void SetElapsedTicks(int elapsedTicks)
        {
            lastVegetarianMealTick = Find.TickManager.TicksGame - System.Math.Max(0, elapsedTicks);
            (parent as Pawn)?.needs?.mood?.thoughts.situational.Notify_SituationalThoughtsDirty();
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            yield return new StatDrawEntry(StatCategoryDefOf.BasicsPawn,
                "Rukaya_DietInfoLabel".Translate(),
                "Rukaya_DietInfoValue".Translate(),
                "Rukaya_DietInfoDescription".Translate() + "\n\n"
                    + "Rukaya_DietLastVegetarianMeal".Translate(
                        GenDate.ToStringTicksToPeriod(TicksSinceVegetarianMeal)),
                2100);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastVegetarianMealTick, "lastVegetarianMealTick");
        }
    }

    public class ThoughtWorker_RukayaVegetarianLack : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!RukayaDietUtility.IsRukaya(p) || p.DevelopmentalStage.Baby())
                return ThoughtState.Inactive;
            CompRukayaDiet comp = p.TryGetComp<CompRukayaDiet>();
            int stage = comp == null ? -1 : RukayaDietUtility.LackStage(comp.TicksSinceVegetarianMeal);
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    [DefOf]
    public static class RukayaDietDefOf
    {
        public static ThoughtDef Rukaya_AteVegetarian;

        static RukayaDietDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RukayaDietDefOf));
    }
}
