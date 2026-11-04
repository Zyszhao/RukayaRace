using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaPlasmaProjectileExtension : DefModExtension
    {
        public float glowSize = 1.15f;
        public float impactGlowSize = 1.0f;
        public Color glowColor = new Color(0.35f, 0.95f, 1f, 0.78f);
        public int glowIntervalTicks = 3;
        public float directDamage;
        public float directArmorPenetration;
        public DamageDef directDamageDef;
    }
}
