using Verse;

namespace RukayaRace.Weapons
{
    /// <summary>
    /// Active-matter overdrive verb used by the Rukaya overdrive sidearms
    /// (Rukaya_SmartRevolver, Rukaya_Pistol, Rukaya_SubMachinegun).
    /// While active matter mode is on with charge available it tightens the firing cycle
    /// (warmup &amp; cooldown multipliers) and extends the effective range, on top of the
    /// overcharged projectile chosen by Verb_RukayaActiveMatterShoot.
    /// </summary>
    public class Verb_RukayaOverchargeShoot : Verb_RukayaActiveMatterShoot
    {
        private CompRukayaActiveMatterWeapon WeaponComp => base.EquipmentSource?.GetComp<CompRukayaActiveMatterWeapon>();

        private bool OverchargeActive => WeaponComp != null && WeaponComp.ActiveMode && WeaponComp.CanUseActiveModeNow();

        public override float WarmupTime
        {
            get
            {
                float warmup = base.WarmupTime;
                if (OverchargeActive)
                {
                    warmup *= WeaponComp.Props.activeWarmupMultiplier;
                }

                return warmup;
            }
        }

        public override float EffectiveRange
        {
            get
            {
                float range = base.EffectiveRange;
                if (OverchargeActive)
                {
                    range *= WeaponComp.Props.activeRangeMultiplier;
                }

                return range;
            }
        }
    }
}
