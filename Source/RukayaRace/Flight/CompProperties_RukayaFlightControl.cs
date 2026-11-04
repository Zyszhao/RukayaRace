using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// XML tunables for the drafted flight stance. The wing visuals themselves are not configurable here any
    /// more - they live in the race's render tree (1.6/Defs/PawnRenderTreeDefs/Rukaya_PawnRenderTrees.xml) and
    /// in the flap animation (1.6/Defs/AnimationDefs/Rukaya_WingFly.xml).
    /// </summary>
    public class CompProperties_RukayaFlightControl : CompProperties
    {
        /// <summary>Body part that must be present (on at least one side) for flight and for the flap animation.</summary>
        public BodyPartDef wingBodyPart;

        /// <summary>Food level percentage required to take off. 0 disables the check.</summary>
        public float hungerPctThresholdCanFly = 0.1f;

        /// <summary>Food consumed per second while airborne.</summary>
        public float hungerPctCostPerSecondFly = 0.001f;

        /// <summary>
        /// Extra airborne hunger cost per point of flight-exhaustion severity, as a fraction of the base rate.
        /// 0.25 means "at severity 3.0 hovering costs 1.75x". 0 disables the link.
        ///
        /// This closes the loop between the two flight systems: emergency flight builds exhaustion, and
        /// exhaustion makes sustained hovering more expensive, so a pawn that panic-jumps repeatedly pays twice.
        /// </summary>
        public float exhaustedHungerCostFactor = 0.25f;

        /// <summary>Hediff applied while airborne. Null = none.</summary>
        public HediffDef inFlightHediff;

        public CompProperties_RukayaFlightControl()
        {
            compClass = typeof(CompRukayaFlightControl);
        }
    }
}
