using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaAirburstRocket : Projectile_Explosive
    {
        private static readonly List<IntVec3> CandidateCells = new List<IntVec3>();
        private static readonly List<IntVec3> SelectedCells = new List<IntVec3>();

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            IntVec3 impactCell = this.Position;
            Map impactMap = this.Map;

            if (!blockedByShield && impactMap != null)
            {
                this.ApplyDirectDamage(hitThing);
                this.LaunchFragments(impactCell, impactMap);
            }

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplyDirectDamage(Thing hitThing)
        {
            RukayaAirburstProjectileExtension extension = this.Extension;
            if (hitThing == null || extension.directDamage <= 0f)
            {
                return;
            }

            DamageInfo damageInfo = new DamageInfo(
                extension.directDamageDef ?? this.DamageDef,
                extension.directDamage,
                extension.directArmorPenetration,
                this.ExactRotation.eulerAngles.y,
                this.launcher,
                null,
                this.equipmentDef ?? this.def,
                DamageInfo.SourceCategory.ThingOrUnknown,
                this.intendedTarget.Thing);
            hitThing.TakeDamage(damageInfo);
        }

        private void LaunchFragments(IntVec3 impactCell, Map map)
        {
            RukayaAirburstProjectileExtension extension = this.Extension;
            if (extension.fragmentProjectile == null || extension.fragmentCount <= 0)
            {
                return;
            }

            SelectedCells.Clear();
            this.SelectFragmentCells(impactCell, map, extension);

            Vector3 origin = impactCell.ToVector3Shifted();
            int maxDelayTicks = Mathf.Max(0, Mathf.RoundToInt(extension.fragmentLaunchDelayMaxSeconds * 60f));
            for (int i = 0; i < SelectedCells.Count; i++)
            {
                IntVec3 targetCell = SelectedCells[i];
                LocalTargetInfo target = this.TargetForCell(targetCell, map);
                if (maxDelayTicks > 0)
                {
                    map.GetComponent<MapComponent_RukayaAirburstFragments>().Schedule(
                        impactCell,
                        targetCell,
                        target,
                        extension.fragmentProjectile,
                        this.launcher,
                        this.preventFriendlyFire,
                        this.equipment,
                        extension.fragmentVisualSegments,
                        extension.fragmentVisualArcHeight,
                        Rand.RangeInclusive(0, maxDelayTicks));
                }
                else
                {
                    ThrowFragmentVisuals(
                        origin,
                        targetCell.ToVector3Shifted(),
                        map,
                        extension.fragmentVisualSegments,
                        extension.fragmentVisualArcHeight);
                    LaunchFragmentProjectile(
                        impactCell,
                        target,
                        map,
                        extension.fragmentProjectile,
                        this.launcher,
                        this.preventFriendlyFire,
                        this.equipment);
                }
            }

            SelectedCells.Clear();
        }

        private void SelectFragmentCells(IntVec3 impactCell, Map map, RukayaAirburstProjectileExtension extension)
        {
            if (extension.preferPawnCells)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead || pawn == this.launcher)
                    {
                        continue;
                    }

                    if (pawn.Position.InHorDistOf(impactCell, extension.fragmentSearchRadius))
                    {
                        this.TryAddSelectedCell(pawn.Position, map, extension.fragmentCount);
                    }
                }

                SelectedCells.Shuffle();
                if (SelectedCells.Count > extension.fragmentCount)
                {
                    SelectedCells.RemoveRange(extension.fragmentCount, SelectedCells.Count - extension.fragmentCount);
                }
            }

            CandidateCells.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(impactCell, extension.fragmentSearchRadius, true))
            {
                if (cell.InBounds(map) && !SelectedCells.Contains(cell))
                {
                    CandidateCells.Add(cell);
                }
            }

            CandidateCells.Shuffle();
            for (int i = 0; i < CandidateCells.Count && SelectedCells.Count < extension.fragmentCount; i++)
            {
                this.TryAddSelectedCell(CandidateCells[i], map, extension.fragmentCount);
            }

            CandidateCells.Clear();
        }

        private void TryAddSelectedCell(IntVec3 cell, Map map, int maxCount)
        {
            if (SelectedCells.Count >= maxCount || !cell.InBounds(map) || SelectedCells.Contains(cell))
            {
                return;
            }

            SelectedCells.Add(cell);
        }

        internal static void LaunchFragmentProjectile(
            IntVec3 impactCell,
            LocalTargetInfo target,
            Map map,
            ThingDef fragmentProjectile,
            Thing launcher,
            bool preventFriendlyFire,
            Thing equipment)
        {
            Projectile projectile = GenSpawn.Spawn(fragmentProjectile, impactCell, map, WipeMode.Vanish) as Projectile;
            if (projectile == null)
            {
                Log.ErrorOnce(
                    $"Rukaya airburst fragmentProjectile {fragmentProjectile.defName} is not a projectile.",
                    74610231);
                return;
            }

            projectile.Launch(
                launcher,
                impactCell.ToVector3Shifted(),
                target,
                target,
                ProjectileHitFlags.All,
                preventFriendlyFire,
                equipment,
                null);
        }

        private LocalTargetInfo TargetForCell(IntVec3 targetCell, Map map)
        {
            List<Thing> thingList = targetCell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i] is Pawn pawn && pawn != this.launcher && !pawn.Dead && !pawn.Destroyed)
                {
                    return pawn;
                }
            }

            return targetCell;
        }

        internal static void ThrowFragmentVisuals(
            Vector3 origin,
            Vector3 destination,
            Map map,
            int segmentCount,
            float arcHeight)
        {
            if (segmentCount <= 0)
            {
                return;
            }

            for (int i = 1; i <= segmentCount; i++)
            {
                float progress = (float)i / segmentCount;
                Vector3 point = Vector3.Lerp(origin, destination, progress);
                point.y = AltitudeLayer.MetaOverlays.AltitudeFor() + GenMath.InverseParabola(progress) * arcHeight;
                FleckMaker.ThrowMicroSparks(point, map);
            }
        }

        private RukayaAirburstProjectileExtension Extension =>
            this.def.GetModExtension<RukayaAirburstProjectileExtension>() ?? new RukayaAirburstProjectileExtension();
    }
}
