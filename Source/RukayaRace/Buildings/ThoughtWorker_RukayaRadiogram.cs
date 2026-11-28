using RimWorld;
using Verse;

namespace RukayaRace.Buildings
{
    public sealed class ThoughtWorker_RukayaRadiogram : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (!pawn.Spawned || !pawn.Awake() || pawn.health?.capacities == null
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing))
                return ThoughtState.Inactive;

            Room room = pawn.GetRoom();
            Map map = pawn.Map;
            if (room == null || map == null) return ThoughtState.Inactive;

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDef.Named("Rukaya_Radiogram")))
            {
                Building building = thing as Building;
                CompPowerTrader power = building?.TryGetComp<CompPowerTrader>();
                if (building != null && power?.PowerOn == true && building.GetRoom() == room
                    && pawn.Position.InHorDistOf(building.Position, 6f))
                    return ThoughtState.ActiveDefault;
            }

            return ThoughtState.Inactive;
        }
    }

    public sealed class CompProperties_RukayaRadiogramRadius : CompProperties
    {
        public CompProperties_RukayaRadiogramRadius() => compClass = typeof(CompRukayaRadiogramRadius);
    }

    public sealed class CompRukayaRadiogramRadius : ThingComp
    {
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(parent.Position, 6f);
        }
    }
}
