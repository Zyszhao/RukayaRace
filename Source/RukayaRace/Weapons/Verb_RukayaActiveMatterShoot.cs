using Verse;

namespace RukayaRace.Weapons
{
    public class Verb_RukayaActiveMatterShoot : Verb_Shoot
    {
        public override ThingDef Projectile
        {
            get
            {
                CompRukayaActiveMatterWeapon weaponComp = base.EquipmentSource?.GetComp<CompRukayaActiveMatterWeapon>();
                ThingDef projectile = weaponComp?.GetCurrentProjectile();
                return projectile ?? base.Projectile;
            }
        }

        protected override bool TryCastShot()
        {
            CompRukayaActiveMatterWeapon weaponComp = base.EquipmentSource?.GetComp<CompRukayaActiveMatterWeapon>();
            bool usingActiveMode = weaponComp != null && weaponComp.ActiveMode;
            if (usingActiveMode && !weaponComp.CanUseActiveModeNow())
            {
                weaponComp.TryDisableActiveModeDueToInsufficient(this.CasterPawn, true);
                usingActiveMode = false;
            }

            bool result = base.TryCastShot();
            if (result && usingActiveMode)
            {
                weaponComp.NotifyRangedShotFired(this.CasterPawn);
            }

            return result;
        }
    }
}
