using Verse;

namespace RukayaRace.Flight
{
    public class RukayaWingExtension : DefModExtension
    {
        public bool canFly;
    }

    public static class RukayaWingUtility
    {
        // Rendering uses PresentWings. Flight additionally requires functioning, flight-rated parts.
        // Do not cache across health changes: surgery and damage may happen within the same game tick.
        public static PawnRenderNodeWorker_RukayaWing.Wings FlightWings(Pawn pawn)
        {
            var result = PawnRenderNodeWorker_RukayaWing.Wings.None;
            if (pawn?.RaceProps?.body == null || pawn.health?.hediffSet == null)
                return result;

            HediffSet health = pawn.health.hediffSet;
            foreach (BodyPartRecord part in pawn.RaceProps.body.AllParts)
            {
                if (part.def.defName != "Rukaya_Wing" || health.PartIsMissing(part))
                    continue;
                Hediff_AddedPart replacement = health.GetFirstHediffMatchingPart<Hediff_AddedPart>(part);
                if (replacement != null && replacement.def.GetModExtension<RukayaWingExtension>()?.canFly != true)
                    continue;
                if (PawnCapacityUtility.CalculatePartEfficiency(health, part) < 0.5f)
                    continue;

                if (part.untranslatedCustomLabel == "left wing")
                    result |= PawnRenderNodeWorker_RukayaWing.Wings.Left;
                else if (part.untranslatedCustomLabel == "right wing")
                    result |= PawnRenderNodeWorker_RukayaWing.Wings.Right;
            }
            return result;
        }
    }
}
