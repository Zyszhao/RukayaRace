using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RukayaRace.Replication
{
    /// <summary>
    /// Which equipment family an unlocked-template list is currently showing. The scanners and
    /// printers share this filter so both windows always speak the same vocabulary.
    /// </summary>
    public enum RukayaTemplateCategory
    {
        Weapons,
        Apparel
    }

    /// <summary>
    /// One collapsible mod folder inside a weapon or apparel list. Every entry is filed under the
    /// name of the mod that authored it, which is the same reading order the vanilla apparel policy
    /// list uses for its top level nodes: folders first, then the entries alphabetically inside them.
    /// </summary>
    public class RukayaModGroup<T>
    {
        /// <summary>Stable identity of the folder; also used as the key of its foldout state.</summary>
        public string Key;

        /// <summary>Player facing folder name (the mod name, or the "other sources" fallback).</summary>
        public string Label;

        /// <summary>Entries of this folder, kept in the order the caller supplied them.</summary>
        public readonly List<T> Items = new List<T>();
    }

    /// <summary>
    /// Shared drawing helpers for the replication terminal windows (scanner + printer).
    ///
    /// Both windows are built from the constants below - the same window size, the same three column
    /// split and the same row grid - so they cannot drift apart visually. The unified column order is:
    ///   left   : the production list (printer) or the equipment selectable on the map (scanner)
    ///   middle : the project that is currently being configured or inspected
    ///   right  : every unlocked template, folded by mod
    /// </summary>
    public static class RukayaReplicationUI
    {
        // ------------------------------------------------------------------ window frame

        /// <summary>Width shared by the scanner and the printer window.</summary>
        public const float WindowWidth = 1240f;

        /// <summary>Height shared by the scanner and the printer window.</summary>
        public const float WindowHeight = 720f;

        /// <summary>Window size handed to <see cref="Window.InitialSize"/> by both terminals.</summary>
        public static readonly Vector2 WindowSize = new Vector2(WindowWidth, WindowHeight);

        /// <summary>Horizontal gap between two neighbouring columns.</summary>
        private const float ColumnGap = 10f;

        /// <summary>Share of the usable width taken by the left (list / selection) column.</summary>
        private const float LeftColumnFraction = 0.32f;

        /// <summary>Share of the usable width taken by the middle (current project) column.</summary>
        private const float MiddleColumnFraction = 0.32f;

        /// <summary>Padding between a column frame and its content.</summary>
        public const float ColumnInnerPadding = 10f;

        private const float TitleHeight = 34f;
        private const float SummaryLineHeight = 24f;
        private const float HeaderGap = 8f;

        /// <summary>
        /// Draws the shared window header (big title plus the one line status summary underneath) and
        /// returns the y coordinate at which the three columns start. Both windows call this so their
        /// columns, and therefore their proportions, line up exactly.
        /// </summary>
        public static float DrawWindowHeader(Rect inRect, string title, string summary)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, TitleHeight), title);
            Text.Font = GameFont.Small;

            Rect summaryRect = new Rect(inRect.x, inRect.y + TitleHeight, inRect.width, SummaryLineHeight);
            Widgets.Label(summaryRect, summary);
            return summaryRect.yMax + HeaderGap;
        }

        /// <summary>
        /// Splits the window body into the three shared columns. Widths are fractions of the usable
        /// width instead of fixed pixel sizes, so both windows keep identical proportions.
        /// </summary>
        public static void SplitColumns(Rect inRect, float top, out Rect left, out Rect middle, out Rect right)
        {
            float usable = Mathf.Max(0f, inRect.width - ColumnGap * 2f);
            float leftWidth = usable * LeftColumnFraction;
            float middleWidth = usable * MiddleColumnFraction;
            float height = Mathf.Max(0f, inRect.yMax - top);

            left = new Rect(inRect.x, top, leftWidth, height);
            middle = new Rect(left.xMax + ColumnGap, top, middleWidth, height);
            right = new Rect(middle.xMax + ColumnGap, top, Mathf.Max(0f, inRect.xMax - middle.xMax - ColumnGap), height);
        }

        /// <summary>Backing frame of one column. Every column uses the same section plate.</summary>
        public static void DrawColumn(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
        }

        // ------------------------------------------------------------------ row grid

        /// <summary>Side length of the item thumbnail drawn at the left edge of every list row.</summary>
        public const float RowIconSize = 44f;

        /// <summary>Horizontal padding between the row icon and the row's first text column.</summary>
        public const float RowIconGap = 7f;

        /// <summary>Height of a two-line list row (title on top, sub-label underneath).</summary>
        public const float TwoLineRowHeight = 54f;

        /// <summary>Height of the shared panel title / category filter row.</summary>
        public const float PanelHeaderHeight = 26f;

        /// <summary>Height of the shared panel search box.</summary>
        public const float PanelSearchHeight = 28f;

        /// <summary>Width reserved on the right of a panel title for the category toggle pair.</summary>
        public const float CategoryToggleWidth = 152f;

        /// <summary>Height of a mod folder header row.</summary>
        public const float ModGroupHeaderHeight = 26f;

        /// <summary>Left indent applied to every entry inside a mod folder.</summary>
        private const float ModGroupIndent = 10f;

        /// <summary>
        /// Divider drawn between two list entries. Deliberately a little lighter than
        /// Widgets.MenuSectionBGFillColor (42,43,44) so it reads as a separation line instead of a hole.
        /// </summary>
        public static readonly Color RowDividerColor = new ColorInt(78, 80, 82).ToColor;

        /// <summary>Thickness of the per-row divider.</summary>
        public const float RowDividerHeight = 1f;

        /// <summary>Backing band of a mod folder header, slightly lighter than the column plate.</summary>
        private static readonly Color ModGroupBandColor = new ColorInt(58, 60, 63).ToColor;

        /// <summary>Gap between the two category toggle buttons (room for the selected border).</summary>
        private const float ToggleGap = 8f;

        /// <summary>Warm light fill used for the selected toggle button.</summary>
        private static readonly Color SelectedTint = new Color(0.42f, 0.37f, 0.27f, 0.95f);

        /// <summary>Bright outline of the selected toggle button (mirrors Widgets.OptionSelectedBGBorderColor).</summary>
        private static readonly Color SelectedBorderColor = new Color(0.76f, 0.67f, 0.50f);

        /// <summary>Shadow drawn along the bottom inside edge of the selected toggle button.</summary>
        private static readonly Color PressedShadowColor = new Color(0f, 0f, 0f, 0.35f);

        /// <summary>
        /// Thumbnails are rebuilt only when their key changes. CompQuality assignment regenerates the
        /// thing's graphic, so a per-row preview would otherwise be recreated on every IMGUI pass.
        /// </summary>
        private static readonly Dictionary<string, Thing> iconCache = new Dictionary<string, Thing>();

        private static int iconCacheGeneration = -1;

        /// <summary>
        /// Foldout state of every mod folder, keyed by mod name. It lives here instead of per window so
        /// the scanner and the printer always show the same folders open.
        /// </summary>
        private static readonly Dictionary<string, bool> modGroupOpenState = new Dictionary<string, bool>();

        /// <summary>
        /// Drops the cached preview things at the start of every frame so a long session cannot keep
        /// an unbounded number of generated things alive.
        /// </summary>
        private static void TrimIconCache()
        {
            int frame = Time.frameCount;
            if (iconCacheGeneration == frame)
            {
                return;
            }

            iconCacheGeneration = frame;
            if (iconCache.Count <= 256)
            {
                return;
            }

            foreach (Thing thing in iconCache.Values)
            {
                thing?.Destroy();
            }

            iconCache.Clear();
        }

        /// <summary>
        /// Returns (and caches per frame) a rendered example of the given template. The caller must not
        /// destroy the result; it is owned by the cache.
        /// </summary>
        public static Thing PreviewThing(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            if (def == null)
            {
                return null;
            }

            TrimIconCache();
            string key = def.defName + "|" + (stuff?.defName ?? string.Empty) + "|" + (int)quality;
            if (iconCache.TryGetValue(key, out Thing cached) && cached != null && !cached.Destroyed)
            {
                return cached;
            }

            Thing preview = RukayaReplicationUtility.MakePreview(def, stuff, quality);
            iconCache[key] = preview;
            return preview;
        }

        /// <summary>
        /// Draws the item thumbnail that sits at the left edge of a row.
        ///
        /// The tile is a solid square filled with the template's quality colour and the item sprite is
        /// drawn straight on top of it, so the quality is readable as a full coloured square instead of
        /// as a thin frame around a dark plate. <paramref name="drewTile"/> is false for things without
        /// any graphic, in which case the caller should not reserve any horizontal space.
        /// </summary>
        public static void DrawThingIcon(Rect rect, Thing thing, out bool drewTile)
        {
            drewTile = false;
            if (thing == null || thing.def == null || !HasIconData(thing))
            {
                return;
            }

            drewTile = true;
            Rect tile = NormalizeToSquare(rect);

            // Solid quality square, sprite on top, no inner plate: the whole tile is the quality readout.
            Widgets.DrawBoxSolid(tile, RukayaReplicationUtility.QualityColor(RukayaReplicationUtility.GetQuality(thing)));

            // Widgets.ThingIcon resolves the same texture / material / rotation / stuff tint that the
            // vanilla icon pipeline uses, so rotatable weapons and color-generator apparel both come
            // out correctly instead of as an unrotated white silhouette.
            Widgets.ThingIcon(
                tile,
                thing,
                1f,
                thing.def.rotatable ? new Rot4?(Rot4.South) : null,
                true,
                1f);
        }

        /// <summary>Expands the smaller side of a rect so the result is a centred square.</summary>
        private static Rect NormalizeToSquare(Rect rect)
        {
            if (rect.width > rect.height)
            {
                rect.x += (rect.width - rect.height) / 2f;
                rect.width = rect.height;
            }
            else if (rect.height > rect.width)
            {
                rect.y += (rect.height - rect.width) / 2f;
                rect.height = rect.width;
            }

            return rect;
        }

        /// <summary>
        /// True when Widgets.ThingIcon will actually have something to draw. Mirrors the checks the
        /// vanilla icon code performs before it returns early.
        /// </summary>
        public static bool HasIconData(Thing thing)
        {
            if (thing?.def == null)
            {
                return false;
            }

            if (thing.UIIconOverride != null)
            {
                return true;
            }

            ThingStyleDef style = thing.StyleDef;
            if (style != null && style.UIIcon != null)
            {
                return true;
            }

            if (!thing.def.uiIconPath.NullOrEmpty())
            {
                return thing.def.uiIcon != null && thing.def.uiIcon != BaseContent.BadTex;
            }

            return thing.Graphic != null && thing.Graphic != BaseContent.BadGraphic;
        }

        /// <summary>
        /// Draws one two-line equipment row: item thumbnail on the left, the item label on the first
        /// text line and a free-form sub-label on the second.
        ///
        /// Both text lines use a fixed Text.LineHeight instead of a fraction of the row height. Splitting
        /// the row in half used to leave each half shorter than a full line of text once the row padding
        /// was subtracted, which is what clipped the descenders of longer labels. Both lines are also
        /// truncated instead of wrapped, so a long mod name can never push text onto a third line.
        ///
        /// <paramref name="trailingReserve"/> keeps the label line clear of a right-aligned column the
        /// caller draws afterwards (the scanner's cost column). Returns the rect that the caller may use
        /// for trailing columns (trailing columns are not touched by this helper).
        /// </summary>
        public static Rect DrawEquipmentRow(Rect row, Thing thing, string label, string subLabel, float trailingReserve = 0f)
        {
            Rect content = DrawRowIconColumn(row, thing);

            float textHeight = row.height - 8f;
            float top = row.y + (row.height - textHeight) / 2f;
            float labelWidth = Mathf.Max(0f, content.width - 4f - trailingReserve);
            Rect labelRect = new Rect(content.x + 2f, top, labelWidth, textHeight / 2f);
            Rect subRect = new Rect(content.x + 2f, labelRect.yMax, Mathf.Max(0f, content.width - 4f), textHeight / 2f);

            Widgets.Label(labelRect, (label ?? string.Empty).Truncate(labelRect.width));
            if (!subLabel.NullOrEmpty())
            {
                GameFont previousFont = Text.Font;
                Text.Font = GameFont.Tiny;
                Widgets.Label(subRect, subLabel.Truncate(subRect.width));
                Text.Font = previousFont;
            }

            return content;
        }

        /// <summary>
        /// Reserves the icon column at the left of a row and draws both the thumbnail and the thin
        /// vertical separator between the icon column and the row text. Returns the same rect with its
        /// left edge pushed past the icon column, which is the area that remains for text.
        /// </summary>
        public static Rect DrawRowIconColumn(Rect row, Thing thing)
        {
            Rect iconRect = NormalizeToSquare(new Rect(
                row.x + 2f,
                row.y + (row.height - RowIconSize) / 2f,
                RowIconSize,
                RowIconSize));

            DrawThingIcon(iconRect, thing, out bool drewTile);
            if (!drewTile)
            {
                return row;
            }

            float columnRight = iconRect.xMax + 2f;
            Widgets.DrawBoxSolid(new Rect(columnRight, row.y + 2f, 1f, row.height - 4f), RowDividerColor);

            Rect remaining = row;
            remaining.xMin = columnRight + RowIconGap;
            return remaining;
        }

        /// <summary>
        /// Horizontal separator drawn underneath a list entry. Always placed on the shared row grid so
        /// the lines stay continuous across the whole list instead of following the highlight rect.
        /// </summary>
        public static void DrawRowDivider(Rect rowRect, Rect view)
        {
            float y = rowRect.yMax - RowDividerHeight;
            Widgets.DrawBoxSolid(new Rect(view.x, y, view.width, RowDividerHeight), RowDividerColor);
        }

        /// <summary>Bold small-font list header used above every scrollable list in the windows.</summary>
        public static void DrawListHeader(Rect rect, string text)
        {
            DrawListTitle(rect, text);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, RowDividerHeight), RowDividerColor);
        }

        /// <summary>
        /// Free-standing hint text (an empty panel state). Word wrap is enabled so a translated hint
        /// that is longer than one line never gets clipped by the narrow unified columns.
        /// </summary>
        public static void DrawHint(Rect rect, TaggedString text)
        {
            Text.WordWrap = true;
            Widgets.Label(rect, text);
            Text.WordWrap = false;
        }

        /// <summary>Title text of a panel header row, sitting on the bottom edge of its rect.</summary>
        public static void DrawListTitle(Rect rect, string text)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(rect, (text ?? string.Empty).Truncate(rect.width));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Title row shared by every list panel: the panel title on the left, the weapon/apparel filter
        /// on the right, and one continuous divider underneath both. Both windows and all four panels
        /// use this so their headers are pixel identical.
        /// </summary>
        public static RukayaTemplateCategory DrawPanelHeader(
            Rect rect,
            string title,
            RukayaTemplateCategory current,
            out bool changed)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, PanelHeaderHeight);
            Rect toggleRect = new Rect(headerRect.xMax - CategoryToggleWidth, headerRect.y, CategoryToggleWidth, PanelHeaderHeight);
            DrawListTitle(new Rect(headerRect.x, headerRect.y, Mathf.Max(0f, headerRect.width - CategoryToggleWidth - 6f), PanelHeaderHeight), title);

            RukayaTemplateCategory picked = DrawCategoryToggles(toggleRect, current, out changed);
            Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, RowDividerHeight), RowDividerColor);
            return picked;
        }

        /// <summary>
        /// True when the def belongs to the requested filter category. Weapons keep priority so a
        /// hypothetical weapon-apparel hybrid is never listed twice.
        /// </summary>
        public static bool MatchesCategory(ThingDef def, RukayaTemplateCategory category)
        {
            if (def == null)
            {
                return false;
            }

            return category == RukayaTemplateCategory.Weapons ? def.IsWeapon : def.IsApparel;
        }

        /// <summary>
        /// Two side-by-side toggle buttons that switch a template list between weapons and apparel.
        /// Returns the (possibly changed) category.
        ///
        /// This deliberately does not use Widgets.ButtonText: both buttons would then be handed to IMGUI
        /// as an empty-content button, i.e. the *same* control, and a single click ends up consumed by
        /// whichever copy was drawn last — which is why clicking "apparel" could silently do nothing or
        /// appear to select "weapons". Each button is drawn with its own explicit texture and the click
        /// is resolved from the mouse position inside its own rect.
        /// </summary>
        public static RukayaTemplateCategory DrawCategoryToggles(
            Rect rect,
            RukayaTemplateCategory current,
            out bool changed)
        {
            changed = false;
            float half = (rect.width - ToggleGap) / 2f;
            Rect weaponsRect = new Rect(rect.x, rect.y, half, rect.height);
            Rect apparelRect = new Rect(rect.x + half + ToggleGap, rect.y, half, rect.height);

            RukayaTemplateCategory picked = current;
            if (DrawCategoryButton(weaponsRect, "Rukaya_CategoryWeapons".Translate(), current == RukayaTemplateCategory.Weapons))
            {
                picked = RukayaTemplateCategory.Weapons;
            }

            if (DrawCategoryButton(apparelRect, "Rukaya_CategoryApparel".Translate(), current == RukayaTemplateCategory.Apparel))
            {
                picked = RukayaTemplateCategory.Apparel;
            }

            changed = picked != current;
            return picked;
        }

        /// <summary>
        /// One category button.
        ///
        /// The click is resolved with Widgets.ButtonText so the label text itself forms IMGUI's control
        /// identity. An earlier version drew the button and then called Widgets.ButtonInvisible, which
        /// hands IMGUI an empty-content button; both toggles then had no content to tell them apart and a
        /// single click could be consumed by the wrong one, so switching category appeared to do nothing.
        /// The "held down" look is painted on top of the vanilla button art afterwards, which means the
        /// selected state wins over hover and press instead of fighting with them.
        /// </summary>
        private static bool DrawCategoryButton(Rect rect, string label, bool selected)
        {
            bool pressed = Widgets.ButtonText(rect, label);

            if (selected)
            {
                // Warm fill + thick bright border + inner bottom shadow: the vanilla
                // Widgets.DrawOptionSelected look, i.e. a button that stays visibly pressed in.
                Widgets.DrawBoxSolid(rect, SelectedTint);

                Color previousColor = GUI.color;
                GUI.color = SelectedBorderColor;
                Widgets.DrawBox(rect, 2, null);
                GUI.color = previousColor;

                Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.yMax - 3f, rect.width - 4f, 2f), PressedShadowColor);

                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, label);
                Text.Anchor = previousAnchor;
            }

            return pressed;
        }

        /// <summary>
        /// Apparel with no stuff (or weapons that ignore stuff) still need a stable display material;
        /// this mirrors what the printer will actually use for the bill.
        /// </summary>
        public static ThingDef ResolveDisplayStuff(ThingDef def, ThingDef stuff)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return null;
            }

            return stuff ?? GenStuff.DefaultStuffFor(def);
        }

        // ------------------------------------------------------------------ mod folded lists

        /// <summary>
        /// Folder name a def is filed under. Mods that leave the author blank (core defs pulled in by
        /// another generator, for instance) land in the shared "other sources" folder.
        /// </summary>
        public static string ModGroupLabel(ThingDef def)
        {
            string name = def?.modContentPack?.Name;
            if (!name.NullOrEmpty())
            {
                return name;
            }

            return "Rukaya_ModGroupOther".Translate();
        }

        /// <summary>True when the mod folder with this name is currently expanded.</summary>
        public static bool IsModGroupOpen(string key)
        {
            return !modGroupOpenState.TryGetValue(key, out bool open) || open;
        }

        private static void ToggleModGroup(string key, bool open)
        {
            modGroupOpenState[key] = !open;
            if (open)
            {
                SoundDefOf.TabClose.PlayOneShotOnCamera(null);
            }
            else
            {
                SoundDefOf.TabOpen.PlayOneShotOnCamera(null);
            }
        }

        /// <summary>
        /// Sorted, category-filtered view of every unlocked template, ordered the way the vanilla
        /// apparel policy list orders its entries: by the folder (mod) name first, and alphabetically
        /// by the def label inside a folder.
        /// </summary>
        public static List<ThingDef> UnlockedTemplates(string search, RukayaTemplateCategory category)
        {
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            if (library == null)
            {
                return new List<ThingDef>();
            }

            return library.UnlockedThingDefs()
                .Where(x => MatchesCategory(x, category))
                .Where(x => search.NullOrEmpty()
                    || x.LabelCap.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(ModGroupLabel)
                .ThenBy(x => x.label)
                .ToList();
        }

        /// <summary>
        /// Same list as <see cref="UnlockedTemplates"/>, already folded into collapsible mod folders.
        /// </summary>
        public static List<RukayaModGroup<ThingDef>> GroupedUnlockedTemplates(string search, RukayaTemplateCategory category)
        {
            return BuildModGroups(UnlockedTemplates(search, category), x => x);
        }

        /// <summary>
        /// Folds an already sorted sequence into mod folders. The folder order follows the folder name,
        /// the entry order inside a folder is the order the caller supplied, which both callers keep in
        /// label order.
        /// </summary>
        public static List<RukayaModGroup<T>> BuildModGroups<T>(IEnumerable<T> items, Func<T, ThingDef> defSelector)
        {
            Dictionary<string, RukayaModGroup<T>> byLabel = new Dictionary<string, RukayaModGroup<T>>();
            List<RukayaModGroup<T>> groups = new List<RukayaModGroup<T>>();

            foreach (T item in items)
            {
                ThingDef def = defSelector(item);
                if (def == null)
                {
                    continue;
                }

                string label = ModGroupLabel(def);
                if (!byLabel.TryGetValue(label, out RukayaModGroup<T> group))
                {
                    group = new RukayaModGroup<T> { Key = label, Label = label };
                    byLabel[label] = group;
                    groups.Add(group);
                }

                group.Items.Add(item);
            }

            groups.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCulture));
            return groups;
        }

        /// <summary>Total scroll height of a folded list, honouring the current foldout states.</summary>
        public static float MeasureGroupedList<T>(List<RukayaModGroup<T>> groups, float rowHeight)
        {
            if (groups == null || groups.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                RukayaModGroup<T> group = groups[i];
                total += ModGroupHeaderHeight;
                if (IsModGroupOpen(group.Key))
                {
                    total += group.Items.Count * rowHeight;
                }
            }

            return total;
        }

        /// <summary>
        /// Draws a folded list: one header row per mod folder with a foldout arrow, then the entries of
        /// every expanded folder, indented underneath their folder. <paramref name="drawRow"/> receives
        /// (entry row, scroll view rect, entry) and stays responsible for that panel's own row content.
        /// </summary>
        public static void DrawGroupedList<T>(
            Rect outRect,
            List<RukayaModGroup<T>> groups,
            float rowHeight,
            ref Vector2 scroll,
            Action<Rect, Rect, T> drawRow,
            TaggedString emptyLabel)
        {
            if (groups == null)
            {
                groups = new List<RukayaModGroup<T>>();
            }

            float contentHeight = MeasureGroupedList(groups, rowHeight);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f), Mathf.Max(outRect.height, contentHeight));
            Widgets.BeginScrollView(outRect, ref scroll, view);

            if (groups.Count == 0)
            {
                DrawHint(new Rect(0f, 0f, view.width, 24f), emptyLabel);
            }

            float y = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                RukayaModGroup<T> group = groups[i];

                // The foldout state is captured before the header is drawn, so a click that collapses a
                // folder takes effect on the next pass instead of leaving this pass half relaid out.
                bool open = IsModGroupOpen(group.Key);
                DrawModGroupHeader(new Rect(0f, y, view.width, ModGroupHeaderHeight), group, open);
                y += ModGroupHeaderHeight;

                if (!open)
                {
                    continue;
                }

                for (int j = 0; j < group.Items.Count; j++)
                {
                    Rect row = new Rect(ModGroupIndent, y, Mathf.Max(0f, view.width - ModGroupIndent), rowHeight);
                    if (drawRow != null)
                    {
                        drawRow(row, view, group.Items[j]);
                    }

                    y += rowHeight;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>Header band of one mod folder: foldout arrow, folder name and entry count.</summary>
        private static void DrawModGroupHeader<T>(Rect row, RukayaModGroup<T> group, bool open)
        {
            Widgets.DrawBoxSolid(row, ModGroupBandColor);
            Widgets.DrawBoxSolid(new Rect(row.x, row.yMax - RowDividerHeight, row.width, RowDividerHeight), RowDividerColor);

            Rect arrowRect = new Rect(row.x + 3f, row.y + (row.height - 18f) / 2f, 18f, 18f);
            if (Widgets.ButtonImage(arrowRect, open ? TexButton.Collapse : TexButton.Reveal, true, null))
            {
                ToggleModGroup(group.Key, open);
            }

            float textLeft = arrowRect.xMax + 6f;
            float textRight = row.xMax - 50f;
            Rect labelRect = new Rect(textLeft, row.y, Mathf.Max(0f, textRight - textLeft), row.height);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, (group.Label ?? string.Empty).Truncate(Mathf.Max(0f, labelRect.width - 4f)));

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(row.xMax - 48f, row.y, 42f, row.height), "Rukaya_ModGroupCount".Translate(group.Items.Count));
            Text.Anchor = previousAnchor;

            if (Widgets.ButtonInvisible(labelRect))
            {
                ToggleModGroup(group.Key, open);
            }
        }
    }
}
