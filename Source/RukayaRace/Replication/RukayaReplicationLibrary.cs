using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    public enum RukayaPrintRepeatMode
    {
        RepeatCount,
        Forever,
        UntilTarget
    }

    public class RukayaPrintBill : IExposable
    {
        public string billId;
        public string thingDefName;
        public string stuffDefName;
        public QualityCategory quality = QualityCategory.Normal;
        public RukayaPrintRepeatMode repeatMode = RukayaPrintRepeatMode.RepeatCount;
        public int targetCount = 1;
        public int completedCount;
        public bool suspended;

        public ThingDef ThingDef => thingDefName.NullOrEmpty() ? null : RukayaReplicationUtility.PrototypeDef(DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName));
        public ThingDef StuffDef => stuffDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);

        public string Label
        {
            get
            {
                ThingDef def = ThingDef;
                return def == null ? "Unknown" : (StuffDef == null ? def.LabelCap : (StuffDef.LabelCap + " " + def.LabelCap));
            }
        }

        public bool IsFinished(Map map)
        {
            if (repeatMode == RukayaPrintRepeatMode.RepeatCount)
            {
                return completedCount >= Mathf.Max(1, targetCount);
            }

            if (repeatMode == RukayaPrintRepeatMode.UntilTarget)
            {
                return RukayaReplicationUtility.CountMatchingStock(map, ThingDef, StuffDef, quality) >= Mathf.Max(1, targetCount);
            }

            return false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref billId, "billId");
            Scribe_Values.Look(ref thingDefName, "thingDefName");
            Scribe_Values.Look(ref stuffDefName, "stuffDefName");
            Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
            Scribe_Values.Look(ref repeatMode, "repeatMode", RukayaPrintRepeatMode.RepeatCount);
            Scribe_Values.Look(ref targetCount, "targetCount", 1);
            Scribe_Values.Look(ref completedCount, "completedCount", 0);
            Scribe_Values.Look(ref suspended, "suspended", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && billId.NullOrEmpty())
            {
                billId = Guid.NewGuid().ToString("N");
            }
        }
    }

    public class GameComponent_RukayaReplicationLibrary : GameComponent
    {
        private List<string> unlockedDefNames = new List<string>();
        private List<int> unlockedQualityValues = new List<int>();
        private Dictionary<string, QualityCategory> unlocked = new Dictionary<string, QualityCategory>();

        public GameComponent_RukayaReplicationLibrary(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref unlockedDefNames, "unlockedDefNames", LookMode.Value);
            Scribe_Collections.Look(ref unlockedQualityValues, "unlockedQualityValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                unlocked = new Dictionary<string, QualityCategory>();
                int count = Mathf.Min(unlockedDefNames?.Count ?? 0, unlockedQualityValues?.Count ?? 0);
                for (int i = 0; i < count; i++)
                {
                    if (!unlockedDefNames[i].NullOrEmpty())
                    {
                        unlocked[unlockedDefNames[i]] = ClampQuality((QualityCategory)unlockedQualityValues[i]);
                    }
                }

                NormalizeLoadedQualities();
            }
        }

        public bool IsUnlocked(ThingDef def)
        {
            def = RukayaReplicationUtility.PrototypeDef(def);
            return def != null && unlocked.ContainsKey(def.defName);
        }

        public QualityCategory GetMaxQuality(ThingDef def)
        {
            def = RukayaReplicationUtility.PrototypeDef(def);
            QualityCategory value;
            return def != null && unlocked.TryGetValue(def.defName, out value) ? value : QualityCategory.Awful;
        }

        public bool UnlockOrUpgrade(ThingDef def, QualityCategory quality)
        {
            def = RukayaReplicationUtility.PrototypeDef(def);
            if (!RukayaReplicationUtility.IsEligibleDef(def))
            {
                return false;
            }

            quality = ClampQuality(quality);
            QualityCategory current = GetMaxQuality(def);
            if (IsUnlocked(def) && quality <= current)
            {
                return false;
            }

            unlocked[def.defName] = quality;
            SyncScribeLists();
            return true;
        }

        /// <summary>
        /// Every unlocked template, ordered the way the terminal windows read them: by the authoring
        /// mod first, then alphabetically by the def label inside a mod - the same reading order the
        /// vanilla apparel policy list uses. Enumeration order is therefore independent of the order in
        /// which the player happened to scan things, and every consumer of the library sees it.
        /// </summary>
        public IEnumerable<ThingDef> UnlockedThingDefs()
        {
            List<ThingDef> defs = new List<ThingDef>();
            foreach (string defName in unlocked.Keys)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def != null && RukayaReplicationUtility.IsEligibleDef(def))
                {
                    defs.Add(def);
                }
            }

            return defs
                .OrderBy(x => x.modContentPack?.Name ?? string.Empty)
                .ThenBy(x => x.label)
                .ToList();
        }

        private void NormalizeLoadedQualities()
        {
            // Merge old unique-weapon templates into their ordinary prototype, retaining the best tier.
            foreach (string key in unlocked.Keys.ToList())
            {
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                ThingDef prototype = RukayaReplicationUtility.PrototypeDef(original);
                if (prototype == original)
                    continue;
                QualityCategory quality = unlocked[key];
                unlocked.Remove(key);
                if (prototype != null && (!unlocked.TryGetValue(prototype.defName, out QualityCategory current) || quality > current))
                    unlocked[prototype.defName] = quality;
            }
            List<string> missing = unlocked.Keys.Where(x => DefDatabase<ThingDef>.GetNamedSilentFail(x) == null).ToList();
            for (int i = 0; i < missing.Count; i++)
            {
                unlocked.Remove(missing[i]);
            }

            foreach (string key in unlocked.Keys.ToList())
            {
                unlocked[key] = ClampQuality(unlocked[key]);
            }

            SyncScribeLists();
        }

        private void SyncScribeLists()
        {
            unlockedDefNames = unlocked.Keys.OrderBy(x => x).ToList();
            unlockedQualityValues = unlockedDefNames.Select(x => (int)unlocked[x]).ToList();
        }

        private static QualityCategory ClampQuality(QualityCategory quality)
        {
            return RukayaReplicationUtility.ClampTemplateQuality(quality);
        }
    }

    public static class RukayaReplicationUtility
    {
        public const int DefaultReservoirCapacity = 1000;
        public const int MinReservoirCapacity = 100;
        public const int MaxReservoirCapacity = 1000;
        public const int ScanValueDivisor = 40;
        public const int PrintValueDivisor = 20;
        public const int MinimumWorkTicks = 600;
        public const float WorkTicksPerMarketValue = 90f;

        /// <summary>
        /// Lowest quality the printer may produce. Sub-normal qualities are
        /// deliberately allowed: an awful or poor copy is a cheap stopgap, and the whole point of the
        /// discount table below is that the player can trade quality for active matter.
        /// </summary>
        public const QualityCategory MinPrintableQuality = QualityCategory.Awful;

        /// <summary>Highest quality reachable through template refinement.</summary>
        public const QualityCategory MaxPrintableQuality = QualityCategory.Legendary;

        /// <summary>
        /// Fraction of the normal-quality print cost charged for a given quality. Vanilla already
        /// scales MarketValue with quality (0.5x awful, 0.75x poor), but weapons and apparel only
        /// move a little, so an awful rifle would otherwise cost barely less than a normal one. These
        /// explicit multipliers make the sub-normal tiers genuinely cheap to print.
        /// </summary>
        public static float QualityCostFactor(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful:
                    return 0.35f;
                case QualityCategory.Poor:
                    return 0.6f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Work (in ticks) multiplier for a given quality. Sub-normal copies are also quicker to print;
        /// anything at or above normal keeps the existing market-value driven duration.
        /// </summary>
        public static float QualityWorkFactor(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful:
                    return 0.5f;
                case QualityCategory.Poor:
                    return 0.75f;
                default:
                    return 1f;
            }
        }

        /// <summary>Template maximums always unlock at least normal-quality printing.</summary>
        public static QualityCategory ClampTemplateQuality(QualityCategory quality)
        {
            return (QualityCategory)Mathf.Clamp((int)quality, (int)QualityCategory.Normal, (int)MaxPrintableQuality);
        }

        /// <summary>Clamps any quality value into the printable quality range.</summary>
        public static QualityCategory ClampPrintableQuality(QualityCategory quality)
        {
            return (QualityCategory)Mathf.Clamp(
                (int)quality,
                (int)MinPrintableQuality,
                (int)MaxPrintableQuality);
        }

        /// <summary>
        /// Row accent colour for a quality tier. Used as the stripe under every list thumbnail so the
        /// quality of a template is readable at a glance.
        /// </summary>
        public static Color QualityColor(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful:
                    return new Color(0.60f, 0.29f, 0.26f);
                case QualityCategory.Poor:
                    return new Color(0.68f, 0.47f, 0.24f);
                case QualityCategory.Normal:
                    return new Color(0.60f, 0.62f, 0.64f);
                case QualityCategory.Good:
                    return new Color(0.44f, 0.68f, 0.36f);
                case QualityCategory.Excellent:
                    return new Color(0.36f, 0.62f, 0.78f);
                case QualityCategory.Masterwork:
                    return new Color(0.72f, 0.52f, 0.86f);
                case QualityCategory.Legendary:
                    return new Color(0.94f, 0.78f, 0.32f);
                default:
                    return Color.grey;
            }
        }

        public static GameComponent_RukayaReplicationLibrary Library => Current.Game?.GetComponent<GameComponent_RukayaReplicationLibrary>();

        public static bool IsEligibleDef(ThingDef def)
        {
            def = PrototypeDef(def);
            return def != null
                && def.category == ThingCategory.Item
                && def.EverHaulable
                && !def.destroyOnDrop
                && (def.IsWeapon || def.apparel != null)
                && def.comps != null
                // Persona traits are rolled for each new instance, so a Def-only template cannot
                // preserve them or provide stable preview values. Include derived mod components.
                && !def.comps.Any(x => x.compClass != null && typeof(CompBladelinkWeapon).IsAssignableFrom(x.compClass))
                && def.comps.Any(x => x.compClass == typeof(CompQuality));
        }

        public static bool IsEligibleThing(Thing thing)
        {
            return thing != null
                && !thing.Destroyed
                && thing.stackCount == 1
                && thing.questTags.NullOrEmpty()
                && IsEligibleDef(thing.def)
                && thing.TryGetComp<CompQuality>() != null;
        }

        public static QualityCategory GetQuality(Thing thing)
        {
            return thing?.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
        }

        public static IEnumerable<Thing> FindEligibleThings(Map map)
        {
            if (map == null)
            {
                yield break;
            }

            HashSet<Thing> found = new HashSet<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (IsEligibleThing(thing) && found.Add(thing))
                {
                    yield return thing;
                }
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                if (pawn.equipment?.AllEquipmentListForReading != null)
                {
                    foreach (Thing thing in pawn.equipment.AllEquipmentListForReading)
                    {
                        if (IsEligibleThing(thing) && found.Add(thing))
                        {
                            yield return thing;
                        }
                    }
                }

                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (Thing thing in pawn.apparel.WornApparel)
                    {
                        if (IsEligibleThing(thing) && found.Add(thing))
                        {
                            yield return thing;
                        }
                    }
                }

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (IsEligibleThing(thing) && found.Add(thing))
                        {
                            yield return thing;
                        }
                    }
                }
            }
        }

        public static List<ThingDef> AllowedStuffs(ThingDef def)
        {
            List<ThingDef> result = new List<ThingDef>();
            if (def == null || !def.MadeFromStuff)
            {
                result.Add(null);
                return result;
            }

            foreach (ThingDef stuff in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!stuff.IsStuff || stuff.stuffProps?.categories == null || def.stuffCategories == null)
                {
                    continue;
                }

                if (stuff.stuffProps.categories.Any(x => def.stuffCategories.Contains(x)))
                {
                    result.Add(stuff);
                }
            }

            if (result.Count == 0)
            {
                ThingDef fallback = GenStuff.DefaultStuffFor(def);
                if (fallback != null)
                {
                    result.Add(fallback);
                }
            }

            return result.OrderBy(x => x?.label ?? string.Empty).ToList();
        }

        public static Thing MakePreview(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            def = PrototypeDef(def);
            if (!IsEligibleDef(def))
            {
                return null;
            }

            if (def.MadeFromStuff && stuff == null)
            {
                stuff = GenStuff.DefaultStuffFor(def);
            }

            Thing preview = ThingMaker.MakeThing(def, stuff);
            CompQuality compQuality = preview.TryGetComp<CompQuality>();
            compQuality?.SetQuality(quality, ArtGenerationContext.Outsider);
            preview.TryGetComp<CompArt>()?.Clear();
            preview.TryGetComp<CompBiocodable>()?.UnCode();
            preview.HitPoints = preview.MaxHitPoints;
            return preview;
        }

        private static bool HasWeaponTraits(ThingDef def)
        {
            return def?.comps?.Any(x => x.compClass != null && typeof(CompUniqueWeapon).IsAssignableFrom(x.compClass)) == true;
        }

        // Odyssey variants link to their ordinary weapon. Never instantiate a random-trait variant
        // for pricing, previews or printing; the actual scanned weapon remains untouched.
        public static ThingDef PrototypeDef(ThingDef def)
        {
            if (!HasWeaponTraits(def))
                return def;
            ThingDef prototype = def.descriptionHyperlinks?
                .Select(x => x.def as ThingDef)
                .FirstOrDefault(x => x != null && x.IsWeapon && !HasWeaponTraits(x));
            if (prototype == null && def.defName.EndsWith("_Unique", StringComparison.Ordinal))
                prototype = DefDatabase<ThingDef>.GetNamedSilentFail(def.defName.Substring(0, def.defName.Length - "_Unique".Length));
            return prototype != null && prototype.IsWeapon && !HasWeaponTraits(prototype) ? prototype : null;
        }

        public static float StandardizedMarketValue(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            Thing preview = MakePreview(def, stuff, quality);
            if (preview == null)
            {
                return 0f;
            }

            float value = preview.GetStatValue(StatDefOf.MarketValue);
            preview.Destroy();
            return Mathf.Max(0f, value);
        }

        public static int GetScanCost(Thing source)
        {
            return Mathf.Max(1, Mathf.CeilToInt(StandardizedMarketValue(source.def, source.Stuff, GetQuality(source)) / ScanValueDivisor));
        }

        public static int GetPrintCost(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            quality = ClampPrintableQuality(quality);
            float baseValue = StandardizedMarketValue(def, stuff, quality) / PrintValueDivisor;
            return Mathf.Max(1, Mathf.CeilToInt(baseValue * QualityCostFactor(quality)));
        }

        public static int GetPrintWorkTicks(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            quality = ClampPrintableQuality(quality);
            float baseTicks = StandardizedMarketValue(def, stuff, quality) * WorkTicksPerMarketValue * QualityWorkFactor(quality);
            return Mathf.Max(MinimumWorkTicks, Mathf.CeilToInt(baseTicks));
        }

        public static int GetQualityUpgradeCost(QualityCategory from, QualityCategory to)
        {
            QualityCategory capped = ClampPrintableQuality(to);
            if (from >= capped)
            {
                return 0;
            }

            int total = 0;
            for (int value = Mathf.Max((int)MinPrintableQuality, (int)from) + 1; value <= (int)capped; value++)
            {
                switch ((QualityCategory)value)
                {
                    // Reaching Poor and Normal from below is deliberately cheap: these are the tiers a
                    // player can already scan directly, so gating them behind a large fee would only
                    // punish templates that were recovered from an awful or poor prototype.
                    case QualityCategory.Poor: total += 5; break;
                    case QualityCategory.Normal: total += 10; break;
                    case QualityCategory.Good: total += 25; break;
                    case QualityCategory.Excellent: total += 50; break;
                    case QualityCategory.Masterwork: total += 100; break;
                    case QualityCategory.Legendary: total += 200; break;
                }
            }

            return total;
        }

        public static int CountMatchingStock(Map map, ThingDef def, ThingDef stuff, QualityCategory minimumQuality)
        {
            if (map == null || def == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing.Spawned && thing.IsInValidStorage() && thing.Stuff == stuff && GetQuality(thing) >= minimumQuality)
                {
                    count += thing.stackCount;
                }
            }

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                IEnumerable<Thing> held = Enumerable.Empty<Thing>();
                if (pawn.equipment?.AllEquipmentListForReading != null)
                {
                    held = held.Concat(pawn.equipment.AllEquipmentListForReading);
                }
                if (pawn.apparel?.WornApparel != null)
                {
                    held = held.Concat(pawn.apparel.WornApparel);
                }
                if (pawn.inventory?.innerContainer != null)
                {
                    held = held.Concat(pawn.inventory.innerContainer);
                }

                foreach (Thing thing in held)
                {
                    if (thing.def == def && thing.Stuff == stuff && GetQuality(thing) >= minimumQuality)
                    {
                        count += thing.stackCount;
                    }
                }
            }

            return count;
        }

        public static string QualityLabel(QualityCategory quality)
        {
            return quality.GetLabel().CapitalizeFirst();
        }
    }
}
