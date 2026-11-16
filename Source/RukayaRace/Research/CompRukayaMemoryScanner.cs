using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Research
{
    // Inherit the vanilla component and its save keys; old scanners retain their target mineral and work progress.
    public class CompRukayaMemoryScanner : CompLongRangeMineralScanner
    {
        public bool MemoryMode { get; private set; }
        private float inactiveModeWork;
        private bool retryFinding;
        private int retryAfterTick;

        public override void PostExposeData()
        {
            base.PostExposeData();
            bool mode = MemoryMode;
            Scribe_Values.Look(ref mode, "rukayaMemoryMode", false);
            MemoryMode = mode;
            Scribe_Values.Look(ref inactiveModeWork, "rukayaInactiveScanWork", 0f);
            Scribe_Values.Look(ref retryFinding, "rukayaRetryMemoryFind", false);
            Scribe_Values.Look(ref retryAfterTick, "rukayaRetryMemoryAfter", 0);
        }

        public void ToggleMode()
        {
            float oldWork = daysWorkingSinceLastFinding;
            daysWorkingSinceLastFinding = inactiveModeWork;
            inactiveModeWork = oldWork;
            MemoryMode = !MemoryMode;
        }

        public override AcceptanceReport CanUseNow
        {
            get
            {
                AcceptanceReport report = base.CanUseNow;
                if (!report.Accepted) return report;
                if (MemoryMode && RukayaMemoryArchives.Candidates().Count == 0)
                    return "Rukaya_MemoryScanNoTarget".Translate();
                return true;
            }
        }

        protected override bool TickDoesFind(float scanSpeed)
        {
            if (!MemoryMode) return base.TickDoesFind(scanSpeed);
            if (RukayaMemoryArchives.Candidates().Count == 0) return false;
            if (retryFinding) return Find.TickManager.TicksGame >= retryAfterTick && parent.IsHashIntervalTick(59);
            return base.TickDoesFind(scanSpeed);
        }

        protected override void DoFind(Pawn worker)
        {
            if (!MemoryMode) { base.DoFind(worker); return; }
            retryFinding = !RukayaMemoryArchives.TryGenerate(parent.Map, worker, out _);
            // CompScanner.Used resets its counter even on failure. Retain the earned discovery separately.
            if (retryFinding) retryAfterTick = Find.TickManager.TicksGame + 2500;
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            if (!MemoryMode) return text;
            string state = RukayaMemoryArchives.Candidates().Count == 0 ? "Rukaya_MemoryScanNoTarget"
                : retryFinding ? "Rukaya_MemoryScanRetry" : "Rukaya_MemoryScanActive";
            return "Rukaya_MemoryScanMode".Translate() + ": " + state.Translate() + "\n" + text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Vanilla's mineral selector stays available; it remembers the mineral to use when returning to mineral mode.
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = (MemoryMode ? "Rukaya_MemoryScanOn" : "Rukaya_MemoryScanOff").Translate(),
                defaultDesc = "Rukaya_MemoryScanDescription".Translate(),
                icon = ContentFinder<Texture2D>.Get(MemoryMode ? "Rukaya/UI/MemoryScanOn" : "Rukaya/UI/MemoryScanOff"),
                action = ToggleMode
            };
        }
    }
}
