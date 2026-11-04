using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public class Verb_RukayaAviationMachinegunShoot : Verb_RukayaActiveMatterShoot
    {
        private bool jamCooldownPending;

        protected override bool TryCastShot()
        {
            CompRukayaActiveMatterWeapon weaponComp = EquipmentSource?.GetComp<CompRukayaActiveMatterWeapon>();
            bool usingActiveMatter = weaponComp != null && weaponComp.ActiveMode && weaponComp.CanUseActiveModeNow();
            if (!usingActiveMatter && Rand.Chance(JamExtension.jamChancePerShot))
            {
                burstShotsLeft = 0;
                jamCooldownPending = true;
                ThrowJamMote();
                return false;
            }

            return base.TryCastShot();
        }

        public void ApplyPendingJamCooldown()
        {
            if (!jamCooldownPending)
            {
                return;
            }

            jamCooldownPending = false;
            Pawn casterPawn = CasterPawn;
            if (casterPawn?.stances == null)
            {
                return;
            }

            int normalCooldownTicks = verbProps.AdjustedCooldownTicks(this, casterPawn);
            int jamCooldownTicks = UnityEngine.Mathf.Max(
                normalCooldownTicks,
                UnityEngine.Mathf.RoundToInt(normalCooldownTicks * JamExtension.jamCooldownMultiplier));
            if (casterPawn.stances.curStance is Stance_Cooldown cooldownStance && cooldownStance.verb == this)
            {
                cooldownStance.ticksLeft = UnityEngine.Mathf.Max(cooldownStance.ticksLeft, jamCooldownTicks);
            }
        }

        private RukayaAviationMachinegunJamExtension JamExtension =>
            EquipmentSource?.def.GetModExtension<RukayaAviationMachinegunJamExtension>()
            ?? new RukayaAviationMachinegunJamExtension();

        private void ThrowJamMote()
        {
            if (caster?.MapHeld != null)
            {
                MoteMaker.ThrowText(caster.DrawPos, caster.MapHeld, "Rukaya_AviationMachinegunJammed".Translate(), 2.5f);
            }
        }
    }
}
