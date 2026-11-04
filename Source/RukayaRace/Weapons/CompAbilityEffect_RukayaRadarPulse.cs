using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompAbilityEffect_RukayaRadarPulse : CompAbilityEffect
    {
        public new CompProperties_AbilityRukayaRadarPulse Props => (CompProperties_AbilityRukayaRadarPulse)this.props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            Pawn primaryTarget = target.Pawn;
            if (caster == null || primaryTarget == null || !primaryTarget.Spawned || primaryTarget.MapHeld == null)
            {
                return;
            }

            if (!caster.HostileTo(primaryTarget))
            {
                return;
            }

            this.ApplyEmp(primaryTarget, caster);
            this.ApplyOrRefreshHediff(primaryTarget, this.Props.lockOnHediff, this.Props.lockOnDurationTicks);
            this.ApplyOrRefreshHediff(primaryTarget, this.Props.exposedHediff, this.Props.exposedDurationTicks);
            this.DisruptInvisibility(primaryTarget);
            this.ExposeNearbyHostiles(primaryTarget, caster);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn targetPawn = target.Pawn;
            return targetPawn != null && this.parent.pawn != null && this.parent.pawn.HostileTo(targetPawn);
        }

        private void ApplyEmp(Pawn target, Pawn caster)
        {
            if (this.Props.empDamageAmount <= 0f)
            {
                return;
            }

            DamageInfo damageInfo = new DamageInfo(
                DamageDefOf.EMP,
                this.Props.empDamageAmount,
                0f,
                -1f,
                caster);
            target.TakeDamage(damageInfo);
        }

        private void ExposeNearbyHostiles(Pawn primaryTarget, Pawn caster)
        {
            Map map = primaryTarget.MapHeld;
            if (map == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn == caster || !pawn.Position.InHorDistOf(primaryTarget.Position, this.Props.exposeRadius))
                {
                    continue;
                }

                if (!caster.HostileTo(pawn))
                {
                    continue;
                }

                this.ApplyOrRefreshHediff(pawn, this.Props.exposedHediff, this.Props.exposedDurationTicks);
                this.DisruptInvisibility(pawn);
            }
        }

        private void ApplyOrRefreshHediff(Pawn pawn, HediffDef hediffDef, int durationTicks)
        {
            if (pawn == null || hediffDef == null)
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing == null)
            {
                existing = HediffMaker.MakeHediff(hediffDef, pawn);
                pawn.health.AddHediff(existing);
            }

            HediffComp_Disappears disappears = existing.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = Mathf.Max(disappears.ticksToDisappear, durationTicks);
            }
        }

        private void DisruptInvisibility(Pawn pawn)
        {
            HediffComp_Invisibility invisibility = pawn?.GetInvisibilityComp();
            if (invisibility != null)
            {
                invisibility.DisruptInvisibility();
            }
        }
    }
}
