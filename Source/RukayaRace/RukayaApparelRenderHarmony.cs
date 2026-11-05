using HarmonyLib;
using RimWorld;
using Verse;

namespace RukayaRace.Apparel
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.CanDrawNow))]
    internal static class Patch_PawnRenderNodeWorker_Apparel_Body_CanDrawNow
    {
        private const string VacsuitDefName = "Rukaya_Vacsuit";
        private const string PowerArmorDefName = "Rukaya_Armour";
        private const string RukayaOnSkinLayerDefName = "Rukaya_OnSkin";
        private const string RukayaUnderwearLayerDefName = "Rukaya_Underwear";

        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result || node?.apparel == null || parms.pawn?.apparel == null)
            {
                return;
            }

            if (!UsesHiddenLayer(node.apparel))
            {
                return;
            }

            foreach (RimWorld.Apparel worn in parms.pawn.apparel.WornApparel)
            {
                string defName = worn.def.defName;
                if (defName == VacsuitDefName || defName == PowerArmorDefName)
                {
                    __result = false;
                    return;
                }
            }
        }

        private static bool UsesHiddenLayer(RimWorld.Apparel apparel)
        {
            foreach (ApparelLayerDef layer in apparel.def.apparel.layers)
            {
                if (layer == ApparelLayerDefOf.OnSkin
                    || layer.defName == RukayaOnSkinLayerDefName
                    || layer.defName == RukayaUnderwearLayerDefName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
