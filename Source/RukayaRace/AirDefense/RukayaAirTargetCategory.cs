using System;

namespace RukayaRace.AirDefense
{
    [Flags]
    public enum RukayaAirTargetCategory
    {
        None = 0,
        DropPods = 1,
        NaturalSkyfallers = 2,
        OtherSkyfallers = 4,
        OverheadProjectiles = 8,
        ArcingProjectiles = 16,
        Flyers = 32
    }
}
