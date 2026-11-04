using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    public class CompRukayaActiveMatterContainer : ThingComp
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn?.equipment?.Primary == null)
            {
                yield break;
            }

            CompRukayaActiveMatterWeapon weaponComp = selPawn.equipment.Primary.GetComp<CompRukayaActiveMatterWeapon>();
            if (weaponComp == null)
            {
                yield break;
            }

            int containerCount = weaponComp.GetManualReloadContainerCount(this.parent.stackCount);
            if (containerCount <= 0)
            {
                yield break;
            }

            if (!RukayaActiveMatterUtility.CanUseReloadSource(this.parent, selPawn, false))
            {
                yield break;
            }

            yield return new FloatMenuOption(
                "Rukaya_ActiveMatterReloadFloatMenu".Translate(selPawn.equipment.Primary.LabelShortCap),
                delegate
                {
                    if (RukayaActiveMatterDefOf.Rukaya_ReloadActiveMatterWeapon == null)
                    {
                        Log.Error("Rukaya_ReloadActiveMatterWeapon JobDef is missing.");
                        return;
                    }

                    Job job = JobMaker.MakeJob(RukayaActiveMatterDefOf.Rukaya_ReloadActiveMatterWeapon, this.parent, selPawn.equipment.Primary);
                    job.count = containerCount;
                    job.playerForced = true;
                    selPawn.jobs.TryTakeOrderedJob(job);
                },
                MenuOptionPriority.High);
        }
    }
}
