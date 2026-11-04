using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Applies the short-term move penalty of flight exhaustion.
    ///
    /// <see cref="Hediff_FlightExhaustion.ShortPenaltyActive"/> is a runtime window (arm on cast, expire after
    /// 10 seconds), so it cannot be expressed as a static hediff stage - stages are pure def data keyed on
    /// severity. This StatPart is the bridge: it is attached to <c>MoveSpeed</c> by an XML patch and contributes
    /// the offset only while the window is open.
    ///
    /// The long-term, severity-graded penalties are **not** here; they come from the stage list in
    /// <c>Rukaya_FlightExhaustion.xml</c>, which is the right place for anything keyed on severity.
    ///
    /// The patch that attaches this (1.7/Patches/Rukaya_FlightExhaustion.xml) is unconditional, so this runs
    /// for every pawn's MoveSpeed query. The lookup is a single hediff-def scan and short-circuits on the
    /// common case (no hediff), and the explanation text is only built when the stat window asks for it.
    /// </summary>
    public class StatPart_RukayaFlightExhaustion : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetExhaustion(req, out Hediff_FlightExhaustion exhaustion) && exhaustion.ShortPenaltyActive)
            {
                val += Hediff_FlightExhaustion.ShortPenaltyMoveSpeed;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!TryGetExhaustion(req, out Hediff_FlightExhaustion exhaustion) || !exhaustion.ShortPenaltyActive)
            {
                return null;
            }

            return "Rukaya_FlightExhaustion_ShortPenalty".Translate()
                   + ": "
                   + Hediff_FlightExhaustion.ShortPenaltyMoveSpeed.ToStringByStyle(
                       ToStringStyle.FloatMaxTwo, ToStringNumberSense.Offset);
        }

        /// <summary>
        /// Resolves the hediff for a stat request. Returns false for anything that is not a pawn, for pawns
        /// without the hediff, and for a non-Rukaya hediff def that happens to share the name.
        /// </summary>
        private static bool TryGetExhaustion(StatRequest req, out Hediff_FlightExhaustion exhaustion)
        {
            exhaustion = null;

            HediffDef def = RukayaFlightDefOf.Rukaya_FlightExhaustion;
            if (def == null || !(req.Thing is Pawn pawn))
            {
                return false;
            }

            exhaustion = pawn.health?.hediffSet?.GetFirstHediffOfDef(def) as Hediff_FlightExhaustion;
            return exhaustion != null;
        }
    }
}
