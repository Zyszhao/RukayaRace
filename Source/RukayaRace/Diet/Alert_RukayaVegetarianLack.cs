using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RukayaRace.Diet
{
    // AlertsReadout discovers Alert subclasses; no Def or extra Harmony hook is needed.
    public class Alert_RukayaVegetarianLack : Alert
    {
        private readonly List<GlobalTargetInfo> targets = new List<GlobalTargetInfo>();
        private readonly List<string> targetDescriptions = new List<string>();
        private bool anySevere;

        public Alert_RukayaVegetarianLack()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => "Rukaya_DietAlertLabel".Translate();

        public override AlertPriority Priority => anySevere ? AlertPriority.High : AlertPriority.Medium;

        public static bool IsEligiblePawn(Pawn pawn)
        {
            return RukayaDietUtility.IsRukaya(pawn)
                && !pawn.Dead && !pawn.Suspended && !pawn.DevelopmentalStage.Baby()
                && pawn.needs?.mood != null
                && (pawn.Faction?.IsPlayer == true || pawn.IsPrisonerOfColony);
        }

        public static bool ShouldAlert(Pawn pawn, int elapsedTicks)
        {
            return IsEligiblePawn(pawn) && RukayaDietUtility.LackStage(elapsedTicks) >= 0;
        }

        private void CalculateTargets()
        {
            targets.Clear();
            targetDescriptions.Clear();
            anySevere = false;

            // Includes colony prisoners, caravans and travelling transporters; excludes world NPCs.
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                CompRukayaDiet comp = pawn.TryGetComp<CompRukayaDiet>();
                if (comp == null)
                    continue;
                int elapsedTicks = comp.TicksSinceVegetarianMeal;
                if (!ShouldAlert(pawn, elapsedTicks))
                    continue;
                int stage = RukayaDietUtility.LackStage(elapsedTicks);

                anySevere |= stage == 1;
                targets.Add(pawn);
                targetDescriptions.Add("Rukaya_DietAlertPawn".Translate(
                    pawn.NameShortColored.Resolve(),
                    GenDate.ToStringTicksToPeriod(elapsedTicks),
                    stage == 1 ? "-10" : "-3").Resolve());
            }
        }

        public override AlertReport GetReport()
        {
            CalculateTargets();
            return AlertReport.CulpritsAre(targets);
        }

        public override TaggedString GetExplanation()
        {
            CalculateTargets();
            return "Rukaya_DietAlertDescription".Translate(targetDescriptions.ToLineList("  - "));
        }
    }
}
