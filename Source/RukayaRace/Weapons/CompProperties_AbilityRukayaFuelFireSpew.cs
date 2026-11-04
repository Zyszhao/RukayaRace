using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompProperties_AbilityRukayaFuelFireSpew : CompProperties_AbilityFireSpew
    {
        // 不要用 ThingDefOf 做字段默认值：DefOf 在所有 def 加载完成之后才初始化，
        // 加载期读取会拿到 null 并触发 "Tried to use an uninitialized DefOf" 警告。
        // 由 XML 提供；本类继承自 AbilityCompProperties，没有 ResolveReferences(ThingDef)
        // 可以重写，因此缺失时由 FuelDef 属性懒加载兜底。
        public ThingDef fuelDef;

        public FloatRange fireSizeRange = new FloatRange(0.55f, 0.85f);

        public ThingDef FuelDef => this.fuelDef ?? ThingDefOf.Filth_Fuel;

        public CompProperties_AbilityRukayaFuelFireSpew()
        {
            this.compClass = typeof(CompAbilityEffect_RukayaFuelFireSpew);
        }
    }
}
