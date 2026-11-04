using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RukayaRace.Weapons
{
    public class CompRukayaActiveMatterEquippable : CompEquippable, IVerbOwner
    {
        List<Tool> IVerbOwner.Tools => this.GetCurrentToolsForVerbs();

        public new List<Tool> Tools => this.GetCurrentToolsForVerbs();

        private List<Tool> GetCurrentToolsForVerbs()
        {
            CompRukayaActiveMatterWeapon weaponComp = this.parent.GetComp<CompRukayaActiveMatterWeapon>();
            return weaponComp?.GetCurrentTools() ?? base.Tools;
        }

        public override IEnumerable<Gizmo> CompGetEquippedGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetEquippedGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn holder = this.Holder;
            CompRukayaActiveMatterWeapon weaponComp = this.parent.GetComp<CompRukayaActiveMatterWeapon>();
            if (holder == null || weaponComp == null || !holder.IsColonistPlayerControlled || !weaponComp.Props.allowPlayerGizmo)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = (weaponComp.ActiveMode
                    ? "Rukaya_ActiveMatterModeCommandOn"
                    : "Rukaya_ActiveMatterModeCommandOff").Translate(),
                defaultDesc = "Rukaya_ActiveMatterModeCommandDesc".Translate(
                    weaponComp.ActiveCostPerUse,
                    weaponComp.CurrentUnits,
                    weaponComp.MaxCapacity),
                icon = weaponComp.ActiveMode ? RukayaActiveMatterUtility.ModeOnIcon : RukayaActiveMatterUtility.ModeOffIcon,
                action = delegate
                {
                    weaponComp.ToggleActiveMode();
                    if (weaponComp.ActiveMode)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    else
                    {
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }
                }
            };

            if (weaponComp.Props.allowAutoReload)
            {
                yield return new Command_Action
                {
                    defaultLabel = (weaponComp.AutoReloadEnabled
                        ? "Rukaya_ActiveMatterAutoReloadCommandOn"
                        : "Rukaya_ActiveMatterAutoReloadCommandOff").Translate(),
                    defaultDesc = "Rukaya_ActiveMatterAutoReloadCommandDesc".Translate(),
                    icon = weaponComp.AutoReloadEnabled
                        ? RukayaActiveMatterUtility.AutoReloadOnIcon
                        : RukayaActiveMatterUtility.AutoReloadOffIcon,
                    action = delegate
                    {
                        weaponComp.ToggleAutoReload();
                        if (weaponComp.AutoReloadEnabled)
                        {
                            SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        }
                        else
                        {
                            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        }
                    }
                };
            }

            yield return new Gizmo_RukayaActiveMatterLoad(weaponComp);

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Rukaya_ActiveMatterDebugAdd".Translate(weaponComp.UnitsPerContainer),
                    defaultDesc = "Rukaya_ActiveMatterDebugAddDesc".Translate(weaponComp.UnitsPerContainer),
                    action = delegate
                    {
                        weaponComp.DebugAdjustUnits(weaponComp.UnitsPerContainer);
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Rukaya_ActiveMatterDebugSubtract".Translate(weaponComp.UnitsPerContainer),
                    defaultDesc = "Rukaya_ActiveMatterDebugSubtractDesc".Translate(weaponComp.UnitsPerContainer),
                    action = delegate
                    {
                        weaponComp.DebugAdjustUnits(-weaponComp.UnitsPerContainer);
                    }
                };
            }
        }

        public void NotifyModeChanged()
        {
            this.verbTracker.VerbsNeedReinitOnLoad();
            Pawn holder = this.Holder;
            if (holder != null)
            {
                List<Verb> verbs = this.verbTracker.AllVerbs;
                for (int i = 0; i < verbs.Count; i++)
                {
                    verbs[i].caster = holder;
                }

                holder.meleeVerbs?.Notify_PawnDespawned();
            }
        }
    }
}
