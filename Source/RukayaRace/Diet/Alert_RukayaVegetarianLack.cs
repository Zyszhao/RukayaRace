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

        private void CalculateTargets(bool buildDescriptions)
        {
            targets.Clear();
            if (buildDescriptions)
                targetDescriptions.Clear();
            anySevere = false;

            // Includes colony prisoners, caravans and travelling transporters; excludes world NPCs.
            List<Pawn> allPawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (!IsEligiblePawn(pawn))
                    continue;
                CompRukayaDiet comp = pawn.TryGetComp<CompRukayaDiet>();
                if (comp == null)
                    continue;
                int elapsedTicks = comp.TicksSinceVegetarianMeal;
                int stage = RukayaDietUtility.LackStage(elapsedTicks);
                if (stage < 0)
                    continue;

                anySevere |= stage == 1;
                targets.Add(pawn);
                if (buildDescriptions)
                {
                    targetDescriptions.Add("Rukaya_DietAlertPawn".Translate(
                        pawn.NameShortColored.Resolve(),
                        GenDate.ToStringTicksToPeriod(elapsedTicks),
                        stage == 1 ? "-10" : "-3").Resolve());
                }
            }
        }

        public override AlertReport GetReport()
        {
            CalculateTargets(false);
            return AlertReport.CulpritsAre(targets);
        }

        public override TaggedString GetExplanation()
        {
            CalculateTargets(true);
            return "Rukaya_DietAlertDescription".Translate(targetDescriptions.ToLineList("  - "));
        }
    }
}
