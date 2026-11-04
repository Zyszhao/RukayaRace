using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Cast verb for emergency flight.
    ///
    /// Swaps vanilla's <c>JumpFlyerDef</c> for our own ThingDef, so the ability
    /// reuses the whole vanilla jump pipeline (start-to-end interpolation, shadow, landing dust, cast and
    /// landing sounds, effecter, and the fact that a forced landing cancels the job queue) with the flight
    /// feel tuned in <c>Rukaya_EmergencyFlyer</c>.
    ///
    /// Movement is inherited from <see cref="Verb_CastAbilityJump"/>. Recheck the requirements at cast
    /// time as well: AI and player warmups must fail safely if a wing or resource is lost meanwhile.
    ///   · <c>TryCastShot</c>      - calls <c>JumpUtility.DoJump</c>
    ///   · <c>OnGUI</c>            - draws the cannot-shoot cursor for invalid cells
    ///   · <c>ValidateTarget</c>   - line of sight + <c>JumpUtility.ValidJumpTarget</c>
    ///   · <c>DrawHighlight</c>    - target highlight and the reachable radius ring
    ///   · <c>OrderForceTarget</c> - right-click ordering through <c>JumpUtility.OrderJump</c>
    ///
    /// </summary>
    public class Verb_CastRukayaEmergencyFly : Verb_CastAbilityJump
    {
        public override ThingDef JumpFlyerDef => RukayaFlightDefOf.Rukaya_EmergencyFlyer;

        protected override bool TryCastShot()
        {
            if (!ability.CanCast || !ValidateTarget(currentTarget, false))
            {
                return false;
            }

            foreach (CompAbilityEffect effect in ability.EffectComps)
            {
                if (!effect.Valid(currentTarget, false))
                {
                    return false;
                }
            }

            return base.TryCastShot();
        }
    }
}
