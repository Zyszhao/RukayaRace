using Verse;

namespace RukayaRace
{
    public class RukayaRaceSettings : ModSettings
    {
        public const float MinSettingValue = 0.1f;
        public const float MaxSettingValue = 10f;
        public const float MaxDailyOutputValue = 50f;
        public const float DefaultFabricatorWorkSpeed = 1f;
        public const float DefaultRefinerDailyOutput = 2f;
        public const float DefaultAdvancedRefinerDailyOutput = 10f;
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
            Scribe_Values.Look(ref refinerDailyOutput, "refinerDailyOutput", DefaultRefinerDailyOutput);
            Scribe_Values.Look(ref advancedRefinerDailyOutput, "advancedRefinerDailyOutput", DefaultAdvancedRefinerDailyOutput);
            Scribe_Values.Look(ref replicationReservoirCapacity, "replicationReservoirCapacity", DefaultReplicationReservoirCapacity);
            Scribe_Values.Look(ref useLightningForElectricWeapons, "useLightningForElectricWeapons", false);
            Scribe_Values.Look(ref trioInitialChance, "trioInitialChance", 0.12f);
            Scribe_Values.Look(ref trioDecay, "trioDecay", 0.85f);
            Scribe_Values.Look(ref trioMinimumChance, "trioMinimumChance", 0.02f);
            Scribe_Values.Look(ref trioDeathChance, "trioDeathChance", 0.35f);
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
