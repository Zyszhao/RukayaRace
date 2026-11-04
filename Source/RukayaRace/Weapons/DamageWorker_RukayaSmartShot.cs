using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    /// <summary>
    /// Shared helpers for the smart revolver's head-lock.
    /// </summary>
    public static class RukayaSmartTargetingUtility
    {
        /// <summary>
        /// Picks one not-missing head-region part (parts carrying the FullHead group) of the victim,
        /// using the same coverage weighting the vanilla body-part lottery uses.
        /// Returns null when the victim has no hittable head region left.
        /// </summary>
        public static BodyPartRecord PickHeadRegionPart(Pawn pawn, DamageDef damageDef)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            List<BodyPartRecord> headParts = new List<BodyPartRecord>();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(
                         BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null))
            {
                if (part.groups != null && part.groups.Contains(BodyPartGroupDefOf.FullHead))
                {
                    headParts.Add(part);
                }
            }

            if (headParts.Count == 0)
            {
                return null;
            }

            if (headParts.TryRandomElementByWeight(
                    part => part.coverageAbs * part.def.GetHitChanceFactorFor(damageDef), out BodyPartRecord chosen))
            {
                return chosen;
            }

            return null;
        }
    }

    /// <summary>
    /// Damage worker for the smart revolver's own bullet damage def (Rukaya_SmartShot).
    /// The head-lock lives at the engine's native seam: DamageWorker_AddInjury.ChooseHitPart is
    /// protected virtual and is exactly the body-part lottery step for a landed shot, so no Harmony
    /// patch is needed. It runs for every connected hit in both normal and active matter mode and
    /// only affects hit resolution, never the ballistic animation.
    /// </summary>
    public class DamageWorker_RukayaSmartShot : DamageWorker_AddInjury
    {
        protected override BodyPartRecord ChooseHitPart(DamageInfo dinfo, Pawn pawn)
        {
            CompProperties_RukayaActiveMatterWeapon props =
                dinfo.Weapon?.GetCompProperties<CompProperties_RukayaActiveMatterWeapon>();
            if (props != null && props.headshotChance > 0f && Rand.Chance(props.headshotChance))
            {
                BodyPartRecord headPart = RukayaSmartTargetingUtility.PickHeadRegionPart(pawn, dinfo.Def);
                if (headPart != null)
                {
                    return headPart;
                }
            }

            return base.ChooseHitPart(dinfo, pawn);
        }
    }
}
