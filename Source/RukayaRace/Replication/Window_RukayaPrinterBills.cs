using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    /// <summary>
    /// Printer terminal window.
    ///
    /// The layout is the one shared by both replication terminals:
    ///   left   - the ordered production lists and their edit buttons
    ///   middle - the production list currently being configured, with the run status underneath
    ///   right  - every unlocked template, folded by mod, click to add to the production lists
    /// </summary>
    public class Window_RukayaPrinterBills : Window
    {
        private const float BillRowHeight = 76f;
        private const float BillMoveButtonWidth = 44f;
        private const float BillMoveButtonHeight = 24f;
        private const float TemplateRowHeight = RukayaReplicationUI.TwoLineRowHeight;
        private const float StatusSectionHeight = 150f;
        private const float SectionGap = 6f;
        private const float StatusLineHeight = 22f;

        private readonly Building_RukayaActiveMatterPrinter printer;

        private Vector2 billScroll;
        private Vector2 templateScroll;
        private int selectedBillIndex = -1;
        private int draggingBillIndex = -1;
        private string templateSearch = string.Empty;
        private RukayaTemplateCategory templateCategory = RukayaTemplateCategory.Weapons;

        public override Vector2 InitialSize => RukayaReplicationUI.WindowSize;

        public Window_RukayaPrinterBills(Building_RukayaActiveMatterPrinter printer)
        {
            this.printer = printer;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (printer == null || printer.Destroyed || !printer.Spawned)
            {
                Close();
                return;
            }

            float top = RukayaReplicationUI.DrawWindowHeader(
                inRect,
                "Rukaya_PrinterWindowTitle".Translate(),
                "Rukaya_PrinterSummary".Translate(printer.Reservoir.StoredContainers, printer.Reservoir.Capacity, printer.CurrentStatus()));

            RukayaReplicationUI.SplitColumns(inRect, top, out Rect leftRect, out Rect middleRect, out Rect rightRect);
            RukayaReplicationUI.DrawColumn(leftRect);
            RukayaReplicationUI.DrawColumn(middleRect);
            RukayaReplicationUI.DrawColumn(rightRect);

            float padding = RukayaReplicationUI.ColumnInnerPadding;
            DrawBills(leftRect.ContractedBy(padding));
            DrawMiddleColumn(middleRect.ContractedBy(padding));
            DrawTemplatesPanel(rightRect.ContractedBy(padding));
        }

        // ------------------------------------------------------------------ left: ordered production lists

        private void DrawBills(Rect rect)
        {
            RukayaReplicationUI.DrawListHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_PrinterBillsHeader".Translate());

            Rect listRect = new Rect(
                rect.x,
                rect.y + RukayaReplicationUI.PanelHeaderHeight + 6f,
                rect.width,
                Mathf.Max(0f, rect.height - RukayaReplicationUI.PanelHeaderHeight - 6f - 40f));
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - 16f), Mathf.Max(listRect.height, printer.Bills.Count * BillRowHeight));
            Widgets.BeginScrollView(listRect, ref billScroll, view);

            if (printer.Bills.Count == 0)
            {
                Widgets.Label(new Rect(0f, 0f, view.width, 24f), "Rukaya_PrinterBillsEmpty".Translate());
            }

            for (int i = 0; i < printer.Bills.Count; i++)
            {
                RukayaPrintBill bill = printer.Bills[i];
                Rect row = new Rect(0f, i * BillRowHeight, view.width, BillRowHeight - 2f);
                if (i == selectedBillIndex)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }

                RukayaReplicationUI.DrawRowDivider(row, view);

                // The grip column stays at the very left edge, so the thumbnail starts right after it
                // and every row keeps the same drag affordance the player already knows.
                Rect dragRect = new Rect(row.x + 2f, row.y + 4f, 18f, row.height - 8f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(dragRect, "=");
                Text.Anchor = TextAnchor.UpperLeft;
                HandleDrag(i, dragRect, BillRowHeight);

                float iconTop = row.y + (row.height - RukayaReplicationUI.RowIconSize) / 2f;
                Thing preview = RukayaReplicationUI.PreviewThing(bill.ThingDef, bill.StuffDef, bill.quality);
                RukayaReplicationUI.DrawThingIcon(
                    new Rect(dragRect.xMax + 2f, iconTop, RukayaReplicationUI.RowIconSize, RukayaReplicationUI.RowIconSize),
                    preview,
                    out bool drewIcon);

                float textX = drewIcon
                    ? dragRect.xMax + 2f + RukayaReplicationUI.RowIconSize + RukayaReplicationUI.RowIconGap
                    : dragRect.xMax + 6f;

                // The move buttons live on the label line now, which keeps the whole second line free
                // for the quality and repeat readout and survives the narrower unified column.
                Rect up = new Rect(row.xMax - BillMoveButtonWidth * 2f - 4f, row.y + 4f, BillMoveButtonWidth, BillMoveButtonHeight);
                Rect down = new Rect(row.xMax - BillMoveButtonWidth, row.y + 4f, BillMoveButtonWidth, BillMoveButtonHeight);

                float textHeight = row.height - 8f;
                Rect labelRect = new Rect(textX, row.y + 4f, Mathf.Max(0f, up.xMin - textX - 6f), textHeight / 2f);
                Rect subRect = new Rect(textX, labelRect.yMax, Mathf.Max(0f, row.xMax - textX - 6f), textHeight / 2f);

                Widgets.Label(labelRect, (bill.Label ?? string.Empty).Truncate(labelRect.width));
                GameFont previousFont = Text.Font;
                Text.Font = GameFont.Tiny;
                Widgets.Label(subRect, (RukayaReplicationUtility.QualityLabel(bill.quality) + " - " + RepeatLabel(bill)).Truncate(subRect.width));
                Text.Font = previousFont;

                if (i > 0 && Widgets.ButtonText(up, "Rukaya_BillMoveUp".Translate()))
                {
                    printer.MoveBill(i, i - 1);
                    selectedBillIndex = i - 1;
                }
                if (i < printer.Bills.Count - 1 && Widgets.ButtonText(down, "Rukaya_BillMoveDown".Translate()))
                {
                    printer.MoveBill(i, i + 1);
                    selectedBillIndex = i + 1;
                }

                Rect selectRect = row;
                selectRect.xMin = textX;
                selectRect.xMax = up.xMin;
                if (Widgets.ButtonInvisible(selectRect))
                {
                    selectedBillIndex = i;
                }
            }
            Widgets.EndScrollView();

            Rect controls = new Rect(rect.x, rect.yMax - 34f, rect.width, 32f);
            if (selectedBillIndex >= 0 && selectedBillIndex < printer.Bills.Count)
            {
                float width = (controls.width - 12f) / 4f;
                if (Widgets.ButtonText(new Rect(controls.x, controls.y, width, controls.height), "Rukaya_BillSuspend".Translate()))
                {
                    printer.Bills[selectedBillIndex].suspended = !printer.Bills[selectedBillIndex].suspended;
                }
                if (Widgets.ButtonText(new Rect(controls.x + width + 4f, controls.y, width, controls.height), "Rukaya_BillCopy".Translate()))
                {
                    printer.DuplicateBill(selectedBillIndex);
                }
                if (Widgets.ButtonText(new Rect(controls.x + (width + 4f) * 2f, controls.y, width, controls.height), "Rukaya_BillDelete".Translate()))
                {
                    printer.RemoveBill(selectedBillIndex);
                    selectedBillIndex = Mathf.Min(selectedBillIndex, printer.Bills.Count - 1);
                }
                if (printer.HasActiveJob && Widgets.ButtonText(new Rect(controls.x + (width + 4f) * 3f, controls.y, width, controls.height), "Rukaya_BillCancelCurrent".Translate()))
                {
                    printer.CancelActiveJob(true);
                }
            }
        }

        // ------------------------------------------------------------------ middle: current project + run status

        private void DrawMiddleColumn(Rect rect)
        {
            float statusHeight = Mathf.Min(StatusSectionHeight, rect.height * 0.6f);
            Rect configRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0f, rect.height - statusHeight - SectionGap));
            Rect statusRect = new Rect(rect.x, configRect.yMax + SectionGap, rect.width, statusHeight);

            DrawConfiguration(configRect);
            DrawProductionStatus(statusRect);
        }

        private void DrawConfiguration(Rect rect)
        {
            RukayaReplicationUI.DrawListHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_PrinterConfigHeader".Translate());

            Rect body = new Rect(
                rect.x,
                rect.y + RukayaReplicationUI.PanelHeaderHeight + 8f,
                rect.width,
                Mathf.Max(0f, rect.yMax - rect.y - RukayaReplicationUI.PanelHeaderHeight - 8f));

            if (selectedBillIndex < 0 || selectedBillIndex >= printer.Bills.Count)
            {
                RukayaReplicationUI.DrawHint(body, "Rukaya_PrinterSelectBill".Translate());
                return;
            }

            RukayaPrintBill bill = printer.Bills[selectedBillIndex];
            ThingDef def = bill.ThingDef;
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            QualityCategory maxQuality = library != null && library.IsUnlocked(def)
                ? library.GetMaxQuality(def)
                : RukayaReplicationUtility.MinPrintableQuality;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(body);
            listing.Label(bill.Label);

            Rect iconRow = listing.GetRect(RukayaReplicationUI.RowIconSize + 8f);
            Thing preview = RukayaReplicationUI.PreviewThing(def, bill.StuffDef, bill.quality);
            RukayaReplicationUI.DrawThingIcon(
                new Rect(iconRow.x, iconRow.y + 2f, RukayaReplicationUI.RowIconSize + 4f, RukayaReplicationUI.RowIconSize + 4f),
                preview,
                out _);

            listing.Gap(4f);
            if (listing.ButtonText("Rukaya_MaterialButton".Translate(bill.StuffDef?.LabelCap ?? "Rukaya_NoMaterial".Translate())))
            {
                List<FloatMenuOption> options = RukayaReplicationUtility.AllowedStuffs(def)
                    .Select(stuff => new FloatMenuOption(stuff?.LabelCap ?? "Rukaya_NoMaterial".Translate(), delegate { bill.stuffDefName = stuff?.defName; }))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }

            if (listing.ButtonText("Rukaya_QualityButton".Translate(RukayaReplicationUtility.QualityLabel(bill.quality))))
            {
                // The selectable range now starts at the printable minimum instead of Normal, which is
                // what makes the cheap awful/poor print runs reachable from the UI.
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (QualityCategory quality = RukayaReplicationUtility.MinPrintableQuality; quality <= maxQuality; quality++)
                {
                    QualityCategory captured = quality;
                    int tierCost = RukayaReplicationUtility.GetPrintCost(def, bill.StuffDef, captured);
                    options.Add(new FloatMenuOption(
                        RukayaReplicationUtility.QualityLabel(captured) + " (" + "Rukaya_CostShort".Translate(tierCost) + ")",
                        delegate { bill.quality = captured; }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            if (listing.ButtonText("Rukaya_RepeatModeButton".Translate(RepeatLabel(bill))))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Rukaya_RepeatCount".Translate(), delegate { bill.repeatMode = RukayaPrintRepeatMode.RepeatCount; }),
                    new FloatMenuOption("Rukaya_RepeatForever".Translate(), delegate { bill.repeatMode = RukayaPrintRepeatMode.Forever; }),
                    new FloatMenuOption("Rukaya_RepeatUntil".Translate(), delegate { bill.repeatMode = RukayaPrintRepeatMode.UntilTarget; })
                }));
            }

            if (bill.repeatMode != RukayaPrintRepeatMode.Forever)
            {
                listing.Label("Rukaya_TargetCount".Translate(bill.targetCount));
                Rect row = listing.GetRect(32f);
                float width = (row.width - 12f) / 4f;
                int[] amounts = { -10, -1, 1, 10 };
                for (int i = 0; i < amounts.Length; i++)
                {
                    if (Widgets.ButtonText(new Rect(row.x + i * (width + 4f), row.y, width, row.height), amounts[i] > 0 ? "+" + amounts[i] : amounts[i].ToString()))
                    {
                        bill.targetCount = Mathf.Clamp(bill.targetCount + amounts[i], 1, 9999);
                    }
                }
            }

            listing.GapLine(12f);
            int cost = RukayaReplicationUtility.GetPrintCost(def, bill.StuffDef, bill.quality);
            int ticks = RukayaReplicationUtility.GetPrintWorkTicks(def, bill.StuffDef, bill.quality);
            listing.Label("Rukaya_PrintCost".Translate(cost));
            listing.Label("Rukaya_PrintTime".Translate(ticks.ToStringTicksToPeriod()));

            if (bill.repeatMode == RukayaPrintRepeatMode.RepeatCount)
            {
                listing.Label("Rukaya_BillProgress".Translate(bill.completedCount, bill.targetCount));
            }
            else if (bill.repeatMode == RukayaPrintRepeatMode.UntilTarget)
            {
                listing.Label("Rukaya_StockProgress".Translate(RukayaReplicationUtility.CountMatchingStock(printer.Map, def, bill.StuffDef, bill.quality), bill.targetCount));
            }

            listing.End();
        }

        private void DrawProductionStatus(Rect rect)
        {
            RukayaReplicationUI.DrawListHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_PrinterStatusHeader".Translate());

            float y = rect.y + RukayaReplicationUI.PanelHeaderHeight + 6f;
            Widgets.Label(new Rect(rect.x, y, rect.width, StatusLineHeight), "Rukaya_PrinterStatusLine".Translate(printer.CurrentStatus()));
            y += StatusLineHeight;
            Widgets.Label(new Rect(rect.x, y, rect.width, StatusLineHeight), "Rukaya_PrinterOutputState".Translate(
                (printer.OutputBlocked ? "Rukaya_PrinterOutputBlockedShort" : "Rukaya_PrinterOutputReady").Translate()));
            y += StatusLineHeight;

            if (printer.HasActiveJob)
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, StatusLineHeight), "Rukaya_PrinterCurrentProduct".Translate(printer.ActiveProductLabel));
                y += StatusLineHeight;
                Widgets.Label(new Rect(rect.x, y, rect.width, StatusLineHeight), "Rukaya_PrinterReservedCost".Translate(printer.ActiveCost));
                y += StatusLineHeight;

                Rect progressRect = new Rect(rect.x, y, rect.width, 24f);
                Widgets.FillableBar(progressRect, printer.Progress);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(progressRect, "Rukaya_PrinterProgress".Translate(Mathf.RoundToInt(printer.Progress * 100f)));
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        // ------------------------------------------------------------------ right: unlocked templates

        private void DrawTemplatesPanel(Rect rect)
        {
            RukayaTemplateCategory picked = RukayaReplicationUI.DrawPanelHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_PrinterTemplatesHeader".Translate(),
                templateCategory,
                out bool changed);
            if (changed)
            {
                templateCategory = picked;
                templateScroll = Vector2.zero;
            }

            Rect searchRect = new Rect(
                rect.x,
                rect.y + RukayaReplicationUI.PanelHeaderHeight + 6f,
                rect.width,
                RukayaReplicationUI.PanelSearchHeight);
            templateSearch = Widgets.TextField(searchRect, templateSearch ?? string.Empty);

            List<RukayaModGroup<ThingDef>> groups = RukayaReplicationUI.GroupedUnlockedTemplates(templateSearch, templateCategory);

            Rect listRect = new Rect(rect.x, searchRect.yMax + 6f, rect.width, Mathf.Max(0f, rect.yMax - searchRect.yMax - 6f));
            RukayaReplicationUI.DrawGroupedList(
                listRect,
                groups,
                TemplateRowHeight,
                ref templateScroll,
                DrawTemplateRow,
                "Rukaya_PrinterTemplatesEmpty".Translate());
        }

        private void DrawTemplateRow(Rect row, Rect view, ThingDef def)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            RukayaReplicationUI.DrawRowDivider(row, view);

            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            QualityCategory max = library?.GetMaxQuality(def) ?? RukayaReplicationUtility.MinPrintableQuality;
            Thing preview = RukayaReplicationUI.PreviewThing(
                def,
                RukayaReplicationUI.ResolveDisplayStuff(def, null),
                max);

            RukayaReplicationUI.DrawEquipmentRow(
                row,
                preview,
                def.LabelCap,
                "Rukaya_PrinterTemplateRow".Translate(RukayaReplicationUtility.QualityLabel(max)));

            if (Widgets.ButtonInvisible(row))
            {
                printer.AddBill(def);
                selectedBillIndex = printer.Bills.Count - 1;
            }
        }

        // ------------------------------------------------------------------ helpers

        private void HandleDrag(int index, Rect dragRect, float rowHeight)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && Mouse.IsOver(dragRect))
            {
                draggingBillIndex = index;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 && draggingBillIndex >= 0)
            {
                int target = Mathf.Clamp(Mathf.FloorToInt(current.mousePosition.y / rowHeight), 0, printer.Bills.Count - 1);
                printer.MoveBill(draggingBillIndex, target);
                selectedBillIndex = target;
                draggingBillIndex = -1;
                current.Use();
            }
        }

        private static string RepeatLabel(RukayaPrintBill bill)
        {
            switch (bill.repeatMode)
            {
                case RukayaPrintRepeatMode.Forever:
                    return "Rukaya_RepeatForever".Translate();
                case RukayaPrintRepeatMode.UntilTarget:
                    return "Rukaya_RepeatUntilValue".Translate(bill.targetCount);
                default:
                    return "Rukaya_RepeatCountValue".Translate(Mathf.Max(0, bill.targetCount - bill.completedCount));
            }
        }
    }
}
