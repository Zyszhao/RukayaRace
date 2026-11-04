using RimWorld;
using Verse;

namespace RukayaRace.Buildings
{
    public class Building_RukayaFabricator : Building_WorkTableAutonomous
    {
        public override bool CanWork()
        {
            return Spawned && !Destroyed && CurrentlyUsableForBills();
        }
    }
}
