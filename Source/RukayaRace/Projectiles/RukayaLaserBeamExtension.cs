using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    /// <summary>
    /// Per-shot tuning of the laser pulse visuals and ignite behavior.
    /// The beam is drawn from the projectile origin to its destination. A projectile def that
    /// also carries <see cref="RukayaBarrageSwayExtension"/> may additionally shift the drawn
    /// origin toward the muzzle and sway both endpoints (visual only; the hit still resolves at
    /// the real destination).
    /// </summary>
    public class RukayaLaserBeamExtension : DefModExtension
    {
        public float igniteChance = 0f;
        public float fireSize = 0.8f;

        public Color beamColor = new Color(1f, 0.45f, 0.24f, 1f);
        public float beamWidth = 0.16f;
        public float beamGlowWidth = 0.55f;
        public int beamDurationTicks = 10;
        public float beamFadeFraction = 0.4f;
        public float impactGlowSize = 1f;
    }
}
