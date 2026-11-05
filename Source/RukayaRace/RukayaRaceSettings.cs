using Verse;

namespace RukayaRace
{
    public class RukayaRaceSettings : ModSettings
    {
        public const float MinSettingValue = 0.1f;
        public const float MaxSettingValue = 10f;
        public const float MaxDailyOutputValue = 200f;
        public const float DefaultFabricatorWorkSpeed = 1f;
        public const float DefaultRefinerDailyOutput = 8f;
        public const float DefaultAdvancedRefinerDailyOutput = 40f;
        private const int CurrentRefinerOutputVersion = 2;
        private int refinerOutputVersion = CurrentRefinerOutputVersion;
        public const float DefaultReplicationReservoirCapacity = 1000f;
        public const float MinReplicationReservoirCapacity = 100f;
        public const float MaxReplicationReservoirCapacity = 1000f;

        public float fabricatorWorkSpeedFactor = DefaultFabricatorWorkSpeed;
        public float refinerDailyOutput = DefaultRefinerDailyOutput;
        public float advancedRefinerDailyOutput = DefaultAdvancedRefinerDailyOutput;
        public float replicationReservoirCapacity = DefaultReplicationReservoirCapacity;
        public bool useLightningForElectricWeapons;
        public float trioInitialChance = 0.12f;
        public float trioDecay = 0.85f;
        public float trioMinimumChance = 0.02f;
        public float trioDeathChance = 0.35f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fabricatorWorkSpeedFactor, "fabricatorWorkSpeedFactor", DefaultFabricatorWorkSpeed);
            Scribe_Values.Look(ref refinerOutputVersion, "refinerOutputVersion", 0);
            float defaultRefinerOutput = refinerOutputVersion == 0 ? 2f : (refinerOutputVersion == 1 ? 20f : DefaultRefinerDailyOutput);
            float defaultAdvancedRefinerOutput = refinerOutputVersion == 0 ? 10f : (refinerOutputVersion == 1 ? 100f : DefaultAdvancedRefinerDailyOutput);
            Scribe_Values.Look(ref refinerDailyOutput, "refinerDailyOutput", defaultRefinerOutput);
            Scribe_Values.Look(ref advancedRefinerDailyOutput, "advancedRefinerDailyOutput", defaultAdvancedRefinerOutput);
            Scribe_Values.Look(ref replicationReservoirCapacity, "replicationReservoirCapacity", DefaultReplicationReservoirCapacity);
            Scribe_Values.Look(ref useLightningForElectricWeapons, "useLightningForElectricWeapons", false);
            Scribe_Values.Look(ref trioInitialChance, "trioInitialChance", 0.12f);
            Scribe_Values.Look(ref trioDecay, "trioDecay", 0.85f);
            Scribe_Values.Look(ref trioMinimumChance, "trioMinimumChance", 0.02f);
            Scribe_Values.Look(ref trioDeathChance, "trioDeathChance", 0.35f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && refinerOutputVersion < CurrentRefinerOutputVersion)
            {
                float migrationFactor = refinerOutputVersion == 0 ? 4f : 0.4f;
                refinerDailyOutput *= migrationFactor;
                advancedRefinerDailyOutput *= migrationFactor;
                refinerOutputVersion = CurrentRefinerOutputVersion;
            }
            ClampValues();
        }

        public void ClampValues()
        {
            trioInitialChance = ClampProbability(trioInitialChance, 0.12f);
            trioDecay = ClampProbability(trioDecay, 0.85f);
            trioMinimumChance = UnityEngine.Mathf.Min(trioInitialChance, ClampProbability(trioMinimumChance, 0.02f));
            trioDeathChance = ClampProbability(trioDeathChance, 0.35f);
            fabricatorWorkSpeedFactor = ClampSetting(fabricatorWorkSpeedFactor);
            refinerDailyOutput = ClampDailyOutput(refinerDailyOutput);
            advancedRefinerDailyOutput = ClampDailyOutput(advancedRefinerDailyOutput);
            replicationReservoirCapacity = UnityEngine.Mathf.Clamp(
                replicationReservoirCapacity,
                MinReplicationReservoirCapacity,
                MaxReplicationReservoirCapacity);
        }

        private static float ClampProbability(float value, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : UnityEngine.Mathf.Clamp01(value);

        public static float ClampSetting(float value)
        {
            if (value < MinSettingValue)
            {
                return MinSettingValue;
            }

            if (value > MaxSettingValue)
            {
                return MaxSettingValue;
            }

            return value;
        }

        public static float ClampDailyOutput(float value)
        {
            if (value < MinSettingValue)
            {
                return MinSettingValue;
            }

            if (value > MaxDailyOutputValue)
            {
                return MaxDailyOutputValue;
            }

            return value;
        }

        public static int GetReplicationReservoirCapacity(int defDefault)
        {
            if (RukayaRaceMod.Settings == null)
            {
                return defDefault > 0 ? defDefault : (int)DefaultReplicationReservoirCapacity;
            }

            return UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(
                RukayaRaceMod.Settings.replicationReservoirCapacity,
                MinReplicationReservoirCapacity,
                MaxReplicationReservoirCapacity));
        }
    }
}

// 更新日志 2：将活性物质提炼产量调整为原始产量的四倍，并迁移旧版一倍/十倍设置。
