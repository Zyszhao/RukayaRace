using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Research
{
    public class CompProperties_RukayaArchiveInvestigation : CompProperties
    {
        public ThingDef memory;
        public float workRequired = 6000f;
        public CompProperties_RukayaArchiveInvestigation() => compClass = typeof(CompRukayaArchiveInvestigation);
    }

    // The memory is created only after investigation, never as an item-stash or destruction reward.
    public class CompRukayaArchiveInvestigation : ThingComp
    {
        private float workDone;
        private bool recovered;
        public CompProperties_RukayaArchiveInvestigation Props => (CompProperties_RukayaArchiveInvestigation)props;
        public bool Recovered => recovered;
        public float Progress => workDone / Props.workRequired;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref workDone, "rukayaInvestigationWork", 0f);
            Scribe_Values.Look(ref recovered, "rukayaMemoryRecovered", false);
        }

        public AcceptanceReport CanInvestigate(Pawn pawn)
        {
            if (recovered) return "Rukaya_ArchiveRecovered".Translate();
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return "Rukaya_ArchiveNoManipulation".Translate();
            if (!pawn.CanReach(parent, PathEndMode.ClosestTouch, Danger.Deadly)) return "NoPath".Translate();
            if (!pawn.CanReserve(parent)) return "Reserved".Translate();
            return true;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            AcceptanceReport report = CanInvestigate(pawn);
            string label = "Rukaya_InvestigateArchive".Translate(parent.LabelShort);
            if (!report.Accepted)
            {
                yield return new FloatMenuOption(label + " (" + report.Reason + ")", null);
                yield break;
            }
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label,
                () => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Rukaya_InvestigateArchive"), parent))), pawn, parent);
        }

        public override string CompInspectStringExtra() => recovered ? "Rukaya_ArchiveRecovered".Translate().ToString()
            : "Rukaya_ArchiveProgress".Translate(Progress.ToStringPercent()).ToString();

        public void Investigate(float amount)
        {
            if (recovered || !parent.Spawned || amount <= 0f) return;
            workDone = System.Math.Min(Props.workRequired, workDone + amount);
            if (workDone < Props.workRequired) return;
            Thing memory = ThingMaker.MakeThing(Props.memory);
            memory.stackCount = 1;
            // Main-thread transaction: latch before placement, roll back if there is nowhere to put it.
            recovered = true;
            if (!GenPlace.TryPlaceThing(memory, parent.Position, parent.Map, ThingPlaceMode.Near))
            {
                recovered = false;
                if (!memory.Destroyed) memory.Destroy(DestroyMode.Vanish);
                return;
            }
            memory.SetForbidden(false);
            RukayaMemoryArchives.Invalidate();
        }
    }

    public class JobDriver_RukayaInvestigateArchive : JobDriver
    {
        private CompRukayaArchiveInvestigation Investigation => TargetThingA.TryGetComp<CompRukayaArchiveInvestigation>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetThingA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Investigation == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            Toil toil = ToilMaker.MakeToil("InvestigateRukayaArchive");
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.handlingFacing = true;
            toil.tickAction = () =>
            {
                if (Investigation.Recovered) { ReadyForNextToil(); return; }
                Investigation.Investigate(System.Math.Max(0.1f, pawn.GetStatValue(StatDefOf.ResearchSpeed)));
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.05f);
                pawn.rotationTracker.FaceTarget(TargetThingA);
                if (Investigation.Recovered) ReadyForNextToil();
            };
            toil.WithProgressBar(TargetIndex.A, () => Investigation.Progress, alwaysShow: true);
            toil.FailOnCannotTouch(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return toil;
        }
    }
}
