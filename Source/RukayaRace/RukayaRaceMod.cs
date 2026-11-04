using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace
{
    public class RukayaRaceMod : Mod
    {
        public static RukayaRaceSettings Settings;

        private string fabricatorWorkSpeedBuffer;
        private string refinerDailyOutputBuffer;
        private string advancedRefinerDailyOutputBuffer;
        private string replicationReservoirCapacityBuffer;
        private bool lastUseLightningForElectricWeapons;
        private Vector2 settingsScroll;
        private float settingsHeight = 1100f;

        public RukayaRaceMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RukayaRaceSettings>();
            Settings.ClampValues();
            fabricatorWorkSpeedBuffer = FormatSpeed(Settings.fabricatorWorkSpeedFactor);
            refinerDailyOutputBuffer = FormatSpeed(Settings.refinerDailyOutput);
            advancedRefinerDailyOutputBuffer = FormatSpeed(Settings.advancedRefinerDailyOutput);
            replicationReservoirCapacityBuffer = FormatSpeed(Settings.replicationReservoirCapacity);
            lastUseLightningForElectricWeapons = Settings.useLightningForElectricWeapons;
            LongEventHandler.ExecuteWhenFinished(ApplySettingsToDefs);
        }

        public override string SettingsCategory()
        {
            return "Rukaya_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.ClampValues();

            Rect view = new Rect(0f, 0f, inRect.width - 20f, settingsHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, view);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);
            DrawFloatSetting(
                listing,
                "Rukaya_FabricatorWorkSpeedLabel".Translate(),
                "Rukaya_FabricatorWorkSpeedDescription".Translate(),
                ref Settings.fabricatorWorkSpeedFactor,
                ref fabricatorWorkSpeedBuffer,
                RukayaRaceSettings.DefaultFabricatorWorkSpeed,
                RukayaRaceSettings.MinSettingValue,
                RukayaRaceSettings.MaxSettingValue);

            listing.Gap(8f);

            DrawFloatSetting(
                listing,
                "Rukaya_RefinerDailyOutputLabel".Translate(),
                "Rukaya_RefinerDailyOutputDescription".Translate(),
                ref Settings.refinerDailyOutput,
                ref refinerDailyOutputBuffer,
                RukayaRaceSettings.DefaultRefinerDailyOutput,
                RukayaRaceSettings.MinSettingValue,
                RukayaRaceSettings.MaxDailyOutputValue);

            listing.Gap(8f);

            DrawFloatSetting(
                listing,
                "Rukaya_AdvancedRefinerDailyOutputLabel".Translate(),
                "Rukaya_AdvancedRefinerDailyOutputDescription".Translate(),
                ref Settings.advancedRefinerDailyOutput,
                ref advancedRefinerDailyOutputBuffer,
                RukayaRaceSettings.DefaultAdvancedRefinerDailyOutput,
                RukayaRaceSettings.MinSettingValue,
                RukayaRaceSettings.MaxDailyOutputValue);

            listing.Gap(12f);

            DrawFloatSetting(
                listing,
                "Rukaya_ReplicationReservoirCapacityLabel".Translate(),
                "Rukaya_ReplicationReservoirCapacityDescription".Translate(),
                ref Settings.replicationReservoirCapacity,
                ref replicationReservoirCapacityBuffer,
                RukayaRaceSettings.DefaultReplicationReservoirCapacity,
                RukayaRaceSettings.MinReplicationReservoirCapacity,
                RukayaRaceSettings.MaxReplicationReservoirCapacity);

            listing.Gap(12f);
            listing.CheckboxLabeled(
                "Rukaya_UseLightningForElectricWeaponsLabel".Translate(),
                ref Settings.useLightningForElectricWeapons,
                "Rukaya_UseLightningForElectricWeaponsDescription".Translate());

            if (Settings.useLightningForElectricWeapons != lastUseLightningForElectricWeapons)
            {
                lastUseLightningForElectricWeapons = Settings.useLightningForElectricWeapons;
                NotifyActiveMatterWeaponsSettingsChanged();
            }

            listing.Gap(18f);
            listing.Label("Rukaya_TrioSettings".Translate());
            DrawProbability(listing, "Rukaya_TrioInitial", ref Settings.trioInitialChance);
            DrawProbability(listing, "Rukaya_TrioDecay", ref Settings.trioDecay);
            DrawProbability(listing, "Rukaya_TrioFloor", ref Settings.trioMinimumChance);
            DrawProbability(listing, "Rukaya_TrioDeath", ref Settings.trioDeathChance);
            if (listing.ButtonText("Rukaya_TrioReset".Translate()))
            {
                Settings.trioInitialChance = 0.12f;
                Settings.trioDecay = 0.85f;
                Settings.trioMinimumChance = 0.02f;
                Settings.trioDeathChance = 0.35f;
            }
            Settings.ClampValues();
            settingsHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawProbability(Listing_Standard listing, string key, ref float value)
        {
            listing.Label(key.Translate() + ": " + value.ToString("P0", CultureInfo.CurrentCulture));
            value = Mathf.Round(listing.Slider(value * 100f, 0f, 100f)) / 100f;
        }

        public override void WriteSettings()
        {
            Settings.ClampValues();
            ApplySettingsToDefs();
            NotifyActiveMatterWeaponsSettingsChanged();
            base.WriteSettings();
        }

        public static void ApplySettingsToDefs()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.ClampValues();
            ThingDef fabricatorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Rukaya_Fabricator");
            if (fabricatorDef != null)
            {
                fabricatorDef.SetStatBaseValue(StatDefOf.WorkTableWorkSpeedFactor, Settings.fabricatorWorkSpeedFactor);
            }

            ApplyDailyOutputToSpawner("Rukaya_Refiner", Settings.refinerDailyOutput);
            ApplyDailyOutputToSpawner("Rukaya_AdvancedRefiner", Settings.advancedRefinerDailyOutput);
        }

        private static void ApplyDailyOutputToSpawner(string defName, float dailyOutput)
        {
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            CompProperties_Spawner spawnerProps = thingDef?.GetCompProperties<CompProperties_Spawner>();
            if (spawnerProps == null)
            {
                return;
            }

            int intervalTicks = Mathf.Max(1, Mathf.RoundToInt(60000f * spawnerProps.spawnCount / dailyOutput));
            spawnerProps.spawnIntervalRange = new IntRange(intervalTicks, intervalTicks);
        }

        private static void NotifyActiveMatterWeaponsSettingsChanged()
        {
            if (Current.Game == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    List<ThingWithComps> equipment = pawn.equipment?.AllEquipmentListForReading;
                    if (equipment == null)
                    {
                        continue;
                    }

                    foreach (ThingWithComps thing in equipment)
                    {
                        thing.GetComp<Weapons.CompRukayaActiveMatterWeapon>()?.NotifySettingsChanged();
                    }
                }
            }
        }

        private static void DrawFloatSetting(
            Listing_Standard listing,
            string label,
            string description,
            ref float value,
            ref string buffer,
            float defaultValue,
            float minValue,
            float maxValue)
        {
            listing.Label(description);

            float previousValue = value;
            Rect row = listing.GetRect(32f);
            Rect labelRect = new Rect(row.x, row.y + 4f, 170f, row.height);
            Rect resetRect = new Rect(row.xMax - 70f, row.y + 2f, 70f, 28f);
            Rect inputRect = new Rect(resetRect.xMin - 92f, row.y + 2f, 80f, 28f);
            Rect sliderRect = new Rect(labelRect.xMax + 12f, row.y, inputRect.xMin - labelRect.xMax - 24f, row.height);

            Widgets.Label(labelRect, label);

            float sliderValue = Widgets.HorizontalSlider(
                sliderRect,
                value,
                minValue,
                maxValue,
                false,
                FormatSpeed(value),
                FormatSpeed(minValue),
                FormatSpeed(maxValue),
                0.1f);

            if (!Mathf.Approximately(sliderValue, value))
            {
                value = sliderValue;
                buffer = FormatSpeed(sliderValue);
            }

            Widgets.TextFieldNumeric(
                inputRect,
                ref value,
                ref buffer,
                minValue,
                maxValue);

            if (Widgets.ButtonText(resetRect, "Rukaya_ResetSetting".Translate()))
            {
                value = defaultValue;
                buffer = FormatSpeed(defaultValue);
            }

            value = Mathf.Clamp(value, minValue, maxValue);
            if (!Mathf.Approximately(previousValue, value))
            {
                ApplySettingsToDefs();
            }
        }

        private static string FormatSpeed(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
