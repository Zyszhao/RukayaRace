using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Diet
{
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class RukayaVegetarianNeedFilterPatch
    {
        public static void Postfix(NeedDef nd, Pawn ___pawn, ref bool __result)
        {
            // Preserve all vanilla life-stage/health/ideology checks. Do not rely on
            // requiredComps, whose vanilla validation only runs with Anomaly enabled.
            if (nd.needClass == typeof(Need_RukayaVegetarian))
                __result &= RukayaDietUtility.IsRukaya(___pawn) && !___pawn.DevelopmentalStage.Baby();
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.NutritionForEater))]
    public static class RukayaDietNutritionPatch
    {
        public static void Postfix(Pawn eater, Thing food, ref float __result)
        {
            // The base amount calculation also uses this method: AI and actual absorption agree.
            if (RukayaDietUtility.IsRukaya(eater) && RukayaDietUtility.IsMeat(food))
                __result *= RukayaDietUtility.MeatNutritionFactor;
        }
    }

    [HarmonyPatch(typeof(Corpse), "IngestedCalculateAmounts")]
    public static class RukayaDietCorpseNutritionPatch
    {
        // Corpse overrides the base calculation and does not call NutritionForEater.
        public static void Prefix(Pawn ingester, ref float nutritionWanted)
        {
            if (RukayaDietUtility.IsRukaya(ingester))
                nutritionWanted /= RukayaDietUtility.MeatNutritionFactor;
        }

        public static void Postfix(Pawn ingester, ref float nutritionIngested)
        {
            if (RukayaDietUtility.IsRukaya(ingester))
                nutritionIngested *= RukayaDietUtility.MeatNutritionFactor;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class RukayaDietIngestedPatch
    {
        public static void Prefix(Thing __instance, Pawn ingester, out bool __state)
        {
            // Capture ingredients before Ingested destroys the last item in a stack.
            __state = !__instance.Destroyed && __instance.IngestibleNow
                && RukayaDietUtility.IsRukaya(ingester)
                && RukayaDietUtility.SatisfiesVegetarianNeed(__instance);
        }

        public static void Postfix(Pawn ingester, float __result, bool __state)
        {
            // Runs for map ingestion, patient feeding and caravan ingestion through Thing.Ingested.
            // An interrupted or failed meal with no nutrition must not grant the memory.
            if (__state && __result > 0f && !ingester.Dead)
                ingester.TryGetComp<CompRukayaDiet>()?.Notify_VegetarianMeal();
        }
    }
}
