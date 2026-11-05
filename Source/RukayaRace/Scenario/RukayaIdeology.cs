using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    public static class RukayaIdeology
    {
        internal const string LegacyMeme = "Rukaya_VegetarianTradition";

        public static bool IsRukayaFaction(FactionDef def) => def?.defName == "Rukaya_PlayerFaction"
            || def?.defName == "Rukaya_DrifterAlliance";

        public static bool IsRukayaIdeo(Ideo ideo)
        {
            if (ideo == null) return false;
            if (ideo.memes.Any(m => m.defName == LegacyMeme)) return true;
            if (Find.GameInitData?.playerFaction is Faction player && IsRukayaFaction(player.def)
                && player.ideos?.PrimaryIdeo == ideo) return true;
            return Find.World != null && Find.FactionManager.AllFactionsListForReading.Any(f =>
                IsRukayaFaction(f.def) && f.ideos?.PrimaryIdeo == ideo);
        }

        public static bool IsApparelPrecept(PreceptDef def) => def?.preceptClass != null
            && typeof(Precept_Apparel).IsAssignableFrom(def.preceptClass);

        public static void RemoveApparelPreferences(Ideo ideo)
        {
            // replacing:true prevents RemovePrecept from generating a replacement desire.
            foreach (Precept precept in ideo.PreceptsListForReading.ToList())
            {
                if (precept is Precept_Apparel) ideo.RemovePrecept(precept, replacing: true);
                else if (precept is Precept_Role role && !role.ApparelRequirements.NullOrEmpty())
                    role.ApparelRequirements = new List<PreceptApparelRequirement>();
            }
            ideo.RecachePrecepts();
        }

        public static void SetVegetarianPrecept(Ideo ideo, FactionDef generatingFor = null)
        {
            if (ideo.classicMode || ideo.createdFromNoExpansionGame) return;
            PreceptDef vegetarian = DefDatabase<PreceptDef>.GetNamed("MeatEating_Disapproved");
            foreach (Precept precept in ideo.PreceptsListForReading
                .Where(p => p.def.issue == vegetarian.issue && p.def != vegetarian).ToList())
                ideo.RemovePrecept(precept, replacing: true);
            if (!ideo.HasPrecept(vegetarian))
                ideo.AddPrecept(PreceptMaker.MakePrecept(vegetarian), init: true, generatingFor: generatingFor);
        }
    }

    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.RandomizePrecepts))]
    internal static class Patch_RukayaIdeo_Generation
    {
        private static void Postfix(IdeoFoundation __instance, IdeoGenerationParms parms)
        {
            if (!ModsConfig.IdeologyActive || !RukayaIdeology.IsRukayaFaction(parms.forFaction)) return;
            RukayaIdeology.SetVegetarianPrecept(__instance.ideo, parms.forFaction);
            RukayaIdeology.RemoveApparelPreferences(__instance.ideo);
        }
    }

    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.CanAddForFaction))]
    internal static class Patch_RukayaIdeo_NoApparelPrecepts
    {
        private static void Postfix(IdeoFoundation __instance, PreceptDef precept, FactionDef forFaction,
            ref bool __result)
        {
            if (__result && RukayaIdeology.IsApparelPrecept(precept)
                && (RukayaIdeology.IsRukayaFaction(forFaction) || RukayaIdeology.IsRukayaIdeo(__instance.ideo)))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.GenerateNewApparelRequirements))]
    internal static class Patch_RukayaIdeo_NoRoleApparel
    {
        private static bool Prefix(Precept_Role __instance, FactionDef generatingFor,
            ref List<PreceptApparelRequirement> __result)
        {
            if (!RukayaIdeology.IsRukayaFaction(generatingFor) && !RukayaIdeology.IsRukayaIdeo(__instance.ideo))
                return true;
            __result = new List<PreceptApparelRequirement>();
            return false;
        }
    }

    [HarmonyPatch(typeof(IdeoUtility), nameof(IdeoUtility.CanUseIdeo))]
    internal static class Patch_RukayaIdeo_Reuse
    {
        private static void Postfix(FactionDef factionDef, Ideo ideo, ref bool __result)
        {
            if (!__result || !ModsConfig.IdeologyActive || !RukayaIdeology.IsRukayaFaction(factionDef)) return;
            // Reject an incompatible shared ideology instead of rewriting another faction's beliefs.
            __result = ideo.HasPrecept(DefDatabase<PreceptDef>.GetNamed("MeatEating_Disapproved"))
                && !ideo.PreceptsListForReading.Any(p => p is Precept_Apparel
                    || p is Precept_Role role && !role.ApparelRequirements.NullOrEmpty());
        }
    }

    // Also handles a culture imported/selected on the player's configuration page.
    [HarmonyPatch(typeof(FactionIdeosTracker), nameof(FactionIdeosTracker.SetPrimary))]
    internal static class Patch_RukayaIdeo_SelectPrimary
    {
        private static void Postfix(Faction ___faction, Ideo ideo)
        {
            if (ModsConfig.IdeologyActive && ideo != null && RukayaIdeology.IsRukayaFaction(___faction.def))
                RukayaIdeology.RemoveApparelPreferences(ideo);
        }
    }

    public sealed class GameComponent_RukayaIdeologyCompatibility : GameComponent
    {
        public GameComponent_RukayaIdeologyCompatibility(Game game) { }

        public override void FinalizeInit()
        {
            if (!ModsConfig.IdeologyActive) return;
            foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
            {
                bool rukaya = RukayaIdeology.IsRukayaIdeo(ideo);
                if (ideo.memes.RemoveAll(m => m.defName == RukayaIdeology.LegacyMeme) > 0)
                {
                    // Keep the existing Ideo object/id and its followers. Resolve the old
                    // MemeDef normally, then remove only that meme once references are ready.
                    RukayaIdeology.SetVegetarianPrecept(ideo);
                    ideo.RecachePrecepts();
                    ideo.RegenerateDescription();
                    Log.Message("[Rukaya] 已将旧版素食传统模因迁移为原版肉食戒律，保留文化及信徒引用。");
                }
                if (rukaya) RukayaIdeology.RemoveApparelPreferences(ideo);
            }
        }
    }
}
