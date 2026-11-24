using System.Collections.Generic;
using Verse;

namespace RukayaRace.Flight
{
    public class RukayaWingExtension : DefModExtension
    {
        public bool canFly;
    }

    public static class RukayaWingUtility
    {
        private struct BodyWingParts
        {
            public BodyPartRecord left;
            public BodyPartRecord right;
        }

        private static BodyPartDef cachedWingPartDef;
        private static readonly Dictionary<BodyDef, BodyWingParts> BodyPartsCache = new Dictionary<BodyDef, BodyWingParts>();
        private static HashSet<ThingDef> cachedFlightCapableDefs;

        internal static HashSet<ThingDef> FlightCapableDefs
        {
            get
            {
                if (cachedFlightCapableDefs != null)
                {
                    return cachedFlightCapableDefs;
                }

                HashSet<ThingDef> set = new HashSet<ThingDef>();
                List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef def = allDefs[i];
                    if (def.HasComp(typeof(CompRukayaFlightControl)))
                    {
                        set.Add(def);
                    }
                }

                cachedFlightCapableDefs = set;
                return set;
            }
        }

        internal static void GetWingParts(BodyDef body, out BodyPartRecord left, out BodyPartRecord right)
        {
            if (body == null)
            {
                left = null;
                right = null;
                return;
            }

            if (BodyPartsCache.TryGetValue(body, out BodyWingParts cached))
            {
                left = cached.left;
                right = cached.right;
                return;
            }

            BodyPartDef wingDef = cachedWingPartDef ??= DefDatabase<BodyPartDef>.GetNamedSilentFail("Rukaya_Wing");
            BodyWingParts parts = default;
            if (wingDef != null)
            {
                List<BodyPartRecord> allParts = body.AllParts;
                for (int i = 0; i < allParts.Count; i++)
                {
                    BodyPartRecord part = allParts[i];
                    if (part == null || part.def != wingDef)
                    {
                        continue;
                    }

                    if (part.untranslatedCustomLabel == "left wing")
                    {
                        parts.left = part;
                    }
                    else if (part.untranslatedCustomLabel == "right wing")
                    {
                        parts.right = part;
                    }
                }
            }

            BodyPartsCache[body] = parts;
            left = parts.left;
            right = parts.right;
        }

        private static bool IsFlightReadyWing(HediffSet health, BodyPartRecord part)
        {
            if (part == null || health.PartIsMissing(part))
            {
                return false;
            }

            Hediff_AddedPart replacement = health.GetFirstHediffMatchingPart<Hediff_AddedPart>(part);
            if (replacement != null && replacement.def.GetModExtension<RukayaWingExtension>()?.canFly != true)
            {
                return false;
            }

            return PawnCapacityUtility.CalculatePartEfficiency(health, part) >= 0.5f;
        }

        // Rendering uses PresentWings. Flight additionally requires functioning, flight-rated parts.
        // Do not cache across health changes: surgery and damage may happen within the same game tick.
        public static PawnRenderNodeWorker_RukayaWing.Wings FlightWings(Pawn pawn)
        {
            var result = PawnRenderNodeWorker_RukayaWing.Wings.None;
            BodyDef body = pawn?.RaceProps?.body;
            HediffSet health = pawn?.health?.hediffSet;
            if (body == null || health == null)
            {
                return result;
            }

            GetWingParts(body, out BodyPartRecord left, out BodyPartRecord right);
            if (IsFlightReadyWing(health, left))
            {
                result |= PawnRenderNodeWorker_RukayaWing.Wings.Left;
            }

            if (IsFlightReadyWing(health, right))
            {
                result |= PawnRenderNodeWorker_RukayaWing.Wings.Right;
            }

            return result;
        }
    }
}
