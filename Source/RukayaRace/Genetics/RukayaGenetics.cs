using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Genetics
{
    public static class RukayaGenetics
    {
        internal const string XenotypeName = "Rukaya_Xenotype";
        internal const string LegacyPhysiology = "Rukaya_LegacyPhysiology";

        internal static bool IsRukaya(Pawn pawn) => pawn?.def?.defName == "Alien_Rukaya";

        internal static void RemoveLegacyPhysiology(Pawn pawn)
        {
            HediffDef legacy = DefDatabase<HediffDef>.GetNamed(LegacyPhysiology);
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs.Where(h => h.def == legacy).ToList())
                pawn.health.RemoveHediff(hediff);
        }

        internal static bool IsUnmodifiedBaseliner(Pawn pawn)
        {
            Pawn_GeneTracker genes = pawn.genes;
            // Old vanilla generation can leave cosmetic hair/melanin genes. Treat all
            // other endogenes, named custom xenotypes and any xenogenes as player edits.
            return genes != null && genes.Xenotype == XenotypeDefOf.Baseliner
                && string.IsNullOrEmpty(genes.xenotypeName) && genes.iconDef == null && genes.Xenogenes.Count == 0
                && genes.Endogenes.All(g => g.def.biostatMet == 0
                    && (g.def.displayCategory?.defName == "HairColor"
                        || g.def.defName.StartsWith("Skin_Melanin", StringComparison.Ordinal)));
        }
    }

    // This method selects the xenotype before a NEW pawn's biography and genes are
    // generated. It does not rewrite reused world pawns or run on each spawn.
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GetXenotypeForGeneratedPawn))]
    public static class Patch_RukayaDefaultXenotype
    {
        [HarmonyPrefix]
        public static bool Prefix(PawnGenerationRequest request, ref XenotypeDef __result)
        {
            if (!ModsConfig.BiotechActive || request.KindDef?.race?.defName != "Alien_Rukaya"
                || request.ForcedXenotype != null || request.ForcedCustomXenotype != null
                || !request.AllowedXenotypes.NullOrEmpty() || request.ForceBaselinerChance > 0f)
                return true;

            // Explicit gene-only requests start from Baseliner instead of silently
            // acquiring our default package underneath the requested custom genes.
            __result = !request.ForcedEndogenes.NullOrEmpty() || !request.ForcedXenogenes.NullOrEmpty()
                ? XenotypeDefOf.Baseliner
                : DefDatabase<XenotypeDef>.GetNamed(RukayaGenetics.XenotypeName);
            return false;
        }
    }

    public sealed class CompProperties_RukayaGenetics : CompProperties
    {
        public CompProperties_RukayaGenetics() => compClass = typeof(CompRukayaGenetics);
    }

    public sealed class CompRukayaGenetics : ThingComp
    {
        // New pawns begin at the current version. Missing fields in old saves load as
        // zero. Per-pawn serialization also covers corpses and nested thing holders.
        private int migrationVersion = 1;
        private bool defaultMigrationPending;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref migrationVersion, "rukayaGeneticsVersion", 0);
            Scribe_Values.Look(ref defaultMigrationPending, "rukayaDefaultGeneticsMigrationPending", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && migrationVersion < 1)
                LongEventHandler.ExecuteWhenFinished(MigrateLegacyPawn);
        }

        private void MigrateLegacyPawn()
        {
            if (migrationVersion >= 1 || parent is not Pawn pawn || !RukayaGenetics.IsRukaya(pawn)) return;
            try
            {
                XenotypeDef xenotype = DefDatabase<XenotypeDef>.GetNamed(RukayaGenetics.XenotypeName);
                if (ModsConfig.BiotechActive && (defaultMigrationPending || RukayaGenetics.IsUnmodifiedBaseliner(pawn)))
                {
                    // Retain the selected route if a third-party gene hook interrupts
                    // migration; a retry must not mistake our partial additions for edits.
                    defaultMigrationPending = true;
                    foreach (GeneDef gene in xenotype.genes)
                        pawn.genes.AddGene(gene, xenogene: false);
                    // Changes only identity, never clears xenogenes or existing genes.
                    pawn.genes.SetXenotypeDirect(xenotype);
                    RukayaGenetics.RemoveLegacyPhysiology(pawn);
                }
                else if (pawn.genes?.Xenotype != xenotype)
                {
                    HediffDef legacy = DefDatabase<HediffDef>.GetNamed(RukayaGenetics.LegacyPhysiology);
                    if (!pawn.health.hediffSet.HasHediff(legacy)) pawn.health.AddHediff(legacy);
                }
                migrationVersion = 1;
                defaultMigrationPending = false;
            }
            catch (Exception exception)
            {
                Log.Error("[Rukaya] 卢卡娅种旧档迁移失败，将在下次读档时重试：" + pawn.ThingID + "\n" + exception);
            }
        }
    }

    // An intentional later conversion to the complete new package must not keep
    // old-base compensation. Direct identity-only changes must not remove it.
    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.SetXenotype))]
    public static class Patch_RukayaXenotypeConversion
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, XenotypeDef xenotype)
        {
            if (RukayaGenetics.IsRukaya(___pawn) && xenotype?.defName == RukayaGenetics.XenotypeName
                && xenotype.genes.All(g => ___pawn.genes.Endogenes.Any(e => e.def == g)))
                RukayaGenetics.RemoveLegacyPhysiology(___pawn);
        }
    }
}
