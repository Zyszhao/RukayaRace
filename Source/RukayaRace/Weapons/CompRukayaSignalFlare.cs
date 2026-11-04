using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompRukayaSignalFlare : ThingComp
    {
        private static HediffDef disruptorFlashDef;
        private static bool disruptorFlashResolved;

        public CompProperties_RukayaSignalFlare Props => (CompProperties_RukayaSignalFlare)this.props;

        private static HediffDef DisruptorFlashDef
        {
            get
            {
                if (!disruptorFlashResolved)
                {
                    disruptorFlashResolved = true;
                    if (ModsConfig.AnomalyActive)
                    {
                        disruptorFlashDef = DefDatabase<HediffDef>.GetNamedSilentFail("DisruptorFlash");
                    }
                }

                return disruptorFlashDef;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                if (this.Props.allowDisruption)
                {
                    this.ApplyInitialFlash();
                }

                this.IgniteArea();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (this.parent.MapHeld == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (this.Props.allowDisruption && this.Props.disruptCheckInterval > 0 && ticksGame % this.Props.disruptCheckInterval == 0)
            {
                this.DisruptNearbyInvisibility();
            }

        }

        private void ApplyInitialFlash()
        {
            Map map = this.parent.MapHeld;
            if (map == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!this.AffectsPawn(pawn, this.Props.disruptRadius))
                {
                    continue;
                }

                this.ApplyFlashToPawn(pawn);
            }
        }

        private void DisruptNearbyInvisibility()
        {
            Map map = this.parent.MapHeld;
            if (map == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!this.AffectsPawn(pawn, this.Props.disruptRadius))
                {
                    continue;
                }

                HediffComp_Invisibility invisibility = pawn.GetInvisibilityComp();
                if (invisibility != null)
                {
                    invisibility.DisruptInvisibility();
                }
            }
        }

        private void IgniteArea()
        {
            Map map = this.parent.MapHeld;
            if (map == null)
            {
                return;
            }

            RukayaIncendiaryUtility.PlaceFuelAndIgnite(
                GenRadial.RadialCellsAround(this.parent.Position, this.Props.burnRadius, true),
                map,
                this.parent,
                this.Props.fuelDef,
                this.Props.fireSizeRange);
        }

        private void ApplyFlashToPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            HediffComp_Invisibility invisibility = pawn.GetInvisibilityComp();
            if (invisibility != null)
            {
                invisibility.DisruptInvisibility();
            }

            HediffDef flashDef = DisruptorFlashDef;
            bool alreadyFlashed = flashDef != null && pawn.health.hediffSet.HasHediff(flashDef);
            if (!alreadyFlashed)
            {
                this.TryPsychicStun(pawn);
            }

            if (flashDef != null)
            {
                pawn.health.AddHediff(flashDef);
            }
        }

        private void TryPsychicStun(Pawn pawn)
        {
            float sensitivity = pawn.GetStatValue(StatDefOf.PsychicSensitivity);
            if (sensitivity <= 0f)
            {
                return;
            }

            StunHandler stunner = pawn.stances?.stunner;
            if (stunner == null)
            {
                return;
            }

            stunner.StunFor(Mathf.RoundToInt(this.Props.initialStunTicks * sensitivity), this.parent, true, true);
        }

        private bool AffectsPawn(Pawn pawn, float radius)
        {
            if (pawn == null || !pawn.Spawned || pawn.MapHeld != this.parent.MapHeld)
            {
                return false;
            }

            if (!pawn.Position.InHorDistOf(this.parent.Position, radius))
            {
                return false;
            }

            return GenSight.LineOfSightToThing(this.parent.Position, pawn, this.parent.MapHeld);
        }
    }
}
