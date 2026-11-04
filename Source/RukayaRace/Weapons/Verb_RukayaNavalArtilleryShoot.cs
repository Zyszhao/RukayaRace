using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    /// <summary>
    /// Twin-barrel naval artillery verb: one shot puts <c>shellsPerShot</c> shells in the air on the same tick.
    /// Every <c>base.TryCastShot()</c> call is a complete vanilla shot (its own accuracy roll and its own
    /// projectile), so the pair disperses independently while staying inside a single burst shot.
    /// Ammo, sound and muzzle flash are still settled once per shot by <c>Verb.TryCastNextBurstShot</c>,
    /// which is what keeps a double salvo cheap: one burst tick, one report, two shells.
    /// </summary>
    public class Verb_RukayaNavalArtilleryShoot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            int count = Mathf.Max(1, this.ShellsPerShot);
            bool anyFired = false;
            for (int i = 0; i < count; i++)
            {
                if (!base.TryCastShot())
                {
                    break;
                }
                anyFired = true;
            }
            return anyFired;
        }

        private int ShellsPerShot
        {
            get
            {
                RukayaNavalArtilleryExtension extension =
                    this.caster?.def?.GetModExtension<RukayaNavalArtilleryExtension>()
                    ?? this.EquipmentSource?.def?.GetModExtension<RukayaNavalArtilleryExtension>();
                return extension?.shellsPerShot ?? RukayaNavalArtilleryExtension.Default.shellsPerShot;
            }
        }
    }
}
