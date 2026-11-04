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
    }
}
