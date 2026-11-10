using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    /// <summary>
    /// Scanner terminal window.
    ///
    /// The layout is the one shared by both replication terminals:
    ///   left   - the weapons and apparel selectable on this map
    ///   middle - the project (the selected prototype or the selected unlocked template)
    ///   right  - every unlocked template, folded by mod
    /// </summary>
    public class Window_RukayaScanner : Window
    {
        private const float ItemRowHeight = RukayaReplicationUI.TwoLineRowHeight;
        private const float TemplateRowHeight = RukayaReplicationUI.TwoLineRowHeight;

        private readonly Building_RukayaTriplexScanner scanner;

        private Vector2 itemScroll;
        private Vector2 templateScroll;
        private string itemSearch = string.Empty;
        private string templateSearch = string.Empty;
        private Thing selected;
        private ThingDef selectedTemplate;
        private RukayaTemplateCategory itemCategory = RukayaTemplateCategory.Weapons;
        private RukayaTemplateCategory templateCategory = RukayaTemplateCategory.Weapons;

        public override Vector2 InitialSize => RukayaReplicationUI.WindowSize;

        public Window_RukayaScanner(Building_RukayaTriplexScanner scanner)
        {
            this.scanner = scanner;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (scanner == null || scanner.Destroyed || !scanner.Spawned)
            {
                Close();
                return;
            }

            float top = RukayaReplicationUI.DrawWindowHeader(
                inRect,
                "Rukaya_ScannerWindowTitle".Translate(),
                "Rukaya_ReservoirInspect".Translate(scanner.Reservoir.StoredContainers, scanner.Reservoir.Capacity));

            RukayaReplicationUI.SplitColumns(inRect, top, out Rect leftRect, out Rect middleRect, out Rect rightRect);
            RukayaReplicationUI.DrawColumn(leftRect);
            RukayaReplicationUI.DrawColumn(middleRect);
            RukayaReplicationUI.DrawColumn(rightRect);

            PruneSelection();

            DrawMapEquipmentPanel(leftRect.ContractedBy(RukayaReplicationUI.ColumnInnerPadding));
            DrawProjectPanel(middleRect.ContractedBy(RukayaReplicationUI.ColumnInnerPadding));
            DrawTemplatesPanel(rightRect.ContractedBy(RukayaReplicationUI.ColumnInnerPadding));
        }

        /// <summary>
        /// Drops selections whose backing thing or def disappeared (item destroyed, template lost to a
        /// dev-mode reset) so the detail panel never shows a stale entry.
        /// </summary>
        private void PruneSelection()
        {
            if (selected != null && (selected.Destroyed || !RukayaReplicationUtility.IsEligibleThing(selected)))
            {
                selected = null;
            }

            if (selectedTemplate != null && !RukayaReplicationUtility.IsEligibleDef(selectedTemplate))
            {
                selectedTemplate = null;
            }
        }

        // ------------------------------------------------------------------ left: map equipment

        private void DrawMapEquipmentPanel(Rect rect)
        {
            RukayaTemplateCategory picked = RukayaReplicationUI.DrawPanelHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_ScannerItemsHeader".Translate(),
                itemCategory,
                out bool changed);
            if (changed)
            {
                itemCategory = picked;
                itemScroll = Vector2.zero;
            }

            Rect searchRect = new Rect(
                rect.x,
                rect.y + RukayaReplicationUI.PanelHeaderHeight + 6f,
                rect.width,
                RukayaReplicationUI.PanelSearchHeight);
            itemSearch = Widgets.TextField(searchRect, itemSearch ?? string.Empty);

            List<Thing> items = RukayaReplicationUtility.FindEligibleThings(scanner.Map)
                .Where(x => RukayaReplicationUI.MatchesCategory(x.def, itemCategory))
                .Where(x => itemSearch.NullOrEmpty()
                    || x.LabelCap.ToString().IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => RukayaReplicationUI.ModGroupLabel(x.def))
                .ThenBy(x => x.def.label)
                .ThenByDescending(RukayaReplicationUtility.GetQuality)
                .ToList();

            List<RukayaModGroup<Thing>> groups = RukayaReplicationUI.BuildModGroups(items, x => x.def);

            Rect listRect = new Rect(rect.x, searchRect.yMax + 6f, rect.width, Mathf.Max(0f, rect.yMax - searchRect.yMax - 6f));
            RukayaReplicationUI.DrawGroupedList(
                listRect,
                groups,
                ItemRowHeight,
                ref itemScroll,
                DrawItemRow,
                "Rukaya_ScannerItemsEmpty".Translate());
        }

        private void DrawItemRow(Rect row, Rect view, Thing thing)
        {
            if (thing == selected)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            RukayaReplicationUI.DrawRowDivider(row, view);

            string subLabel = RukayaReplicationUtility.QualityLabel(RukayaReplicationUtility.GetQuality(thing));
            // The cost column is measured first so the label line can reserve room for it and never
            // runs underneath the trailing number.
            string cost = "Rukaya_CostShort".Translate(RukayaReplicationUtility.GetScanCost(thing));
            float costWidth = Text.CalcSize(cost).x + 12f;

            Rect content = RukayaReplicationUI.DrawEquipmentRow(row, thing, thing.LabelCap, subLabel, costWidth);

            Rect costRect = new Rect(content.xMax - costWidth, row.y + 4f, costWidth - 6f, (row.height - 8f) / 2f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(costRect, cost);
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(row))
            {
                selected = thing;
                selectedTemplate = null;
            }
        }

        // ------------------------------------------------------------------ right: unlocked templates

        private void DrawTemplatesPanel(Rect rect)
        {
            RukayaTemplateCategory picked = RukayaReplicationUI.DrawPanelHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_ScannerTemplatesHeader".Translate(),
                templateCategory,
                out bool changed);
            if (changed)
            {
                templateCategory = picked;
                selectedTemplate = null;
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
                "Rukaya_ScannerTemplatesEmpty".Translate());
        }

        private void DrawTemplateRow(Rect row, Rect view, ThingDef def)
        {
            if (def == selectedTemplate)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else if (Mouse.IsOver(row))
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
                "Rukaya_ScannerTemplateRow".Translate(RukayaReplicationUtility.QualityLabel(max)));

            if (Widgets.ButtonInvisible(row))
            {
                selectedTemplate = def;
                selected = null;
            }
        }

        // ------------------------------------------------------------------ middle: current project

        private void DrawProjectPanel(Rect rect)
        {
            RukayaReplicationUI.DrawListHeader(
                new Rect(rect.x, rect.y, rect.width, RukayaReplicationUI.PanelHeaderHeight),
                "Rukaya_ScannerProjectHeader".Translate());

            Rect body = new Rect(
                rect.x,
                rect.y + RukayaReplicationUI.PanelHeaderHeight + 8f,
                rect.width,
                Mathf.Max(0f, rect.yMax - rect.y - RukayaReplicationUI.PanelHeaderHeight - 8f));

            if (selectedTemplate != null)
            {
                DrawTemplateDetails(body, selectedTemplate);
                return;
            }

            if (selected == null || !RukayaReplicationUtility.IsEligibleThing(selected))
            {
                RukayaReplicationUI.DrawHint(body, "Rukaya_ScannerSelectTarget".Translate());
                return;
            }

            DrawThingDetails(body, selected);
        }

        private void DrawTemplateDetails(Rect rect, ThingDef def)
        {
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            if (library?.IsUnlocked(def) != true)
            {
                RukayaReplicationUI.DrawHint(rect, "Rukaya_ScannerSelectTarget".Translate());
                return;
            }

            ThingDef stuff = RukayaReplicationUI.ResolveDisplayStuff(def, null);
            QualityCategory current = library.GetMaxQuality(def);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label(def.LabelCap);
            listing.Label("Rukaya_MaterialLabel".Translate(stuff?.LabelCap ?? "Rukaya_NoMaterial".Translate()));
            listing.Gap(8f);
            listing.Label("Rukaya_ScannerCurrentMaximum".Translate(RukayaReplicationUtility.QualityLabel(current)));
            listing.Label("Rukaya_PrintCost".Translate(RukayaReplicationUtility.GetPrintCost(def, stuff, current)));
            listing.Label("Rukaya_PrintTime".Translate(RukayaReplicationUtility.GetPrintWorkTicks(def, stuff, current).ToStringTicksToPeriod()));
            if (current > RukayaReplicationUtility.MinPrintableQuality)
            {
                QualityCategory cheaper = current - 1;
                listing.Label("Rukaya_ScannerCheaperTier".Translate(
                    RukayaReplicationUtility.QualityLabel(cheaper),
                    RukayaReplicationUtility.GetPrintCost(def, stuff, cheaper)));
            }

            listing.Gap(12f);
            DrawUpgradeSection(listing, def, current, library);
            listing.End();
        }

        private void DrawThingDetails(Rect rect, Thing thing)
        {
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            ThingDef def = RukayaReplicationUtility.PrototypeDef(thing.def);
            QualityCategory sourceQuality = RukayaReplicationUtility.GetQuality(thing);
            QualityCategory scanQuality = RukayaReplicationUtility.ClampTemplateQuality(sourceQuality);
            bool unlocked = library?.IsUnlocked(def) == true;
            QualityCategory current = unlocked ? library.GetMaxQuality(def) : RukayaReplicationUtility.MinPrintableQuality;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label(def.LabelCap);
            if (thing.Stuff != null)
            {
                listing.Label("Rukaya_MaterialLabel".Translate(thing.Stuff.LabelCap));
            }

            listing.Gap(8f);
            listing.Label("Rukaya_ScannerSourceQuality".Translate(RukayaReplicationUtility.QualityLabel(sourceQuality)));
            listing.Label("Rukaya_ScannerCurrentMaximum".Translate(unlocked
                ? RukayaReplicationUtility.QualityLabel(current)
                : "Rukaya_Locked".Translate()));
            listing.Label("Rukaya_ScannerResultMaximum".Translate(RukayaReplicationUtility.QualityLabel(current > scanQuality ? current : scanQuality)));
            listing.Label("Rukaya_ScannerCost".Translate(RukayaReplicationUtility.GetScanCost(thing)));
            listing.Label("Rukaya_ScannerPrintCostPreview".Translate(
                RukayaReplicationUtility.GetPrintCost(def, thing.Stuff, scanQuality),
                RukayaReplicationUtility.QualityLabel(scanQuality)));
            listing.Gap(12f);

            if (listing.ButtonText("Rukaya_ScanTemplate".Translate()))
            {
                ShowResult(scanner.TryScan(thing, out string reason), reason);
            }

            if (unlocked)
            {
                listing.GapLine(12f);
                DrawUpgradeSection(listing, def, current, library);
            }

            listing.End();
        }

        private void DrawUpgradeSection(
            Listing_Standard listing,
            ThingDef def,
            QualityCategory current,
            GameComponent_RukayaReplicationLibrary library)
        {
            if (library?.IsUnlocked(def) != true)
            {
                return;
            }

            if (current >= RukayaReplicationUtility.MaxPrintableQuality)
            {
                listing.Label("Rukaya_ScannerQualityMaximum".Translate());
                return;
            }

            QualityCategory next = current + 1;
            int cost = RukayaReplicationUtility.GetQualityUpgradeCost(current, next);
            listing.Label("Rukaya_ScannerUpgradePreview".Translate(RukayaReplicationUtility.QualityLabel(next), cost));
            if (listing.ButtonText("Rukaya_ScannerUpgradeQuality".Translate()))
            {
                ShowResult(scanner.TryUpgradeQuality(def, out string reason), reason);
            }
        }

        private void ShowResult(bool success, string text)
        {
            Messages.Message(text, scanner, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }
}
