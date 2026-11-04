using UnityEngine;
using Verse;
using RimWorld;

namespace RukayaRace.Weapons
{
    public class CompProperties_RukayaSignalFlare : CompProperties
    {
        public float disruptRadius = 8.9f;
        public int disruptCheckInterval = 30;
        public bool allowDisruption = true;
        public float burnRadius = 3.9f;
        public int initialStunTicks = 120;
        public FloatRange fireSizeRange = new FloatRange(0.45f, 0.8f);

        // 不要用 ThingDefOf 做字段默认值：DefOf 在 def 加载完成后才初始化。
        // 由 XML 提供，缺失时在 ResolveReferences 中兜底。
        public ThingDef fuelDef;

        public CompProperties_RukayaSignalFlare()
        {
            this.compClass = typeof(CompRukayaSignalFlare);
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            if (this.fuelDef == null)
            {
                this.fuelDef = ThingDefOf.Filth_Fuel;
            }
        }
    }
}
