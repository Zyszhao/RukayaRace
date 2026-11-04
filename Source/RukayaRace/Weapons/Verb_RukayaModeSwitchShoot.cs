using Verse;

namespace RukayaRace.Weapons
{
    public class Verb_RukayaModeSwitchShoot : Verb_RukayaActiveMatterShoot
    {
        protected override int ShotsPerBurst
        {
            get
            {
                CompRukayaActiveMatterWeapon weaponComp = base.EquipmentSource?.GetComp<CompRukayaActiveMatterWeapon>();
                if (weaponComp != null && weaponComp.ActiveMode && weaponComp.CanUseActiveModeNow())
                {
                    return 1;
                }

                return base.ShotsPerBurst;
            }
        }
    }
}
