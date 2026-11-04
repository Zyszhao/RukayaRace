using System.Collections.Generic;
using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    // Active-matter (charge mode) guided missile for the ATGM launcher.
    // Keeps the full guided homing/radar-lock behavior of the base guided missile,
    // and on impact layers the two effects this mod already ships elsewhere:
    //  - ignition: attach fire to the directly struck thing, then lay fuel and force
    //    fire across the impact zone (same path as the incendiary canisters);
    //  - chain electrocution: strike up to maxTargets hostile pawns around the impact
    //    cell with burn + EMP damage (or a real lightning bolt per target when the
    //    mod's electric-lightning option is on) and draw lightning arcs through
    //    MapComponent_RukayaLightningArcs, mirroring the grenade launcher warhead.
    public class Projectile_RukayaChargedATGM : Projectile_RukayaGuidedATGM
    {
        private const int ArcVisualTicks = 12;

        private List<Pawn> cachedTargets;
        private IntVec3 cachedImpactCell = IntVec3.Invalid;
        private Map cachedMap;
        private Pawn cachedLauncherPawn;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            IntVec3 impactCell = this.Position;
            Map map = this.Map;
            Pawn launcherPawn = this.launcher as Pawn;

            this.cachedImpactCell = impactCell;
            this.cachedMap = map;
            this.cachedLauncherPawn = launcherPawn;
            this.cachedTargets = map == null ? null : this.GetHostileTargets(impactCell, map, launcherPawn);

            // Direct ignition of the struck thing (like the igniting rounds), done
            // before the blast so the primary target is already burning.
            if (!blockedByShield
                && hitThing != null
                && hitThing.CanEverAttachFire()
                && this.Extension.directFireSize > 0f)
            {
                hitThing.TryAttachFire(this.Extension.directFireSize, this.launcher);
            }

            base.Impact(hitThing, blockedByShield);

            if (blockedByShield || map == null)
            {
                return;
            }

            this.IgniteImpactZone();
            this.TriggerChainShock();
        }

        private void IgniteImpactZone()
        {
            if (this.cachedMap == null || !this.cachedImpactCell.IsValid)
            {
                return;
            }

            RukayaChargedATGMProjectileExtension extension = this.Extension;
            if (extension.igniteRadius <= 0f)
            {
                return;
            }

            RukayaIncendiaryUtility.PlaceFuelAndIgnite(
                GenRadial.RadialCellsAround(this.cachedImpactCell, extension.igniteRadius, true),
                this.cachedMap,
                this.launcher ?? this,
                extension.fuelDef,
                extension.fireSizeRange);
        }

        private void TriggerChainShock()
        {
            if (this.cachedMap == null || !this.cachedImpactCell.IsValid)
            {
                return;
            }

            RukayaChargedATGMProjectileExtension extension = this.Extension;
            List<Pawn> targets = this.cachedTargets ?? new List<Pawn>();
            Vector3 origin = this.cachedImpactCell.ToVector3Shifted();

            for (int i = 0; i < 4; i++)
            {
                FleckMaker.ThrowMicroSparks(origin, this.cachedMap);
                FleckMaker.ThrowLightningGlow(origin, this.cachedMap, extension.lightningGlowSize);
            }

            foreach (Pawn target in targets)
            {
                if (target == null || target.Destroyed || target.Dead || target.MapHeld != this.cachedMap)
                {
                    continue;
                }

                this.ApplyDamage(target, this.cachedLauncherPawn);
                this.ScheduleArcVisual(origin, target);
            }

            this.cachedTargets = null;
        }

        private void ApplyDamage(Pawn target, Pawn casterPawn)
        {
            if (target?.health == null)
            {
                return;
            }

            Thing instigator = casterPawn ?? this.launcher;
            ThingDef weaponDef = this.equipmentDef ?? this.def;
            RukayaChargedATGMProjectileExtension extension = this.Extension;

            if (RukayaLightningUtility.ShouldUseLightningForElectricWeapons)
            {
                RukayaLightningUtility.Strike(target.Position, target.Map);
                return;
            }

            if (extension.directDamage > 0)
            {
                DamageInfo burnInfo = new DamageInfo(
                    DamageDefOf.Flame,
                    extension.directDamage,
                    extension.directArmorPenetration,
                    -1f,
                    instigator,
                    null,
                    weaponDef,
                    DamageInfo.SourceCategory.ThingOrUnknown,
                    target);
                target.TakeDamage(burnInfo);
            }

            if (extension.empDamage > 0f)
            {
                DamageInfo empInfo = new DamageInfo(
                    DamageDefOf.EMP,
                    extension.empDamage,
                    0f,
                    -1f,
                    instigator,
                    null,
                    weaponDef,
                    DamageInfo.SourceCategory.ThingOrUnknown,
                    target);
                target.TakeDamage(empInfo);
            }
        }

        private List<Pawn> GetHostileTargets(IntVec3 impactCell, Map map, Pawn casterPawn)
        {
            List<Pawn> candidates = new List<Pawn>();
            float searchRadius = this.Extension.searchRadius;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn == casterPawn)
                {
                    continue;
                }

                if (!this.IsValidHostileTarget(pawn, casterPawn))
                {
                    continue;
                }

                if (!pawn.Position.InHorDistOf(impactCell, searchRadius))
                {
                    continue;
                }

                candidates.Add(pawn);
            }

            candidates.SortBy(pawn => pawn.Position.DistanceToSquared(impactCell));
            if (candidates.Count > this.Extension.maxTargets)
            {
                candidates.RemoveRange(this.Extension.maxTargets, candidates.Count - this.Extension.maxTargets);
            }

            return candidates;
        }

        private bool IsValidHostileTarget(Pawn pawn, Pawn casterPawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (casterPawn != null)
            {
                if (pawn == casterPawn)
                {
                    return false;
                }

                return casterPawn.HostileTo(pawn);
            }

            if (this.launcher != null && pawn == this.launcher)
            {
                return false;
            }

            if (this.intendedTarget.IsValid && this.intendedTarget.Thing is Pawn intendedPawn)
            {
                return pawn.HostileTo(intendedPawn) || intendedPawn.HostileTo(pawn);
            }

            Faction launcherFaction = this.launcher?.Faction;
            return launcherFaction != null && pawn.Faction != null && launcherFaction.HostileTo(pawn.Faction);
        }

        private void ScheduleArcVisual(Vector3 origin, Pawn target)
        {
            if (target == null || !target.Spawned || this.cachedMap == null)
            {
                return;
            }

            RukayaChargedATGMProjectileExtension extension = this.Extension;
            FleckMaker.ThrowLightningGlow(target.TrueCenter(), this.cachedMap, extension.lightningGlowSize);
            FleckMaker.ThrowMicroSparks(target.TrueCenter(), this.cachedMap);

            this.cachedMap.GetComponent<MapComponent_RukayaLightningArcs>()?.ScheduleArc(
                origin,
                target.TrueCenter(),
                Mathf.Max(1, extension.arcSegmentsPerTarget),
                extension.arcJitterRadius,
                ArcVisualTicks);
        }

        private RukayaChargedATGMProjectileExtension Extension =>
            this.def.GetModExtension<RukayaChargedATGMProjectileExtension>()
            ?? new RukayaChargedATGMProjectileExtension();
    }
}
