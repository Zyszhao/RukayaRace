using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public class GameComponent_RukayaActiveMatterAI : GameComponent
    {
        private const int ScanIntervalTicks = 180;
        private const float SafeReloadRadius = 25f;

        public GameComponent_RukayaActiveMatterAI(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager.TicksGame % ScanIntervalTicks != 0)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    this.TryReloadPawn(pawn);
                }
            }
        }

        private void TryReloadPawn(Pawn pawn)
        {
            if (!RukayaActiveMatterAIUtility.IsAutonomousPawn(pawn) || pawn.equipment?.Primary == null
                || pawn.Downed || pawn.InMentalState || pawn.Drafted)
            {
                return;
            }

            CompRukayaActiveMatterWeapon weapon =
                pawn.equipment.Primary.GetComp<CompRukayaActiveMatterWeapon>();
            if (weapon == null || weapon.Props.aiModePolicy == RukayaActiveMatterAIPolicy.Never)
            {
                return;
            }

            weapon.InitializeAISupplyIfNeeded(pawn);
            if (weapon.GetAutoReloadContainerCount(int.MaxValue) <= 0
                || pawn.inventory?.innerContainer == null
                || !HasReloadContainerInInventory(pawn, weapon.Props.activeMatterThingDef))
            {
                return;
            }

            if (RukayaActiveMatterAIUtility.IsCombatActive(pawn)
                || RukayaActiveMatterAIUtility.HasHostileNear(pawn, SafeReloadRadius))
            {
                return;
            }

            int loaded = weapon.ReloadFromInventory(pawn);
            if (loaded > 0)
            {
                weapon.SetAILastDecision("Rukaya_ActiveMatterAI_Reloaded");
            }
        }

        private static bool HasReloadContainerInInventory(Pawn pawn, ThingDef containerDef)
        {
            if (containerDef == null)
            {
                return false;
            }

            ThingOwner<Thing> inv = pawn.inventory.innerContainer;
            for (int i = 0; i < inv.Count; i++)
            {
                Thing item = inv[i];
                if (item.def == containerDef && item.stackCount > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
