using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public class GameComponent_RukayaActiveMatterAutoReload : GameComponent
    {
        private const int ScanInterval = 180;

        public GameComponent_RukayaActiveMatterAutoReload(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager.TicksGame % ScanInterval != 0)
            {
                return;
            }

            List<Pawn> pawns = PawnsFinder.HomeMaps_FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                this.TryIssueReloadJob(pawns[i]);
            }
        }

        private void TryIssueReloadJob(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || pawn.Dead || pawn.Downed || pawn.InMentalState)
            {
                return;
            }

            if (pawn.equipment?.Primary == null)
            {
                return;
            }

            CompRukayaActiveMatterWeapon weaponComp = pawn.equipment.Primary.GetComp<CompRukayaActiveMatterWeapon>();
            if (weaponComp == null || !weaponComp.Props.allowAutoReload || !weaponComp.AutoReloadEnabled)
            {
                return;
            }

            if (weaponComp.Props.autoReloadPlayerOnly && !pawn.IsColonistPlayerControlled)
            {
                return;
            }

            if (weaponComp.Props.autoReloadRequireUndrafted && pawn.Drafted)
            {
                return;
            }

            if (weaponComp.Props.autoReloadRequireSafe && GenHostility.AnyHostileActiveThreatToPlayer(pawn.Map, false, false))
            {
                return;
            }

            if (pawn.jobs == null || pawn.CurJobDef == RukayaActiveMatterDefOf.Rukaya_ReloadActiveMatterWeapon)
            {
                return;
            }

            if (pawn.CurJob != null && (pawn.CurJob.playerForced || !pawn.jobs.IsCurrentJobPlayerInterruptible()))
            {
                return;
            }

            Thing source = RukayaActiveMatterUtility.FindClosestReloadSource(
                pawn,
                weaponComp.Props.activeMatterThingDef,
                weaponComp.Props.autoReloadFromStorageOnly);
            if (source == null)
            {
                return;
            }

            int containerCount = weaponComp.GetAutoReloadContainerCount(source.stackCount);
            if (containerCount <= 0)
            {
                return;
            }

            Job job = JobMaker.MakeJob(RukayaActiveMatterDefOf.Rukaya_ReloadActiveMatterWeapon, source, pawn.equipment.Primary);
            job.count = containerCount;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.MiscWork, false);
        }
    }
}
