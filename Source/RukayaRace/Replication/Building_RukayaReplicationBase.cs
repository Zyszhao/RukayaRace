using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RukayaRace.Replication
{
    public abstract class Building_RukayaReplicationBase : Building
    {
        public CompRukayaActiveMatterReservoir Reservoir => GetComp<CompRukayaActiveMatterReservoir>();

        public bool CanOperate
        {
            get
            {
                CompPowerTrader power = GetComp<CompPowerTrader>();
                CompFlickable flick = GetComp<CompFlickable>();
                CompBreakdownable breakdown = GetComp<CompBreakdownable>();
                return Spawned && !Destroyed
                    && (power == null || power.PowerOn)
                    && (flick == null || flick.SwitchIsOn)
                    && (breakdown == null || !breakdown.BrokenDown);
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            CompRukayaActiveMatterReservoir reservoir = Reservoir;
            if (selPawn == null || reservoir == null || reservoir.IsFull)
            {
                yield break;
            }

            Job job = RukayaReservoirLoadingUtility.MakeLoadJob(selPawn, this, true);
            if (job == null)
            {
                yield return new FloatMenuOption("Rukaya_ReservoirNoStoredMatter".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(
                "Rukaya_LoadReservoirFloatMenu".Translate(LabelShortCap),
                delegate { selPawn.jobs.TryTakeOrderedJob(job); },
                MenuOptionPriority.High);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            CompRukayaActiveMatterReservoir reservoir = Reservoir;
            if (reservoir == null)
            {
                yield break;
            }

            yield return new Command_SetRukayaReservoirLimit
            {
                reservoir = reservoir,
                defaultLabel = "Rukaya_ReservoirLimitGizmo".Translate(
                    Command_SetRukayaReservoirLimit.LimitText(
                        reservoir.UsesCustomStorageLimit ? reservoir.StorageLimit : 0,
                        reservoir.GlobalCapacity)),
                defaultDesc = "Rukaya_ReservoirLimitGizmoDesc".Translate(),
                icon = Command_SetRukayaReservoirLimit.LimitIcon
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Rukaya_DevFillReservoir".Translate(),
                    defaultDesc = "Rukaya_DevFillReservoirDesc".Translate(),
                    icon = Command_SetRukayaReservoirLimit.LimitIcon,
                    action = delegate { Find.WindowStack.Add(new Dialog_RukayaDevFillReservoir(reservoir)); }
                };
            }
        }

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            CompRukayaActiveMatterReservoir reservoir = Reservoir;
            string reservoirText = reservoir != null && reservoir.UsesCustomStorageLimit
                ? "Rukaya_ReservoirInspectWithLimit".Translate(reservoir.StoredContainers, reservoir.Capacity, reservoir.GlobalCapacity)
                : "Rukaya_ReservoirInspect".Translate(reservoir?.StoredContainers ?? 0, reservoir?.Capacity ?? 0);
            return baseText.NullOrEmpty() ? reservoirText : baseText + "\n" + reservoirText;
        }
    }
}
