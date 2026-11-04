using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Weapons
{
    public enum RukayaActiveMatterAIPolicy
    {
        Never,
        AnyHostileTarget,
        CloseOrArmored,
        ArmoredOrGrouped,
        GroupedTargets
    }

    public class CompProperties_RukayaActiveMatterWeapon : CompProperties
    {
        public ThingDef activeMatterThingDef;
        public ThingDef normalProjectile;
        public ThingDef activeProjectile;
        public List<Tool> normalTools;
        public List<Tool> activeTools;
        public int unitsPerContainer = 30;
        public int maxCapacity = 30;
        public int defaultLoad = 30;
        public int activeCostPerUse = 3;
        // Autonomous pawns receive one internal container and one spare by default.
        // 玩家单位仍由玩家手动装填和切换；这些字段只由 AI 管理器读取。
        public int aiInitialLoadContainers = 1;
        public int aiCarryContainers = 1;
        public RukayaActiveMatterAIPolicy aiModePolicy = RukayaActiveMatterAIPolicy.Never;
        public float aiFriendlyFireRadius;
        public float aiCloseTargetRange = 10f;
        public float aiArmorThreshold = 0.25f;
        public float aiGroupRadius = 4f;
        public int aiMinimumGroupTargets = 2;
        // Chance (0..1) that a landed shot from this weapon is forced to resolve on a head-region part. 0 disables.
        // 命中判定强制打头概率（0..1）；0 表示关闭。
        public float headshotChance = 0f;
        // Extra multiplier applied to the equipment accuracy factor while active matter mode is on (and charge available). 1 disables.
        // 活性物质模式开启时的武器精度增益系数；1 表示关闭。
        public float activeAccuracyFactor = 1f;
        // Warmup multiplier while active matter mode is on (and charge available). Values below 1 speed the firing cycle up.
        // 活性物质模式开启时的开火前摇系数；小于 1 使射击循环更快。
        public float activeWarmupMultiplier = 1f;
        // Cooldown multiplier while active matter mode is on (and charge available). Values below 1 shorten the time between cycles.
        // 活性物质模式开启时的冷却系数；小于 1 缩短射击间隔。
        public float activeCooldownMultiplier = 1f;
        // Effective range multiplier while active matter mode is on (and charge available).
        // 活性物质模式开启时的有效射程系数。
        public float activeRangeMultiplier = 1f;
        public bool allowPlayerGizmo = true;
        public bool allowAutoReload = true;
        public bool autoReloadPlayerOnly = true;
        public bool autoReloadRequireUndrafted = true;
        public bool autoReloadRequireSafe = true;
        public bool autoReloadFromStorageOnly = true;
        public bool revertToNormalWhenInsufficient = true;
        public bool showInsufficientMessage = true;
        public bool explodeWhenDestroyedWhileLoaded = true;
        public DamageDef explosionDamageDef;
        public int explosionBaseDamage = 10;
        public float explosionDamagePerUnit = 1f;
        public int explosionMaxDamage = 60;
        public float explosionBaseRadius = 0.9f;
        public float explosionRadiusPerUnit = 0.03f;
        public float explosionMaxRadius = 2.8f;

        public CompProperties_RukayaActiveMatterWeapon()
        {
            this.compClass = typeof(CompRukayaActiveMatterWeapon);
        }
    }
}
