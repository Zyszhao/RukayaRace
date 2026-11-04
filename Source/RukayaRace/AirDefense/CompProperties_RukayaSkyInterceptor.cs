using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class CompProperties_RukayaSkyInterceptor : CompProperties_TurretGun
    {
        public CompProperties_RukayaSkyInterceptor()
        {
            compClass = typeof(CompRukayaSkyInterceptor);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;
            if (parentDef.tickerType != TickerType.Normal)
                yield return "Rukaya sky interceptor requires tickerType Normal.";
            if (CarrierHost(parentDef) && turretDef != null && turretDef == parentDef.building?.turretGunDef)
                yield return "Rukaya sky interceptor on a Building_TurretGun host must use its own air-defense weapon: "
                    + "turretDef must NOT be the host's building.turretGunDef (sharing that gun would take over the "
                    + "host's verb and burst state, which the design forbids).";
            if (turretDef?.Verbs == null || turretDef.Verbs.Count == 0
                || !typeof(Verb_RukayaSkyInterceptorShoot).IsAssignableFrom(turretDef.Verbs[0].verbClass))
                yield return "Rukaya sky interceptor requires a turretDef whose primary verb is Verb_RukayaSkyInterceptorShoot "
                    + "or a subclass of it (such as Verb_RukayaNavalArtilleryShoot).";
            if (scanIntervalTicks < 1 || crashDurationTicks < 1)
                yield return "Rukaya sky interceptor has an invalid tick interval.";
            if (airDefenseRange < 1f || airDefenseRange > GenRadial.MaxRadialPatternRadius)
                yield return "Rukaya sky interceptor has an invalid air-defense range (must be at least 1 and within "
                    + GenRadial.MaxRadialPatternRadius + ", the largest radius ring the game can draw).";
            if (interceptBlastRadius < 0f || interceptBlastRadius > GenRadial.MaxRadialPatternRadius)
                yield return "Rukaya sky interceptor has an invalid interception blast radius (must be non-negative and within "
                    + GenRadial.MaxRadialPatternRadius + ").";
            if (maxSecondaryTargets < 0)
                yield return "Rukaya sky interceptor has a negative secondary target limit.";
            if (dropPodBlastRadius < 0f || dropPodBlastRadius > GenRadial.MaxRadialPatternRadius
                || dropPodMaxSecondaryTargets < 0 || dropPodWarmupTicks < 1 || dropPodCooldownTicks < 1)
                yield return "Rukaya sky interceptor has invalid drop-pod interception settings.";
            if (airMagazineCapacity < 0 || airMagazineCapacity > 4 || (airMagazineCapacity > 0 && magazineReloadTicks < 1))
                yield return "Rukaya sky interceptor has invalid magazine settings.";
            if (airMagazineCapacity > 0 && turretDef?.Verbs?.Count > 0 && (turretDef.Verbs[0].burstShotCount > 4
                || turretDef.Verbs[0].ticksBetweenBurstShots < 1))
                yield return "Rukaya missile interceptor requires at most four burst shots and a positive shot interval.";
            if (crashOffsetRadius < 0f || pawnDamageCount < 0 || pawnDamageAmount < 0f || cargoLossFraction < 0f || cargoLossFraction > 1f)
                yield return "Rukaya sky interceptor has invalid crash damage settings.";
            if (beamDurationTicks < 1 || beamWidth <= 0f || beamGlowWidth < beamWidth)
                yield return "Rukaya sky interceptor has invalid beam settings.";
            if (meteoriteFragmentCount.min < 2 || meteoriteFragmentCount.max < meteoriteFragmentCount.min
                || meteoriteScatterRadius < 2f || meteoriteOrePerFragment.min < 1
                || meteoriteOrePerFragment.max < meteoriteOrePerFragment.min)
                yield return "Rukaya sky interceptor has invalid meteorite fragment settings.";
            const RukayaAirTargetCategory allCategories = RukayaAirTargetCategory.DropPods
                | RukayaAirTargetCategory.NaturalSkyfallers | RukayaAirTargetCategory.OtherSkyfallers
                | RukayaAirTargetCategory.OverheadProjectiles | RukayaAirTargetCategory.ArcingProjectiles
                | RukayaAirTargetCategory.Flyers;
            if ((allowedCategories & ~allCategories) != 0)
                yield return "Rukaya sky interceptor has unknown air target categories.";
            // Pawn（机械体）宿主：地面火力由同一个 ThingDef 上**另一门** CompProperties_TurretGun 提供，
            // 本组件是"对空吊舱"。两门 comp 的先后顺序有实际后果——Pawn.TryGetAttackVerb(allowTurrets)
            // 取的是 comps 列表里**第一个** CompTurretGun 的 verb：顺序排反，机械体的"攻击"就会指到
            // 那把只打空中目标的炮上（拿去打地面等于空放，而且会与地面炮抢同一套索敌状态）。
            // 所以这里要求：地面炮在前，对空吊舱在后。
            if (parentDef?.thingClass != null && typeof(Pawn).IsAssignableFrom(parentDef.thingClass)
                && parentDef.comps != null)
            {
                bool selfSeen = false;
                foreach (CompProperties comp in parentDef.comps)
                {
                    if (ReferenceEquals(comp, this))
                    {
                        selfSeen = true;
                        continue;
                    }
                    if (selfSeen && comp is CompProperties_TurretGun)
                    {
                        yield return "Rukaya sky interceptor must be declared AFTER the host's ground turret comp "
                            + "on a Pawn host: Pawn.TryGetAttackVerb takes the first CompTurretGun in the comp list, "
                            + "so the ground gun has to come first.";
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 载体模式判定（**只用于 XML 静态校验**）：宿主是 <see cref="Building_TurretGun"/>
        /// （自带炮身，例如舰炮炮塔）时，组件走"载体模式"——它**仍然造自己那把对空枪**
        /// （<c>Props.turretDef</c>），只是把地面索敌交回宿主、并且不自绘炮身。详见
        /// <see cref="CompRukayaSkyInterceptor.CarrierMode"/> 与
        /// <c>设计文档/分层防空系统_实现交接.md</c> §7.2。
        ///
        /// 注意这里**看不到**第二种载体模式：Pawn（机械体）宿主是不是"对空吊舱"，取决于运行时
        /// <c>&lt;comps&gt;</c> 里有没有另一门 <see cref="CompProperties_TurretGun"/>，
        /// 那由 <see cref="CompRukayaSkyInterceptor.AirOnlyCompanion"/> 在运行时判定，
        /// 本方法只负责拦"把宿主的 <c>building.turretGunDef</c> 直接当对空炮用"这种静态配置错误。
        /// </summary>
        public static bool CarrierHost(ThingDef parentDef)
        {
            return parentDef?.thingClass != null && typeof(Building_TurretGun).IsAssignableFrom(parentDef.thingClass);
        }

        public bool AllowsTarget(RukayaAirTargetCategory category, ThingDef targetDef)
        {
            if (targetDef == null || category == RukayaAirTargetCategory.None
                || (allowedCategories & category) == 0)
                return false;
            if (targetDefBlacklist != null && targetDefBlacklist.Contains(targetDef))
                return false;
            return targetDefWhitelist == null || targetDefWhitelist.Count == 0
                || targetDefWhitelist.Contains(targetDef);
        }

        public int scanIntervalTicks = 3;
        /// <summary>
        /// 炮弹、PawnFlyer 等局部目标的防空半径；空投仓与自然天降物全图拦截。
        /// 与宿主对地射程无关。已发射的拦截弹不再因为目标越界而失效。
        /// </summary>
        public float airDefenseRange = 35f;
        public float dropPodBlastRadius = 8f;
        public int dropPodMaxSecondaryTargets = 5;
        public int dropPodWarmupTicks = 6;
        public int dropPodCooldownTicks = 24;
        // 非零时启用真实逐发齐射：一枚备弹只生成一枚拦截导弹，不补装饰弹。
        public int airMagazineCapacity;
        public int magazineReloadTicks = 360;
        /// <summary>
        /// **一发拦截弹的半径判定半径（格）**：同一次射击里，主目标周围这个半径内的其它合法空中目标
        /// 会被一并拦下——每发仍然只扣一次装填（见 <c>CompRukayaSkyInterceptor.FireAirShot</c>）。
        /// 0 = 关闭，退回"一发只打一个目标"。默认 4 格，对应 2026-10-08 实测反馈"拦截效率过低"。
        /// </summary>
        public float interceptBlastRadius = 4f;
        /// <summary>一次射击最多额外带走几个次级目标（防止一发清空整波空投，便于按需调平衡）。</summary>
        public int maxSecondaryTargets = 2;
        /// <summary>新放置的宿主是否默认开启对空模式（关闭时玩家用 Gizmo 手动切换）。</summary>
        public bool airModeOnDefault;
        public RukayaAirTargetCategory allowedCategories = RukayaAirTargetCategory.DropPods | RukayaAirTargetCategory.NaturalSkyfallers;
        public List<ThingDef> targetDefWhitelist = new List<ThingDef>();
        public List<ThingDef> targetDefBlacklist = new List<ThingDef>();
        public bool instantBeam;
        public float crashOffsetRadius = 4f;
        public int crashDurationTicks = 30;
        public int pawnDamageCount = 5;
        public float pawnDamageAmount = 20f;
        public float cargoLossFraction = 0.8f;
        public IntRange meteoriteFragmentCount = new IntRange(3, 5);
        public float meteoriteScatterRadius = 8f;
        public IntRange meteoriteOrePerFragment = new IntRange(1, 2);
        public int beamDurationTicks = 10;
        public float beamWidth = 0.16f;
        public float beamGlowWidth = 0.55f;
        public Color beamColor = new Color(1f, 0.45f, 0.24f);
    }
}
