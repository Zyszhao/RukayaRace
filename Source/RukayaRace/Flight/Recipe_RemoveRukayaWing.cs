using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Flight
{
    public class Recipe_RemoveRukayaWing : Recipe_RemoveBodyPart
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            foreach (BodyPartRecord part in base.GetPartsToApplyOn(pawn, recipe))
                if (part.def.defName == "Rukaya_Wing")
                    yield return part;
        }
    }
}
