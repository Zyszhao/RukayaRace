using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompRukayaSignalFlareAnomaly : ThingComp
    {
        public CompProperties_RukayaSignalFlareAnomaly Props => (CompProperties_RukayaSignalFlareAnomaly)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad || !ModsConfig.AnomalyActive || this.parent.MapHeld == null)
            {
                return;
            }

            foreach (Pawn pawn in this.parent.MapHeld.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.Spawned || pawn.MapHeld != this.parent.MapHeld)
                {
                    continue;
                }

                if (!pawn.Position.InHorDistOf(this.parent.Position, this.Props.radius))
                {
                    continue;
                }

                if (!GenSight.LineOfSightToThing(this.parent.Position, pawn, this.parent.MapHeld))
                {
                    continue;
                }

                CompRevenant revenant = pawn.TryGetComp<CompRevenant>();
                if (revenant != null && pawn.IsPsychologicallyInvisible())
                {
                    revenant.SetState(RevenantState.Escape);
                }
            }
        }
    }
}
