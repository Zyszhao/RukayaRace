using Verse;

namespace RukayaRace.AirDefense
{
    public class RukayaSkyCrashSettings : IExposable
    {
        public RukayaSkyCrashSettings()
        {
        }

        public RukayaSkyCrashSettings(CompProperties_RukayaSkyInterceptor props)
        {
            offsetRadius = props.crashOffsetRadius;
            durationTicks = props.crashDurationTicks;
            pawnDamageCount = props.pawnDamageCount;
            pawnDamageAmount = props.pawnDamageAmount;
            cargoLossFraction = props.cargoLossFraction;
            meteoriteFragmentCount = props.meteoriteFragmentCount;
            meteoriteScatterRadius = props.meteoriteScatterRadius;
            meteoriteOrePerFragment = props.meteoriteOrePerFragment;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref offsetRadius, "offsetRadius", 4f);
            Scribe_Values.Look(ref durationTicks, "durationTicks", 30);
            Scribe_Values.Look(ref pawnDamageCount, "pawnDamageCount", 5);
            Scribe_Values.Look(ref pawnDamageAmount, "pawnDamageAmount", 20f);
            Scribe_Values.Look(ref cargoLossFraction, "cargoLossFraction", 0.8f);
            Scribe_Values.Look(ref meteoriteFragmentCount, "meteoriteFragmentCount", new IntRange(3, 5));
            Scribe_Values.Look(ref meteoriteScatterRadius, "meteoriteScatterRadius", 8f);
            Scribe_Values.Look(ref meteoriteOrePerFragment, "meteoriteOrePerFragment", new IntRange(1, 2));
        }

        public static int SurvivingCount(int count, float lossFraction, float roundingSample)
        {
            double expected = count * (1.0 - lossFraction);
            int whole = (int)System.Math.Floor(expected);
            return System.Math.Max(0, System.Math.Min(count, whole + (roundingSample < expected - whole ? 1 : 0)));
        }

        public float offsetRadius = 4f;
        public int durationTicks = 30;
        public int pawnDamageCount = 5;
        public float pawnDamageAmount = 20f;
        public float cargoLossFraction = 0.8f;
        public IntRange meteoriteFragmentCount = new IntRange(3, 5);
        public float meteoriteScatterRadius = 8f;
        public IntRange meteoriteOrePerFragment = new IntRange(1, 2);
    }
}
