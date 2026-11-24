using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public static class RukayaPlasmaProjectileUtility
    {
        // Use a near-white cyan so the impact flash reads clearly over terrain and explosion effects.
        private static readonly Color ImpactFlashColor = new Color(0.62f, 1f, 1f, 1f);
        private static bool glowDefsResolved;
        private static FleckDef cachedImpactFlashDef;
        private static FleckDef cachedLightningGlowDef;

        internal static FleckDef LightningGlowDef
        {
            get
            {
                EnsureGlowDefs();
                return cachedLightningGlowDef;
            }
        }

        private static void EnsureGlowDefs()
        {
            if (glowDefsResolved)
            {
                return;
            }

            glowDefsResolved = true;
            cachedLightningGlowDef = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
            cachedImpactFlashDef = DefDatabase<FleckDef>.GetNamedSilentFail("Rukaya_PlasmaImpactFlash")
                ?? cachedLightningGlowDef;
        }

        public static void ThrowImpactFlash(Vector3 position, Map map, float size)
        {
            if (map == null)
            {
                return;
            }

            size = Mathf.Max(0.1f, size);

            EnsureGlowDefs();
            FleckDef glowDef = cachedImpactFlashDef;
            if (glowDef == null)
            {
                FleckMaker.ThrowLightningGlow(position, map, size);
                return;
            }

            FleckCreationData data = FleckMaker.GetDataStatic(position, map, glowDef, size);
            data.instanceColor = ImpactFlashColor;
            map.flecks.CreateFleck(data);
        }
    }
}
