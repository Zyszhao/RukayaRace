using System.Collections.Generic;
using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaArcShockGrenade : Projectile_Explosive
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

            if (RukayaLightningUtility.ShouldUseLightningForElectricWeapons)
            {
                GenClamor.DoClamor(this, 12f, ClamorDefOf.Impact);
                if (!blockedByShield && this.def.projectile.landedEffecter != null)
                {
                    this.def.projectile.landedEffecter.Spawn(this.ExactPosition.ToIntVec3(), map, 1f).Cleanup();
                }

                this.Destroy(DestroyMode.Vanish);
                if (blockedByShield || map == null)
                {
                    return;
                }

                RukayaLightningUtility.Strike(this.cachedImpactCell, this.cachedMap);
                this.TriggerArcShock();
                return;
            }

            base.Impact(hitThing, blockedByShield);

            if (blockedByShield || map == null)
            {
                return;
            }

            this.TriggerArcShock();
        }

        private void TriggerArcShock()
        {
            if (this.cachedMap == null || !this.cachedImpactCell.IsValid)
            {
                return;
            }

            List<Pawn> targets = this.cachedTargets ?? new List<Pawn>();
            Vector3 origin = this.cachedImpactCell.ToVector3Shifted();

            for (int i = 0; i < 4; i++)
            {
                FleckMaker.ThrowMicroSparks(origin, this.cachedMap);
                FleckMaker.ThrowLightningGlow(origin, this.cachedMap, this.Extension.lightningGlowSize);
            }

            foreach (Pawn target in targets)
            {
                if (target == null || target.Destroyed || target.Dead || target.MapHeld != this.cachedMap)
                {
                    continue;
                }

                this.ApplyDamage(target, this.cachedLauncherPawn);
                this.ScheduleArcVisual(origin, target, this.cachedMap);
            }

            this.cachedTargets = null;
        }

        private List<Pawn> GetHostileTargets(IntVec3 impactCell, Map map, Pawn casterPawn)
        {
            List<Pawn> candidates = new List<Pawn>();
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

                if (!pawn.Position.InHorDistOf(impactCell, this.Extension.searchRadius))
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

        private void ApplyDamage(Pawn target, Pawn casterPawn)
        {
            if (target?.health == null)
            {
                return;
            }

            Thing instigator = casterPawn ?? this.launcher;
            ThingDef weaponDef = this.equipmentDef ?? this.def;

            if (RukayaLightningUtility.ShouldUseLightningForElectricWeapons)
            {
                RukayaLightningUtility.Strike(target.Position, target.Map);
                return;
            }

            if (this.Extension.directDamage > 0)
            {
                DamageInfo damageInfo = new DamageInfo(
                    DamageDefOf.Flame,
                    this.Extension.directDamage,
                    this.Extension.directArmorPenetration,
                    -1f,
                    instigator,
                    null,
                    weaponDef,
                    DamageInfo.SourceCategory.ThingOrUnknown,
                    target);
                target.TakeDamage(damageInfo);
            }

            if (this.Extension.empDamage > 0f)
            {
                DamageInfo empInfo = new DamageInfo(
                    DamageDefOf.EMP,
                    this.Extension.empDamage,
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

        private void ScheduleArcVisual(Vector3 origin, Pawn target, Map map)
        {
            if (target == null || !target.Spawned || map == null)
            {
                return;
            }

            FleckMaker.ThrowLightningGlow(target.TrueCenter(), map, this.Extension.lightningGlowSize);
            FleckMaker.ThrowMicroSparks(target.TrueCenter(), map);

            map.GetComponent<MapComponent_RukayaLightningArcs>()?.ScheduleArc(
                origin,
                target.TrueCenter(),
                Mathf.Max(1, this.Extension.arcSegmentsPerTarget),
                this.Extension.arcJitterRadius,
                ArcVisualTicks);
        }

        private RukayaArcShockProjectileExtension Extension =>
            this.def.GetModExtension<RukayaArcShockProjectileExtension>() ?? new RukayaArcShockProjectileExtension();
    }
}
