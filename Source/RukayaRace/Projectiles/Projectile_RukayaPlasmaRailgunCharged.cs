using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaPlasmaRailgunCharged : Projectile_RukayaExplosivePlasma
    {
        private static readonly HashSet<IntVec3> TrailCells = new HashSet<IntVec3>();
        private static readonly List<IntVec3> ExplosionCenters = new List<IntVec3>();

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!blockedByShield && this.Map != null)
            {
                this.ApplyRailExplosionTrail(this.Map);
            }

            base.Impact(hitThing, blockedByShield);
        }

        public override int DamageAmount
        {
            get
            {
                return base.DamageAmount + this.Extension.mainExplosionDamageBonus;
            }
        }

        protected override void Explode()
        {
            Map map = this.Map;
            this.Destroy(DestroyMode.Vanish);
            if (map == null)
            {
                return;
            }

            if (this.def.projectile.explosionEffect != null)
            {
                Effecter effecter = this.def.projectile.explosionEffect.Spawn();
                if (this.def.projectile.explosionEffectLifetimeTicks != 0)
                {
                    map.effecterMaintainer.AddEffecterToMaintain(effecter, this.Position.ToVector3().ToIntVec3(), this.def.projectile.explosionEffectLifetimeTicks);
                }
                else
                {
                    effecter.Trigger(new TargetInfo(this.Position, map, false), new TargetInfo(this.Position, map, false), -1);
                    effecter.Cleanup();
                }
            }

            GenExplosion.DoExplosion(
                this.Position,
                map,
                // Keep the damage area equal to the radius shown by the projectile Def.
                this.def.projectile.explosionRadius,
                this.DamageDef,
                this.launcher,
                this.DamageAmount,
                this.ArmorPenetration,
                this.def.projectile.soundExplode,
                this.equipmentDef,
                this.def,
                this.intendedTarget.Thing,
                this.def.projectile.postExplosionSpawnThingDef ?? (this.def.projectile.explosionSpawnsSingleFilth ? null : this.def.projectile.filth),
                this.def.projectile.postExplosionSpawnChance,
                this.def.projectile.postExplosionSpawnThingCount,
                this.def.projectile.postExplosionGasType,
                null,
                255,
                this.def.projectile.applyDamageToExplosionCellsNeighbors,
                this.def.projectile.preExplosionSpawnThingDef,
                this.def.projectile.preExplosionSpawnChance,
                this.def.projectile.preExplosionSpawnThingCount,
                this.def.projectile.explosionChanceToStartFire,
                this.def.projectile.explosionDamageFalloff,
                this.origin.AngleToFlat(this.destination),
                null,
                null,
                this.def.projectile.doExplosionVFX,
                this.DamageDef.expolosionPropagationSpeed,
                0f,
                true,
                this.def.projectile.postExplosionSpawnThingDefWater,
                this.def.projectile.screenShakeFactor,
                null,
                null,
                this.def.projectile.postExplosionSpawnSingleThingDef,
                this.def.projectile.preExplosionSpawnSingleThingDef);

            if (this.def.projectile.explosionSpawnsSingleFilth && this.def.projectile.filth != null && this.def.projectile.filthCount.TrueMax > 0 && Rand.Chance(this.def.projectile.filthChance) && !this.Position.Filled(map))
            {
                FilthMaker.TryMakeFilth(this.Position, map, this.def.projectile.filth, this.def.projectile.filthCount.RandomInRange, FilthSourceFlags.None, true);
            }
        }

        private void ApplyRailExplosionTrail(Map map)
        {
            IntVec3 impactCell = this.Position;
            // 方向与起点都取发射时固定下来的 origin（原版 Projectile.Launch 写入的炮口位置），
            // 不再读 launcher 的当前坐标：机械体/载具在开火后可能立刻移动，按当前位置算会让
            // "炮座身后"那段整个错位到别处。
            Vector3 originFlat = this.origin.Yto0();
            Vector3 direction = impactCell.ToVector3Shifted().Yto0() - originFlat;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            direction = direction.normalized;
            // backwardLength = **炮座身后**多少格（TestList「炮座身后 5 格起爆」），所以起点是 origin - 方向。
            IntVec3 startCell = (originFlat - direction * this.Extension.backwardLength).ToIntVec3();
            IntVec3 endCell = (impactCell.ToVector3Shifted() + direction * this.Extension.forwardLength).ToIntVec3();

            TrailCells.Clear();
            ExplosionCenters.Clear();

            List<IntVec3> centerLine = GenSight.BresenhamCellsBetween(startCell, endCell);
            int halfWidth = Mathf.Max(0, Mathf.RoundToInt((this.Extension.trailWidth - 1f) * 0.5f));
            IntVec3 perpendicular = new IntVec3(-Mathf.RoundToInt(direction.z), 0, Mathf.RoundToInt(direction.x));
            if (perpendicular == IntVec3.Zero)
            {
                perpendicular = new IntVec3(0, 0, 1);
            }

            for (int i = 0; i < centerLine.Count; i++)
            {
                IntVec3 center = centerLine[i];
                if (!center.InBounds(map))
                {
                    continue;
                }

                for (int offset = -halfWidth; offset <= halfWidth; offset++)
                {
                    IntVec3 cell = center + perpendicular * offset;
                    if (cell.InBounds(map) && TrailCells.Add(cell))
                    {
                        ExplosionCenters.Add(cell);
                    }
                }
            }

            for (int i = 0; i < ExplosionCenters.Count; i++)
            {
                IntVec3 cell = ExplosionCenters[i];
                RukayaPlasmaProjectileUtility.ThrowImpactFlash(
                    cell.ToVector3Shifted(),
                    map,
                    this.Extension.trailExplosionRadius);
                GenExplosion.DoExplosion(
                    cell,
                    map,
                    this.Extension.trailExplosionRadius,
                    this.Extension.trailDamageDef ?? this.DamageDef,
                    this.launcher,
                    this.Extension.trailExplosionDamage,
                    this.ArmorPenetration,
                    this.Extension.trailExplosionSound,
                    this.equipmentDef,
                    this.def,
                    this.intendedTarget.Thing,
                    doVisualEffects: false);
            }
        }

        private RukayaPlasmaRailgunProjectileExtension Extension =>
            this.def.GetModExtension<RukayaPlasmaRailgunProjectileExtension>() ?? new RukayaPlasmaRailgunProjectileExtension();
    }
}
