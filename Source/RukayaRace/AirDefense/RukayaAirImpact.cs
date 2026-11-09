using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public static class RukayaAirImpact
    {
        public static bool RequiresProjectileHit(Thing target)
        {
            if (!(target is Skyfaller skyfaller))
                return false;
            return skyfaller.def?.defName == "MeteoriteIncoming"
                || skyfaller.def?.defName == "MeteoriteCraterIncoming";
        }

        public static bool HasMeteoriteInterceptWindow(int remainingTicks)
        {
            return remainingTicks > 15;
        }

        /// <summary>
        /// 结算一次空中拦截。
        ///
        /// <paramref name="alreadyCommitted"/> = true 表示**这次拦截在开火那一刻就已经定论**
        /// （见 <see cref="CompRukayaSkyInterceptor.FireAirShot"/>）：本方法只负责把结果播出来，
        /// 不再重新过一次 <c>CanTarget</c>。否则会出现"拦截弹飞到目标跟前时，目标刚好进入
        /// 陨石最后 15 tick 落地段 / 已经不再满足某个条件"，让一次注定命中的拦截白白失效——
        /// 这正是"拦截行为与拦截动画解耦"要消掉的风险。
        /// 仍然保留"目标已经不存在"的兜底，以及首领任务空投仓的强制保护。
        /// </summary>
        public static bool TryIntercept(Thing target, Thing launcher, Faction faction,
            RukayaSkyCrashSettings settings, Vector3 shotPosition, bool alreadyCommitted = false)
        {
            if (target == null || target.Destroyed || !target.Spawned)
                return false;
            if (!alreadyCommitted && !CompRukayaSkyInterceptor.CanTarget(target, faction, target?.Map))
                return false;
            if (target is Skyfaller skyfaller)
            {
                if (skyfaller is DropPodIncoming)
                    return DestroyDropPod(skyfaller);
                if (!Thing_RukayaSkyCrash.IsNatural(skyfaller.def))
                    return Thing_RukayaSkyCrash.TryIntercept(skyfaller, launcher, faction, settings, alreadyCommitted);
                if (RequiresProjectileHit(skyfaller))
                    return TryScatterMeteorite(skyfaller, settings);
                Map map = skyfaller.Map;
                Vector3 hitPosition = skyfaller.DrawPos;
                skyfaller.Destroy(DestroyMode.Vanish);
                if (!skyfaller.Destroyed)
                {
                    Log.Error($"[Rukaya] Natural skyfaller {skyfaller.def.defName} ({skyfaller.GetType().FullName}) could not be destroyed.");
                    return false;
                }
                for (int i = 0; i < 6; i++)
                    FleckMaker.ThrowDustPuff(hitPosition + Gen.RandomHorizontalVector(1.5f), map, 1.5f);
                return true;
            }
            if (target is Projectile projectile)
                return Thing_RukayaAirborneDebris.TryIntercept(projectile, shotPosition);
            if (target is PawnFlyer flyer)
                return Thing_RukayaFallingFlyer.TryIntercept(flyer, launcher, settings, shotPosition);
            return false;
        }

        private static bool DestroyDropPod(Skyfaller pod)
        {
            // 即使旧存档中的拦截弹已经发射，也不能跳过任务保护。
            // Vanish 不会触发首领死亡、芯片掉落和任务击杀信号。
            if (RukayaBossgroupDropPodProtection.IsProtected(pod))
                return false;
            Map map = pod.Map;
            Vector3 position = pod.DrawPos;
            // Skyfaller.Destroy 会销毁 ActiveTransporter；后者递归销毁乘员和货物。
            // 不生成坠毁载体、不寻找落地点，也不释放本已击毁的载荷。
            pod.Destroy(DestroyMode.Vanish);
            if (!pod.Destroyed)
                return false;
            FleckMaker.ThrowLightningGlow(position, map, 2f);
            for (int i = 0; i < 6; i++)
                FleckMaker.ThrowDustPuff(position + Gen.RandomHorizontalVector(1f), map, 1.5f);
            return true;
        }

        private static bool TryScatterMeteorite(Skyfaller source, RukayaSkyCrashSettings settings)
        {
            Map map = source.Map;
            List<ThingDef> oreDefs = new List<ThingDef>();
            foreach (Thing thing in source.innerContainer)
            {
                if (!thing.def.mineable || thing.def.building?.mineableThing == null)
                {
                    Log.ErrorOnce($"[Rukaya] Cannot fragment meteorite {source.def.defName} ({source.GetType().FullName}) with unsupported payload {thing.def.defName}; original descent retained.", source.thingIDNumber ^ 177231);
                    return false;
                }
                oreDefs.Add(thing.def);
            }
            List<IntVec3> cells = FindMeteoriteFragmentCells(source.Position, map, settings.meteoriteScatterRadius,
                oreDefs.Count > 0 ? Math.Min(settings.meteoriteFragmentCount.RandomInRange, oreDefs.Count)
                    : settings.meteoriteFragmentCount.RandomInRange);
            if (cells.Count < 2)
            {
                Log.ErrorOnce($"[Rukaya] No scattered landing cells for meteorite {source.def.defName} ({source.GetType().FullName}); original descent retained.", source.thingIDNumber ^ 177232);
                return false;
            }
            ThingDef fragmentDef = DefDatabase<ThingDef>.GetNamed("Rukaya_InterceptedMeteoriteFragment");
            Vector3 hitPosition = source.DrawPos;
            List<Skyfaller> fragments = new List<Skyfaller>();
            int oreIndex = 0;
            try
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    Thing_RukayaMeteoriteFragment fragment = (Thing_RukayaMeteoriteFragment)ThingMaker.MakeThing(fragmentDef);
                    fragments.Add(fragment);
                    int oreCount = settings.meteoriteOrePerFragment.RandomInRange;
                    if (oreDefs.Count > 0)
                        oreCount = Math.Min(oreCount, oreDefs.Count - oreIndex - (cells.Count - i - 1));
                    for (int j = 0; j < oreCount; j++)
                    {
                        ThingDef oreDef = oreDefs.Count > 0 ? oreDefs[oreIndex++] : ThingDefOf.MineableSteel;
                        Thing ore = ThingMaker.MakeThing(oreDef);
                        if (ore is Building mineable)
                            mineable.canChangeTerrainOnDestroyed = false;
                        if (fragment.innerContainer.TryAdd(ore, false))
                            continue;
                        ore.Destroy(DestroyMode.Vanish);
                        throw new InvalidOperationException($"Cannot add mineral {oreDef.defName} to meteorite fragment.");
                    }
                    if (GenSpawn.Spawn(fragment, cells[i], map) != fragment || !fragment.Spawned)
                        throw new InvalidOperationException($"Cannot spawn meteorite fragment at {cells[i]}.");
                    fragment.InitializeFlight(hitPosition, source.Position);
                    fragment.angle = (fragment.Position.ToVector3Shifted() - hitPosition).AngleFlat() + 90f;
                }
                source.Destroy(DestroyMode.Vanish);
                if (!source.Destroyed)
                    throw new InvalidOperationException("Original meteorite could not be destroyed.");
            }
            catch (Exception exception)
            {
                if (source.Destroyed)
                {
                    Log.Error($"[Rukaya] Meteorite {source.def.defName} ({source.GetType().FullName}) was destroyed with an error after its fragments spawned: {exception}");
                    return true;
                }
                foreach (Skyfaller fragment in fragments)
                {
                    if (!fragment.Destroyed)
                        fragment.Destroy(DestroyMode.Vanish);
                }
                Log.Error($"[Rukaya] Failed to fragment meteorite {source.def.defName} ({source.GetType().FullName}): {exception}");
                return false;
            }
            for (int i = 0; i < 8; i++)
                FleckMaker.ThrowDustPuff(hitPosition + Gen.RandomHorizontalVector(1.5f), map, 1.5f);
            return true;
        }

        private static List<IntVec3> FindMeteoriteFragmentCells(IntVec3 center, Map map, float radius, int desired)
        {
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell != center && cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map)
                    && cell.GetEdifice(map) == null && cell.GetFirstPawn(map) == null)
                    candidates.Add(cell);
            }
            List<IntVec3> selected = new List<IntVec3>();
            while (selected.Count < desired)
            {
                List<IntVec3> spaced = new List<IntVec3>();
                foreach (IntVec3 cell in candidates)
                {
                    bool separated = true;
                    foreach (IntVec3 previous in selected)
                    {
                        if ((cell - previous).LengthHorizontalSquared >= 9)
                            continue;
                        separated = false;
                        break;
                    }
                    if (separated)
                        spaced.Add(cell);
                }
                if (spaced.Count == 0)
                    break;
                IntVec3 chosen = spaced[Rand.Range(0, spaced.Count)];
                selected.Add(chosen);
                candidates.Remove(chosen);
            }
            return selected;
        }
    }
}
