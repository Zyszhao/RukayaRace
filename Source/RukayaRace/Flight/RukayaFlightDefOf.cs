using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Typed handles for the defs the emergency-flight ability is built from.
    ///
    /// These exist for the same reason <see cref="RukayaWingRenderDefOf"/> does: the ability def, the flyer
    /// def, the cost props and the exhaustion hediff all reference each other by defName, and a typo there is
    /// a **silent** failure (a null DefOf field means the ability simply never accumulates exhaustion, or the
    /// flyer falls back to vanilla). Keeping the names in one place makes the whole chain greppable.
    ///
    /// Every field name must match its defName exactly - <c>[DefOf]</c> injects by name after defs load, and a
    /// mismatch leaves the field null without any error.
    ///
    /// Sources:
    ///   · <c>Rukaya_EmergencyFlyer</c>      - 1.7/Defs/ThingDefs_Misc/Rukaya_PawnFlyer.xml
    ///   · <c>Rukaya_EmergencyFly</c>        - 1.7/Defs/AbilityDefs/Rukaya_Abilities.xml
    ///   · <c>Rukaya_FlightExhaustion</c>    - 1.7/Defs/HediffDefs/Rukaya_FlightExhaustion.xml
    /// </summary>
    [DefOf]
    public static class RukayaFlightDefOf
    {
        /// <summary>The tuned <c>PawnFlyer</c> subclass used by the emergency-flight verb.</summary>
        public static ThingDef Rukaya_EmergencyFlyer;

        /// <summary>The emergency-flight ability itself.</summary>
        public static AbilityDef Rukaya_EmergencyFly;

        /// <summary>Accumulating exhaustion hediff; see <see cref="Hediff_FlightExhaustion"/>.</summary>
        public static HediffDef Rukaya_FlightExhaustion;

        static RukayaFlightDefOf()
        {
            // DefOf fields are injected by name after all defs are loaded; this type has no state to set up.
            DefOfHelper.EnsureInitializedInCtor(typeof(RukayaFlightDefOf));
        }
    }
}
