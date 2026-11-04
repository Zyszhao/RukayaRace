using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompRukayaActiveMatterWeapon : ThingComp
    {
        private int currentUnits;
        private int targetUnits;
        private bool activeMode;
        private bool autoReloadEnabled = true;
        private bool aiSupplyInitialized;
        private string aiLastDecision;
        private List<Tool> lightningActiveTools;
        private HashSet<Tool> lightningStrikeTools;

        public CompProperties_RukayaActiveMatterWeapon Props => (CompProperties_RukayaActiveMatterWeapon)this.props;

        public Pawn Holder => RukayaActiveMatterUtility.GetEquipmentHolder(this.parent as ThingWithComps);

        public int CurrentUnits => this.currentUnits;

        public int TargetUnits => this.targetUnits;

        public bool ActiveMode => this.activeMode;

        public bool AutoReloadEnabled => this.autoReloadEnabled;

        public int MaxCapacity => Mathf.Max(0, this.Props.maxCapacity);

        public int UnitsPerContainer => Mathf.Max(1, this.Props.unitsPerContainer);

        public int ActiveCostPerUse => Mathf.Max(0, this.Props.activeCostPerUse);

        public float CurrentLoadPercent => this.MaxCapacity <= 0 ? 0f : (float)this.currentUnits / this.MaxCapacity;

        public float TargetLoadPercent
        {
            get => this.MaxCapacity <= 0 ? 0f : (float)this.targetUnits / this.MaxCapacity;
            set => this.targetUnits = Mathf.Clamp(Mathf.RoundToInt(value * this.MaxCapacity), 0, this.MaxCapacity);
        }

        public ThingDef NormalProjectile => this.Props.normalProjectile;

        public ThingDef ActiveProjectile => this.Props.activeProjectile;

        public bool HasRangedProjectile => this.NormalProjectile != null;

        public bool HasActiveProjectile => this.ActiveProjectile != null;

        public bool HasActiveToolSet => this.Props.activeTools != null && this.Props.activeTools.Count > 0;

        public bool SupportsActiveMode => this.HasRangedProjectile || this.HasActiveToolSet;

        public int AutoReloadTargetUnits => Mathf.Min(this.targetUnits, this.MaxCapacity);

        public override void PostPostMake()
        {
            base.PostPostMake();
            this.InitializeStateIfNeeded();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref this.currentUnits, "currentUnits", 0);
            Scribe_Values.Look(ref this.targetUnits, "targetUnits", 0);
            Scribe_Values.Look(ref this.activeMode, "activeMode", false);
            Scribe_Values.Look(ref this.autoReloadEnabled, "autoReloadEnabled", true);
            Scribe_Values.Look(ref this.aiSupplyInitialized, "aiSupplyInitialized", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                this.InitializeStateIfNeeded();
                this.currentUnits = Mathf.Clamp(this.currentUnits, 0, this.MaxCapacity);
                this.targetUnits = Mathf.Clamp(this.targetUnits, 0, this.MaxCapacity);
                if (this.activeMode && !this.CanUseActiveModeNow())
                {
                    this.activeMode = false;
                }
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            this.InitializeStateIfNeeded();
            this.InitializeAISupplyIfNeeded(pawn);
            if (this.activeMode && !this.CanUseActiveModeNow())
            {
                this.activeMode = false;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            this.InitializeAISupplyIfNeeded(this.Holder);
            if (this.activeMode && !this.CanUseActiveModeNow())
            {
                this.TryDisableActiveModeDueToInsufficient(null, false);
            }
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Rukaya_ActiveMatterInspect".Translate(this.currentUnits, this.MaxCapacity));
            builder.AppendLine();
            builder.Append("Rukaya_ActiveMatterTargetInspect".Translate(this.targetUnits));
            builder.AppendLine();
            builder.Append((this.activeMode ? "Rukaya_ActiveMatterModeOnShort" : "Rukaya_ActiveMatterModeOffShort").Translate());
            if (this.Props.allowAutoReload)
            {
                builder.AppendLine();
                builder.Append((this.autoReloadEnabled
                    ? "Rukaya_ActiveMatterAutoReloadOnShort"
                    : "Rukaya_ActiveMatterAutoReloadOffShort").Translate());
            }

            if (Prefs.DevMode && this.Holder != null && !this.Holder.IsColonistPlayerControlled
                && !this.aiLastDecision.NullOrEmpty())
            {
                builder.AppendLine();
                builder.Append("Rukaya_ActiveMatterAIDecision".Translate(this.aiLastDecision.Translate()));
            }

            return builder.ToString();
        }

        public override void Notify_UsedVerb(Pawn pawn, Verb verb)
        {
            base.Notify_UsedVerb(pawn, verb);
            if (!this.activeMode || verb == null || !verb.verbProps.IsMeleeAttack || verb.EquipmentSource != this.parent)
            {
                return;
            }

            if (this.ShouldTriggerLightningForMelee(verb))
            {
                LocalTargetInfo target = verb.CurrentTarget;
                Thing targetThing = target.Thing;
                Map map = targetThing?.Map ?? pawn?.Map;
                IntVec3 cell = targetThing?.Position ?? target.Cell;
                RukayaLightningUtility.Strike(cell, map);
            }

            this.ConsumeActiveUse(pawn);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (!this.Props.explodeWhenDestroyedWhileLoaded || this.currentUnits <= 0)
            {
                return;
            }

            Map map = previousMap ?? this.parent.MapHeld ?? this.Holder?.MapHeld;
            if (map == null)
            {
                return;
            }

            IntVec3 cell = this.parent.PositionHeld;
            if (!cell.IsValid && this.Holder != null)
            {
                cell = this.Holder.PositionHeld;
            }

            if (!cell.IsValid)
            {
                return;
            }

            int damage = Mathf.RoundToInt(this.Props.explosionBaseDamage + this.Props.explosionDamagePerUnit * this.currentUnits);
            if (this.Props.explosionMaxDamage > 0)
            {
                damage = Mathf.Min(damage, this.Props.explosionMaxDamage);
            }

            float radius = this.Props.explosionBaseRadius + this.Props.explosionRadiusPerUnit * this.currentUnits;
            if (this.Props.explosionMaxRadius > 0f)
            {
                radius = Mathf.Min(radius, this.Props.explosionMaxRadius);
            }

            GenExplosion.DoExplosion(
                cell,
                map,
                radius,
                this.Props.explosionDamageDef ?? DamageDefOf.Bomb,
                this.Holder ?? this.parent,
                damage);
        }

        public List<Tool> GetNormalTools()
        {
            if (this.Props.normalTools != null && this.Props.normalTools.Count > 0)
            {
                return this.Props.normalTools;
            }

            return this.parent.def.tools;
        }

        public List<Tool> GetCurrentTools()
        {
            if (this.activeMode && this.HasActiveToolSet)
            {
                if (this.ShouldUseLightningActiveTools())
                {
                    return this.GetLightningActiveTools();
                }

                return this.Props.activeTools;
            }

            return this.GetNormalTools();
        }

        public void ToggleActiveMode(bool notifyUser = true)
        {
            this.SetActiveMode(!this.activeMode, notifyUser);
        }

        public void SetActiveMode(bool enabled, bool notifyUser = true)
        {
            if (!enabled)
            {
                if (!this.activeMode)
                {
                    return;
                }

                this.activeMode = false;
                this.NotifyModeChanged();
                if (notifyUser)
                {
                    RukayaActiveMatterUtility.ShowModeSwitchMessage(this.Holder, false);
                }

                return;
            }

            if (!this.SupportsActiveMode || !this.CanUseActiveModeNow())
            {
                this.TryDisableActiveModeDueToInsufficient(this.Holder, notifyUser);
                return;
            }

            if (this.activeMode)
            {
                return;
            }

            this.activeMode = true;
            this.NotifyModeChanged();
            if (notifyUser)
            {
                RukayaActiveMatterUtility.ShowModeSwitchMessage(this.Holder, true);
            }
        }

        public void ToggleAutoReload(bool notifyUser = true)
        {
            this.autoReloadEnabled = !this.autoReloadEnabled;
            if (notifyUser)
            {
                RukayaActiveMatterUtility.ShowAutoReloadMessage(this.Holder, this.autoReloadEnabled);
            }
        }

        public bool CanUseActiveModeNow()
        {
            return this.SupportsActiveMode && this.currentUnits >= this.ActiveCostPerUse;
        }

        public void ConsumeActiveUse(Pawn pawn)
        {
            if (!this.activeMode)
            {
                return;
            }

            if (this.currentUnits < this.ActiveCostPerUse)
            {
                this.TryDisableActiveModeDueToInsufficient(pawn, true);
                return;
            }

            this.currentUnits = Mathf.Clamp(this.currentUnits - this.ActiveCostPerUse, 0, this.MaxCapacity);
            if (this.Props.revertToNormalWhenInsufficient && this.currentUnits < this.ActiveCostPerUse)
            {
                this.TryDisableActiveModeDueToInsufficient(pawn, this.Props.showInsufficientMessage);
            }
        }

        public void NotifyRangedShotFired(Pawn pawn)
        {
            this.ConsumeActiveUse(pawn);
        }

        public void NotifySettingsChanged()
        {
            this.lightningActiveTools = null;
            this.lightningStrikeTools = null;
            this.NotifyModeChanged();
        }

        public void SetAILastDecision(string decision)
        {
            this.aiLastDecision = decision;
        }

        public void SetAIMode(bool enabled, string decision)
        {
            this.SetActiveMode(enabled, false);
            this.aiLastDecision = decision;
        }

        public int ReloadFromInventory(Pawn pawn)
        {
            if (pawn?.inventory?.innerContainer == null || this.currentUnits >= this.MaxCapacity)
            {
                return 0;
            }

            ThingDef containerDef = this.Props.activeMatterThingDef;
            if (containerDef == null)
            {
                return 0;
            }

            Thing source = null;
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (item != null && item.def == containerDef && item.stackCount > 0)
                {
                    source = item;
                    break;
                }
            }

            if (source == null)
            {
                return 0;
            }

            int usedContainers = this.GetAutoReloadContainerCount(source.stackCount);
            if (this.currentUnits >= this.UnitsPerContainer || usedContainers <= 0)
            {
                return 0;
            }

            int loadedContainers = this.ReloadFromContainers(usedContainers, false);
            if (loadedContainers <= 0)
            {
                return 0;
            }

            Thing spent = source.SplitOff(loadedContainers);
            spent.Destroy(DestroyMode.Vanish);
            return loadedContainers;
        }

        public void InitializeAISupplyIfNeeded(Pawn pawn)
        {
            if (this.aiSupplyInitialized || !this.HasRangedProjectile
                || this.Props.aiModePolicy == RukayaActiveMatterAIPolicy.Never
                || !RukayaActiveMatterAIUtility.IsAutonomousPawn(pawn))
            {
                return;
            }

            this.aiSupplyInitialized = true;
            this.targetUnits = this.MaxCapacity;

            int initialUnits = Mathf.Min(
                this.MaxCapacity,
                Mathf.Max(0, this.Props.aiInitialLoadContainers) * this.UnitsPerContainer);
            if (this.currentUnits <= 0 && initialUnits > 0)
            {
                this.currentUnits = initialUnits;
            }

            ThingDef containerDef = this.Props.activeMatterThingDef;
            ThingOwner inventory = pawn.inventory?.innerContainer;
            int carryCount = Mathf.Max(0, this.Props.aiCarryContainers);
            if (containerDef == null || inventory == null || carryCount <= 0)
            {
                return;
            }

            Thing carried = ThingMaker.MakeThing(containerDef);
            carried.stackCount = carryCount;
            if (inventory.TryAdd(carried))
            {
                // ThingOwner merges the stack and may destroy the temporary Thing.
            }
            else if (!carried.Destroyed)
            {
                carried.Destroy(DestroyMode.Vanish);
            }
        }

        public ThingDef GetCurrentProjectile()
        {
            if (this.activeMode && this.currentUnits >= this.ActiveCostPerUse)
            {
                return this.ActiveProjectile ?? this.NormalProjectile;
            }

            return this.NormalProjectile;
        }

        public int GetManualReloadContainerCount(int availableCount)
        {
            if (availableCount <= 0 || this.currentUnits >= this.MaxCapacity)
            {
                return 0;
            }

            int missingUnits = this.MaxCapacity - this.currentUnits;
            int neededContainers = Mathf.CeilToInt((float)missingUnits / this.UnitsPerContainer);
            return Mathf.Min(availableCount, Mathf.Max(neededContainers, 1));
        }

        public int GetAutoReloadContainerCount(int availableCount)
        {
            if (!this.Props.allowAutoReload || !this.autoReloadEnabled || availableCount <= 0)
            {
                return 0;
            }

            int desiredUnits = this.AutoReloadTargetUnits - this.currentUnits;
            if (desiredUnits < this.UnitsPerContainer || this.currentUnits + this.UnitsPerContainer > this.AutoReloadTargetUnits)
            {
                return 0;
            }

            int maxContainers = desiredUnits / this.UnitsPerContainer;
            return Mathf.Min(availableCount, maxContainers);
        }

        public int ReloadFromContainers(int requestedContainers, bool manual)
        {
            requestedContainers = Mathf.Max(0, requestedContainers);
            if (requestedContainers <= 0 || this.MaxCapacity <= 0)
            {
                return 0;
            }

            if (manual)
            {
                if (this.currentUnits >= this.MaxCapacity)
                {
                    return 0;
                }

                int addedUnits = requestedContainers * this.UnitsPerContainer;
                this.currentUnits = Mathf.Clamp(this.currentUnits + addedUnits, 0, this.MaxCapacity);
                return requestedContainers;
            }

            int autoContainers = this.GetAutoReloadContainerCount(requestedContainers);
            if (autoContainers <= 0)
            {
                return 0;
            }

            this.currentUnits = Mathf.Clamp(this.currentUnits + autoContainers * this.UnitsPerContainer, 0, this.MaxCapacity);
            return autoContainers;
        }

        public void DebugAdjustUnits(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            this.currentUnits = Mathf.Clamp(this.currentUnits + delta, 0, this.MaxCapacity);
            if (this.activeMode && !this.CanUseActiveModeNow())
            {
                this.TryDisableActiveModeDueToInsufficient(this.Holder, false);
            }
        }

        public void TryDisableActiveModeDueToInsufficient(Pawn pawn, bool showMessage)
        {
            bool wasActive = this.activeMode;
            this.activeMode = false;
            if (wasActive)
            {
                this.NotifyModeChanged();
            }

            if (showMessage && this.Props.showInsufficientMessage)
            {
                RukayaActiveMatterUtility.ShowInsufficientMessage(pawn ?? this.Holder, this.parent);
            }
        }

        private void InitializeStateIfNeeded()
        {
            if (this.targetUnits <= 0)
            {
                this.targetUnits = Mathf.Clamp(this.Props.defaultLoad, 0, this.MaxCapacity);
            }

            this.targetUnits = Mathf.Clamp(this.targetUnits, 0, this.MaxCapacity);
            this.currentUnits = Mathf.Clamp(this.currentUnits, 0, this.MaxCapacity);
            this.autoReloadEnabled = this.Props.allowAutoReload && this.autoReloadEnabled;
            if (!this.Props.allowAutoReload)
            {
                this.autoReloadEnabled = false;
            }
        }

        private void NotifyModeChanged()
        {
            CompRukayaActiveMatterEquippable equippable = this.parent.GetComp<CompRukayaActiveMatterEquippable>();
            equippable?.NotifyModeChanged();
        }

        private bool ShouldUseLightningActiveTools()
        {
            return RukayaLightningUtility.ShouldUseLightningForElectricWeapons
                && this.parent?.def?.defName == "Rukaya_StunBaton";
        }

        private bool ShouldTriggerLightningForMelee(Verb verb)
        {
            return this.ShouldUseLightningActiveTools()
                && verb?.tool != null
                && this.lightningStrikeTools != null
                && this.lightningStrikeTools.Contains(verb.tool);
        }

        private List<Tool> GetLightningActiveTools()
        {
            if (this.lightningActiveTools != null)
            {
                return this.lightningActiveTools;
            }

            this.lightningActiveTools = new List<Tool>();
            this.lightningStrikeTools = new HashSet<Tool>();
            foreach (Tool tool in this.Props.activeTools)
            {
                Tool clone = CloneToolWithoutElectricExtraDamage(tool);
                this.lightningActiveTools.Add(clone);
                if (ToolHasElectricExtraDamage(tool))
                {
                    this.lightningStrikeTools.Add(clone);
                }
            }

            return this.lightningActiveTools;
        }

        private static Tool CloneToolWithoutElectricExtraDamage(Tool source)
        {
            Tool clone = new Tool
            {
                id = source.id,
                label = source.label,
                labelNoLocation = source.labelNoLocation,
                labelUsedInLogging = source.labelUsedInLogging,
                capacities = new List<ToolCapacityDef>(source.capacities),
                power = source.power,
                armorPenetration = source.armorPenetration,
                cooldownTime = source.cooldownTime,
                surpriseAttack = source.surpriseAttack,
                hediff = source.hediff,
                chanceFactor = source.chanceFactor,
                alwaysTreatAsWeapon = source.alwaysTreatAsWeapon,
                soundMeleeHit = source.soundMeleeHit,
                soundMeleeMiss = source.soundMeleeMiss,
                linkedBodyPartsGroup = source.linkedBodyPartsGroup,
                ensureLinkedBodyPartsGroupAlwaysUsable = source.ensureLinkedBodyPartsGroupAlwaysUsable
            };

            if (source.extraMeleeDamages != null)
            {
                clone.extraMeleeDamages = source.extraMeleeDamages
                    .FindAll(extraDamage => !IsElectricExtraDamage(extraDamage.def));
            }

            return clone;
        }

        private static bool ToolHasElectricExtraDamage(Tool tool)
        {
            if (tool?.extraMeleeDamages == null)
            {
                return false;
            }

            foreach (ExtraDamage extraDamage in tool.extraMeleeDamages)
            {
                if (IsElectricExtraDamage(extraDamage.def))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsElectricExtraDamage(DamageDef damageDef)
        {
            return damageDef == DamageDefOf.EMP || damageDef == DamageDefOf.Stun;
        }
    }
}
