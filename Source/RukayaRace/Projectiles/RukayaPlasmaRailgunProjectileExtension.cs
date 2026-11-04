using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class RukayaPlasmaRailgunProjectileExtension : RukayaPlasmaProjectileExtension
    {
        public float trailExplosionRadius = 1.5f;
        public int trailExplosionDamage = 12;
        public float trailWidth = 3f;
        public int backwardLength = 5;
        public int forwardLength = 10;
        public DamageDef trailDamageDef;
        public SoundDef trailExplosionSound;
        public int mainExplosionDamageBonus;
    }
}
