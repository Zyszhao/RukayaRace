using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Apparel
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.CanDrawNow))]
    internal static class Patch_PawnRenderNodeWorker_Apparel_Body_CanDrawNow
    {
        private static readonly ThingDef VacsuitDef = DefDatabase<ThingDef>.GetNamedSilentFail("Rukaya_Vacsuit");
        private static readonly ThingDef PowerArmorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Rukaya_Armour");
        private static readonly HashSet<ThingDef> HiddenLayerApparelDefs = BuildHiddenLayerDefs();

        private static HashSet<ThingDef> BuildHiddenLayerDefs()
        {
            HashSet<ThingDef> result = new HashSet<ThingDef>();
            ApparelLayerDef rukayaOnSkin = DefDatabase<ApparelLayerDef>.GetNamedSilentFail("Rukaya_OnSkin");
            ApparelLayerDef rukayaUnderwear = DefDatabase<ApparelLayerDef>.GetNamedSilentFail("Rukaya_Underwear");
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                List<ApparelLayerDef> layers = def.apparel?.layers;
                if (layers == null)
                {
                    continue;
                }

                for (int j = 0; j < layers.Count; j++)
                {
                    ApparelLayerDef layer = layers[j];
                    if (layer == ApparelLayerDefOf.OnSkin
                        || (rukayaOnSkin != null && layer == rukayaOnSkin)
                        || (rukayaUnderwear != null && layer == rukayaUnderwear))
                    {
                        result.Add(def);
                        break;
                    }
                }
            }

            return result;
        }

        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result || node?.apparel == null || parms.pawn?.apparel == null)
            {
                return;
            }

            List<RimWorld.Apparel> wornApparel = parms.pawn.apparel.WornApparel;
            if (wornApparel.Count <= 1 || !HiddenLayerApparelDefs.Contains(node.apparel.def))
            {
                return;
            }

            for (int i = 0; i < wornApparel.Count; i++)
            {
                ThingDef def = wornApparel[i].def;
                if ((VacsuitDef != null && def == VacsuitDef) || (PowerArmorDef != null && def == PowerArmorDef))
                {
                    __result = false;
                    return;
                }
            }
        }
    }
}
