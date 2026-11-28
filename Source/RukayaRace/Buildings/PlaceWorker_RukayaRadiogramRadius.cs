using RimWorld;
using Verse;

namespace RukayaRace.Buildings
{
    public sealed class PlaceWorker_RukayaRadiogramRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            GenDraw.DrawRadiusRing(loc, 6f);
            return true;
        }
    }
}
