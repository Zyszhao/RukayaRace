using System;

namespace RukayaRace.Storytelling
{
    // Kept independent of game APIs so annual limits and probability progression
    // can be exercised directly without creating a running RimWorld world.
    public sealed class AnnualTrioSchedule
    {
        public int Year = -1;
        public int Attempts;
        public bool Occurred;

        public void RefreshYear(int year)
        {
            if (Year == year) return;
            Year = year;
            Attempts = 0;
            Occurred = false;
        }

        public bool TryBeginAttempt(int year, float initial, float decay, float floor, out float chance)
        {
            RefreshYear(year);
            chance = 0f;
            if (Occurred) return false;
            chance = (float)Math.Max(floor, initial * Math.Pow(decay, Attempts));
            if (Attempts < int.MaxValue) Attempts++;
            return true;
        }
    }
}
