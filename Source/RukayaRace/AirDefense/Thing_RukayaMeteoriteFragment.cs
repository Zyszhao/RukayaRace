using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Thing_RukayaMeteoriteFragment : Skyfaller
    {
        public override Vector3 DrawPos
        {
            get
            {
                if (!flightInitialized || flightDurationTicks <= 0)
                    return base.DrawPos;
                float progress = Mathf.Clamp01(1f - (float)ticksToImpact / flightDurationTicks);
                Vector3 destination = Position.ToVector3Shifted();
                return (Vector3.Lerp(flightOrigin, destination, progress)
                    + scatterOffset * Mathf.Sin(Mathf.PI * progress))
                    .WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            }
        }

        public void InitializeFlight(Vector3 origin, IntVec3 originalLanding)
        {
            flightOrigin = origin.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            flightDurationTicks = ticksToImpact;
            scatterOffset = (Position - originalLanding).ToVector3().normalized * 3f;
            flightInitialized = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref flightOrigin, "flightOrigin");
            Scribe_Values.Look(ref scatterOffset, "scatterOffset");
            Scribe_Values.Look(ref flightDurationTicks, "flightDurationTicks");
            Scribe_Values.Look(ref flightInitialized, "flightInitialized");
        }

        private Vector3 flightOrigin;
        private Vector3 scatterOffset;
        private int flightDurationTicks;
        private bool flightInitialized;
    }
}
