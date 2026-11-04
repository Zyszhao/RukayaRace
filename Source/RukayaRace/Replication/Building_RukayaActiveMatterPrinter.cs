using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    public class Building_RukayaActiveMatterPrinter : Building_RukayaReplicationBase, IThingHolder
    {
        private List<RukayaPrintBill> bills = new List<RukayaPrintBill>();
        private ThingOwner<Thing> outputSlot;
        private string activeBillId;
        private string activeThingDefName;
        private string activeStuffDefName;
        private QualityCategory activeQuality = QualityCategory.Normal;
        private int activeCost;
        private int activeWorkLeft;
        private int activeWorkTotal;

        public List<RukayaPrintBill> Bills => bills;
        public bool HasActiveJob => !activeBillId.NullOrEmpty();
        public RukayaPrintBill ActiveBill => HasActiveJob ? bills.FirstOrDefault(x => x.billId == activeBillId) : null;
        public int ActiveCost => activeCost;
        public string ActiveProductLabel
        {
            get
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(activeThingDefName);
                return def?.LabelCap ?? string.Empty;
            }
        }
        public float Progress => activeWorkTotal <= 0 ? 0f : Mathf.Clamp01(1f - (float)activeWorkLeft / activeWorkTotal);
        public bool OutputBlocked => outputSlot != null && outputSlot.Count > 0;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            outputSlot ??= new ThingOwner<Thing>(this, false);
            bills ??= new List<RukayaPrintBill>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref bills, "bills", LookMode.Deep);
            Scribe_Deep.Look(ref outputSlot, "outputSlot", this);
            Scribe_Values.Look(ref activeBillId, "activeBillId");
            Scribe_Values.Look(ref activeThingDefName, "activeThingDefName");
            Scribe_Values.Look(ref activeStuffDefName, "activeStuffDefName");
            Scribe_Values.Look(ref activeQuality, "activeQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref activeCost, "activeCost", 0);
            Scribe_Values.Look(ref activeWorkLeft, "activeWorkLeft", 0);
            Scribe_Values.Look(ref activeWorkTotal, "activeWorkTotal", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bills ??= new List<RukayaPrintBill>();
                outputSlot ??= new ThingOwner<Thing>(this, false);
                bills.RemoveAll(x => x == null || x.ThingDef == null);
                for (int i = bills.Count - 1; i >= 0; i--)
                {
                    RukayaPrintBill bill = bills[i];
                    NormalizeBill(bill);
                    if (bill.ThingDef == null || !RukayaReplicationUtility.IsEligibleDef(bill.ThingDef))
                    {
                        bills.RemoveAt(i);
                    }
                }

                if (!activeBillId.NullOrEmpty())
                {
                    RukayaPrintBill activeBill = bills.FirstOrDefault(x => x.billId == activeBillId);
                    if (activeBill == null || DefDatabase<ThingDef>.GetNamedSilentFail(activeThingDefName) == null)
                    {
                        activeBillId = null;
                        activeThingDefName = null;
                        activeStuffDefName = null;
                        activeCost = 0;
                        activeWorkLeft = 0;
                        activeWorkTotal = 0;
                    }
                }
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned)
            {
                return;
            }

            if (OutputBlocked)
            {
                TryEjectOutput();
                if (OutputBlocked)
                {
                    return;
                }
            }

            if (!CanOperate)
            {
                return;
            }

            if (!HasActiveJob)
            {
                TryStartNextBill();
                return;
            }

            activeWorkLeft--;
            if (activeWorkLeft <= 0)
            {
                FinishActiveJob();
            }
        }

        public void AddBill(ThingDef def)
        {
            if (!RukayaReplicationUtility.IsEligibleDef(def) || RukayaReplicationUtility.Library?.IsUnlocked(def) != true)
            {
                return;
            }

            ThingDef stuff = RukayaReplicationUtility.AllowedStuffs(def).FirstOrDefault();
            bills.Add(new RukayaPrintBill
            {
                billId = Guid.NewGuid().ToString("N"),
                thingDefName = def.defName,
                stuffDefName = stuff?.defName,
                // New lists start at the cheapest printable tier; the player raises it from the
                // configuration panel, which uses the template's own maximum as its ceiling.
                quality = RukayaReplicationUtility.ClampPrintableQuality(QualityCategory.Normal),
                repeatMode = RukayaPrintRepeatMode.RepeatCount,
                targetCount = 1
            });
        }

        public void DuplicateBill(int index)
        {
            if (index < 0 || index >= bills.Count)
            {
                return;
            }

            RukayaPrintBill source = bills[index];
            bills.Insert(index + 1, new RukayaPrintBill
            {
                billId = Guid.NewGuid().ToString("N"),
                thingDefName = source.thingDefName,
                stuffDefName = source.stuffDefName,
                quality = source.quality,
                repeatMode = source.repeatMode,
                targetCount = source.targetCount,
                suspended = source.suspended
            });
        }

        public void RemoveBill(int index)
        {
            if (index < 0 || index >= bills.Count)
            {
                return;
            }

            if (bills[index].billId == activeBillId)
            {
                CancelActiveJob(true);
            }

            bills.RemoveAt(index);
        }

        public void MoveBill(int from, int to)
        {
            if (from < 0 || from >= bills.Count || to < 0 || to >= bills.Count || from == to)
            {
                return;
            }

            RukayaPrintBill bill = bills[from];
            bills.RemoveAt(from);
            bills.Insert(to, bill);
        }

        public void CancelActiveJob(bool refund)
        {
            if (!HasActiveJob)
            {
                return;
            }

            if (refund && activeCost > 0)
            {
                if (Reservoir == null || !Reservoir.TryRefund(activeCost))
                {
                    DropActiveMatter(activeCost);
                }
            }

            ClearActiveJob();
        }

        public string CurrentStatus()
        {
            if (OutputBlocked)
            {
                return "Rukaya_PrinterStatusOutputBlocked".Translate();
            }

            if (!CanOperate)
            {
                return "Rukaya_ReplicationBuildingUnavailable".Translate();
            }

            if (HasActiveJob)
            {
                return "Rukaya_PrinterStatusPrinting".Translate(Mathf.RoundToInt(Progress * 100f));
            }

            RukayaPrintBill next = FindNextBill();
            if (next != null)
            {
                int cost = RukayaReplicationUtility.GetPrintCost(next.ThingDef, next.StuffDef, next.quality);
                if (Reservoir == null || Reservoir.StoredContainers < cost)
                {
                    return "Rukaya_PrinterStatusInsufficient".Translate(cost);
                }
            }

            return "Rukaya_PrinterStatusIdle".Translate();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = "Rukaya_OpenPrinter".Translate(),
                defaultDesc = "Rukaya_OpenPrinterDesc".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/LaunchReport", false),
                action = delegate { Find.WindowStack.Add(new Window_RukayaPrinterBills(this)); }
            };
        }

        public override string GetInspectString()
        {
            return base.GetInspectString() + "\n" + CurrentStatus();
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned && outputSlot != null && outputSlot.Count > 0)
            {
                outputSlot.TryDropAll(Position, Map, ThingPlaceMode.Near);
            }

            base.Destroy(mode);
        }

        private RukayaPrintBill FindNextBill()
        {
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            return bills.FirstOrDefault(x => x != null
                && !x.suspended
                && x.ThingDef != null
                && RukayaReplicationUtility.IsEligibleDef(x.ThingDef)
                && library?.IsUnlocked(x.ThingDef) == true
                && !x.IsFinished(Map));
        }

        private void TryStartNextBill()
        {
            RukayaPrintBill bill = FindNextBill();
            if (bill == null)
            {
                return;
            }

            NormalizeBill(bill);
            if (bill.ThingDef == null || !RukayaReplicationUtility.IsEligibleDef(bill.ThingDef))
            {
                return;
            }

            int cost = RukayaReplicationUtility.GetPrintCost(bill.ThingDef, bill.StuffDef, bill.quality);
            if (Reservoir == null || !Reservoir.TryConsume(cost))
            {
                return;
            }

            activeBillId = bill.billId;
            activeThingDefName = bill.thingDefName;
            activeStuffDefName = bill.stuffDefName;
            activeQuality = bill.quality;
            activeCost = cost;
            activeWorkTotal = RukayaReplicationUtility.GetPrintWorkTicks(bill.ThingDef, bill.StuffDef, bill.quality);
            activeWorkLeft = activeWorkTotal;
        }

        private void FinishActiveJob()
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(activeThingDefName);
            ThingDef stuff = activeStuffDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(activeStuffDefName);
            if (def != null)
            {
                Thing product = RukayaReplicationUtility.MakePreview(def, stuff, activeQuality);
                if (product != null)
                {
                    outputSlot.TryAdd(product, true);
                }
            }

            RukayaPrintBill bill = bills.FirstOrDefault(x => x.billId == activeBillId);
            if (bill != null)
            {
                bill.completedCount++;
            }

            ClearActiveJob();
            TryEjectOutput();
        }

        private void TryEjectOutput()
        {
            if (outputSlot == null || outputSlot.Count == 0 || !Spawned)
            {
                return;
            }

            Thing product = outputSlot[0];
            outputSlot.Remove(product);
            if (!GenPlace.TryPlaceThing(product, InteractionCell, Map, ThingPlaceMode.Near))
            {
                outputSlot.TryAdd(product, true);
            }
        }

        private void ClearActiveJob()
        {
            activeBillId = null;
            activeThingDefName = null;
            activeStuffDefName = null;
            activeQuality = QualityCategory.Normal;
            activeCost = 0;
            activeWorkLeft = 0;
            activeWorkTotal = 0;
        }

        private static void NormalizeBill(RukayaPrintBill bill)
        {
            if (bill == null)
            {
                return;
            }

            ThingDef def = bill.ThingDef;
            if (def == null)
            {
                return;
            }

            List<ThingDef> stuffs = RukayaReplicationUtility.AllowedStuffs(def);
            if (!stuffs.Contains(bill.StuffDef))
            {
                bill.stuffDefName = stuffs.FirstOrDefault()?.defName;
            }

            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            QualityCategory max = library != null && library.IsUnlocked(def)
                ? library.GetMaxQuality(def)
                : RukayaReplicationUtility.MaxPrintableQuality;
            bill.quality = (QualityCategory)Mathf.Clamp(
                (int)bill.quality,
                (int)RukayaReplicationUtility.MinPrintableQuality,
                (int)max);
            bill.targetCount = Mathf.Clamp(bill.targetCount, 1, 9999);
            bill.completedCount = Mathf.Max(0, bill.completedCount);
        }

        private void DropActiveMatter(int count)
        {
            if (!Spawned || count <= 0)
            {
                return;
            }

            int remaining = count;
            int stackLimit = RukayaReplicationDefOf.Rukaya_ActiveMatterContainer.stackLimit;
            while (remaining > 0)
            {
                Thing stack = ThingMaker.MakeThing(RukayaReplicationDefOf.Rukaya_ActiveMatterContainer);
                int chunk = Mathf.Min(stackLimit, remaining);
                stack.stackCount = chunk;
                GenPlace.TryPlaceThing(stack, InteractionCell, Map, ThingPlaceMode.Near);
                remaining -= chunk;
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return outputSlot;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
        }
    }
}
