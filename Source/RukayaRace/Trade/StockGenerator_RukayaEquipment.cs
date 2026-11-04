using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RukayaRace.Trade
{
    // The tagged pool limits the products; quality is intentionally independent of NPC apparel.
    public class StockGenerator_RukayaEquipment : StockGenerator_Tag
    {
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            foreach (Thing item in base.GenerateThings(forTile, faction))
            {
                item.TryGetComp<CompQuality>()?.SetQuality(
                    Rand.Bool ? QualityCategory.Normal : QualityCategory.Good, ArtGenerationContext.Outsider);
                yield return item;
            }
        }
    }
}
