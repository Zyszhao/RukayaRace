using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Research
{
    // Vanilla counts satisfied item types (0/1). Only the node display should count individual analyses.
    // Keep ResearchProjectDef's getters intact: research gating relies on their item-type semantics.
    [HarmonyPatch(typeof(MainTabWindow_Research), "DrawBottomRow")]
    public static class RukayaMemoryResearchUI
    {
        public static int Completed(ResearchProjectDef project)
        {
            if (!HasMemoryRequirement(project)) return project.AnalyzedThingsCompleted;
            int total = 0;
            foreach (ThingDef memory in project.requiredAnalyzed)
            {
                var props = memory.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>();
                if (Find.AnalysisManager.TryGetAnalysisProgress(props.analysisID, out var progress))
                    total += System.Math.Min(progress.timesDone, progress.required);
            }
            return total;
        }

        public static int Required(ResearchProjectDef project)
        {
            if (!HasMemoryRequirement(project)) return project.RequiredAnalyzedThingCount;
            int total = 0;
            foreach (ThingDef memory in project.requiredAnalyzed)
            {
                var props = memory.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>();
                total += Find.AnalysisManager.TryGetAnalysisProgress(props.analysisID, out var progress)
                    ? progress.required : props.analysisRequiredRange.min;
            }
            return total;
        }

        private static bool HasMemoryRequirement(ResearchProjectDef project)
        {
            return !project.requiredAnalyzed.NullOrEmpty() && project.requiredAnalyzed.TrueForAll(RukayaMemoryArchives.IsMemory);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var completed = AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.AnalyzedThingsCompleted));
            var required = AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.RequiredAnalyzedThingCount));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(completed))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RukayaMemoryResearchUI), nameof(Completed));
                }
                else if (instruction.Calls(required))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RukayaMemoryResearchUI), nameof(Required));
                }
                yield return instruction;
            }
        }
    }
}
