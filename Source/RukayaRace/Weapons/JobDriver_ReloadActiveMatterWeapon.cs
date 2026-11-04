using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public class JobDriver_ReloadActiveMatterWeapon : JobDriver
    {
        private const TargetIndex SourceIndex = TargetIndex.A;
        private const TargetIndex WeaponIndex = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return this.pawn.Reserve(this.job.GetTarget(SourceIndex), this.job, 1, this.job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(SourceIndex);
            this.FailOn(() => this.job.GetTarget(WeaponIndex).Thing == null);
            this.FailOn(() => this.pawn.equipment?.Primary != this.job.GetTarget(WeaponIndex).Thing);

            yield return Toils_Goto.GotoThing(SourceIndex, PathEndMode.ClosestTouch);

            Toil reload = ToilMaker.MakeToil("ReloadActiveMatterWeapon");
            reload.initAction = delegate
            {
                Thing source = this.job.GetTarget(SourceIndex).Thing;
                Thing weapon = this.job.GetTarget(WeaponIndex).Thing;
                CompRukayaActiveMatterWeapon weaponComp = weapon?.TryGetComp<CompRukayaActiveMatterWeapon>();
                if (source == null || weaponComp == null)
                {
                    return;
                }

                int requestedContainers = this.job.count > 0 ? this.job.count : 1;
                int availableContainers = source.stackCount;
                int usedContainers = weaponComp.ReloadFromContainers(
                    requestedContainers > availableContainers ? availableContainers : requestedContainers,
                    this.job.playerForced);
                if (usedContainers <= 0)
                {
                    return;
                }

                if (usedContainers >= source.stackCount)
                {
                    source.Destroy();
                    return;
                }

                Thing spent = source.SplitOff(usedContainers);
                spent.Destroy();
            };
            reload.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return reload;
        }
    }
}
