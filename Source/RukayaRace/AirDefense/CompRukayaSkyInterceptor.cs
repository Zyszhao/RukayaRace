using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using RukayaRace.Projectiles;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class CompRukayaSkyInterceptor : CompTurretGun
    {
        public new CompProperties_RukayaSkyInterceptor Props => (CompProperties_RukayaSkyInterceptor)props;
        public bool AirMode => airMode;
        public bool AirBurstActive => airBurstActive;
        public int AvailableAirRounds => Math.Max(0, airRoundsLeft);
        private bool UsesAirMagazine => Props.airMagazineCapacity > 0;
        private bool HasAirResources => (!UsesAirMagazine || AvailableAirRounds > 0)
            && (parent.GetComp<CompRukayaLaserHeat>()?.CanFire ?? true);
        public Verb_RukayaSkyInterceptorShoot WeaponVerb => gun?.TryGetComp<CompEquippable>()?.PrimaryVerb as Verb_RukayaSkyInterceptorShoot;

        /// <summary>
        /// 载体模式：宿主已经自带一门负责地面火力的炮时返回 true，本组件退化成**只管对空**的
        /// "对空吊舱"。两种宿主都会进入这个模式：
        ///
        /// ① <see cref="Building_TurretGun"/>（舰炮炮塔）：地面炮身是建筑自带的 <c>building.turretGunDef</c>
        ///    （原版把它存在 <c>Building_TurretGun.gun</c> 字段里，不是 comp）；
        /// ② Pawn（卢卡娅机械体的激光/机炮/导弹型）：同一个 ThingDef 的 &lt;comps&gt; 里**另有一门**
        ///    <see cref="CompTurretGun"/> 负责地面火力（见 <see cref="GroundTurretCompanion"/>），
        ///    本组件是挂在同一底盘上的独立对空炮座。
        ///
        /// 按设计口径（见 设计文档/分层防空系统_实现交接.md §7.2）：本组件**不占用宿主的 verb**，
        /// 自己那一把对空枪与自己的 burst 状态机完全独立，与宿主的 burst 互不干扰；
        /// 唯一需要让步的是"地面索敌"——宿主自己在做，所以载体模式下本组件不再跑地面逻辑
        /// （见 <see cref="GroundTick"/>），也不再自绘炮身（见 PostDraw）。
        /// </summary>
        public bool CarrierMode => parent is Building_TurretGun || GroundTurretCompanion != null;

        /// <summary>
        /// "对空吊舱"模式：机械体宿主的 <c>&lt;comps&gt;</c> 里已经有另一门地面炮，
        /// 本组件只做对空（<see cref="CarrierMode"/> 也随之为 true）。
        /// 用来决定要不要把自己那个（本来给"独立模式"准备的）「炮塔开关」按钮让出去——
        /// 同一个机械体上出现两个同名开关只会让人分不清哪个管地面、哪个管对空。
        /// </summary>
        public bool AirOnlyCompanion => parent is Pawn && GroundTurretCompanion != null;

        /// <summary>
        /// 宿主上那门负责地面火力的炮（另一个 <see cref="CompTurretGun"/>，只在 Pawn 宿主上存在）。
        /// 没有它时本组件按 <c>turretDef</c> 自己造枪、地面与对空都归它管——即文档里的"独立模式"。
        /// 顺序上取 <c>AllComps</c> 里第一个，配合 <see cref="CompProperties_RukayaSkyInterceptor.ConfigErrors"/>
        /// 里那条"地面炮必须排在前面"的校验，保证与 <c>Pawn.TryGetAttackVerb</c> 取到的是同一门。
        /// </summary>
        private CompTurretGun GroundTurretCompanion
        {
            get
            {
                if (!(parent is ThingWithComps host))
                    return null;
                List<ThingComp> comps = host.AllComps;
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is CompTurretGun turret && turret != this)
                        return turret;
                }
                return null;
            }
        }

        /// <summary>
        /// 对空交战期间炮塔应当指向的角度（度）。机械体没有 <c>TurretTop</c>，炮塔是渲染树上的
        /// <c>PawnRenderNode_TurretGun</c>，它读的是**地面那门炮**的 <c>curRotation</c>；
        /// 本属性就是给 <see cref="Patch_PawnTurretGun_RotationFor_RukayaAirAim"/> 在绘制那一刻
        /// 改写那个字段用的。
        ///
        /// 口径与建筑炮塔顶完全一致（见 <see cref="Patch_TurretTop_TurretTopTick_RukayaAirAim"/> 的注释）：
        /// * 拦截弹在途 → 用**发射弹道角** <see cref="AirAimAngle"/>；
        /// * 预热/锁定期 → 指向空中目标。
        /// 没在对空交战时返回 null，渲染完全交还原版逻辑。
        /// </summary>
        public float? AirAimRotation
        {
            get
            {
                if (!airMode)
                    return null;
                // 对空连发打得正起劲：整轮 burst 保持"开火那一刻的弹道角"。
                // 目标很可能已经被第一发拦掉了，但"还在连续开火"的画面不能因此回正（见 CompTick）。
                if (airBurstActive)
                    return airBurstAimAngle;
                float? shotAngle = AirAimAngle;
                if (shotAngle.HasValue)
                    return shotAngle.Value;
                if (airTarget == null)
                    return null;
                return (airTarget.DrawPos - parent.DrawPos).AngleFlat() + Props.angleOffset;
            }
        }

        /// <summary>
        /// 当前正在对空交战的瞄准目标，供 <see cref="Patch_TurretTop_TurretTopTick_RukayaAirAim"/>
        /// 把宿主炮塔顶扳向空中目标用（原版炮塔顶由 TurretTop.TurretTopTick 每 tick 覆盖，
        /// 只有补丁能在它之后改写；组件自己不去碰宿主的 verb）。
        /// </summary>
        public Thing AirAimTarget => airMode ? airTarget : null;

        /// <summary>
        /// 本座炮塔刚打出去的那枚拦截弹的**发射弹道角**（度），拦截弹还在飞时非空。
        ///
        /// 口径（需求方 2026-10-08 实测反馈）：拦截进行时炮塔顶应当朝向"拦截弹发射出去的角度"，
        /// 而不是原版 <c>ForceFaceTarget</c> 用的目标 **Cell**（空投仓的地面投影，看起来像在瞄落点）。
        /// 这里记录的就是开火那一刻炮口自己的 <c>curRotation</c>：从炮塔指向目标的 **DrawPos（空中位置）**
        /// 再加上 <see cref="CompProperties_TurretGun.angleOffset"/>，与拦截弹初始飞行角一致。
        /// 弹体结算/销毁后返回 null，炮塔顶交还给原版逻辑。
        /// </summary>
        public float? AirAimAngle
        {
            get
            {
                if (committedShotTarget == null || committedShotTarget.Destroyed || !committedShotTarget.Spawned
                    || !Projectile_RukayaSkyInterceptor.HasShotFor(committedShotTarget))
                    return null;
                return committedShotAngle;
            }
        }

        public bool CanOperate => CanOperateCore && !parent.Position.Roofed(parent.Map);

        private bool CanOperateCore
        {
            get
            {
                if (!parent.Spawned || WeaponVerb == null || fireAtWillField == null)
                    return false;
                if (parent.GetComp<CompPowerTrader>() is CompPowerTrader power && !power.PowerOn)
                    return false;
                if (parent.GetComp<CompFlickable>() is CompFlickable flickable && !flickable.SwitchIsOn)
                    return false;
                if (parent.GetComp<CompBreakdownable>() is CompBreakdownable breakdownable && breakdownable.BrokenDown)
                    return false;
                if (parent.GetComp<CompRefuelable>() is CompRefuelable fuel && !fuel.HasFuel)
                    return false;
                if (parent.GetComp<CompCanBeDormant>() is CompCanBeDormant dormant && !dormant.Awake)
                    return false;
                if (parent is Pawn pawn)
                    return !pawn.Dead && !pawn.Downed && pawn.Awake() && !pawn.stances.stunner.Stunned
                        && !TurretDestroyed && (!pawn.IsColonyMechPlayerControlled || (bool)fireAtWillField.GetValue(this));
                if (parent is Building)
                    return !(parent.GetComp<CompStunnable>()?.StunHandler.Stunned ?? false)
                        && (parent.GetComp<CompMannable>()?.MannedNow ?? true)
                        && (parent.GetComp<CompInitiatable>()?.Initiated ?? true)
                        && !(parent.GetComp<CompMechPowerCell>()?.depleted ?? false)
                        && !(parent.GetComp<CompHackable>()?.IsHacked ?? false);
                return false;
            }
        }

        public override void CompTick()
        {
            RefreshMagazine();
            if (!CanOperateCore)
            {
                FinishBurst();
                EndAirBurst();
                ResetAirTarget();
                SetAirTarget(null);
                return;
            }
            if (!airMode)
            {
                EndAirBurst();
                SetAirTarget(null);
                GroundTick();
                return;
            }
            // 机炮/激光保留完整视觉连发；导弹每发重新选敌、逐发扣弹，空弹或无目标即结束。
            if (airBurstActive)
            {
                if (WeaponVerb.state == VerbState.Bursting)
                {
                    curRotation = airBurstAimAngle;
                    WeaponVerb.VerbTick();
                    return;
                }
                EndAirBurst();
            }
            // 恢复冷却按时间流逝，不能在两波目标之间冻结，否则新空投到达时又白等一轮。
            if (CarrierMode && burstCooldownTicksLeft > 0)
                burstCooldownTicksLeft--;
            if (!HasAirResources)
            {
                SetAirTarget(null);
                ResetAirTarget();
                GroundTick();
                return;
            }
            if (airTarget != null && (!CanTarget(airTarget, parent.Faction, parent.Map)
                || !AllowsTarget(airTarget) || !IsWithinAirRange(airTarget) || IsClaimedByOther(airTarget)))
                SetAirTarget(null);
            if (airTarget != null && WeaponVerb.state != VerbState.Bursting
                && Projectile_RukayaSkyInterceptor.HasShotFor(airTarget))
                SetAirTarget(null);
            if (airTarget != null)
                ClaimAirTarget(airTarget);
            // 保留有效锁定，避免密集空投更换最近目标时反复重置预热。
            if (airTarget == null && WeaponVerb.state != VerbState.Bursting && parent.IsHashIntervalTick(Props.scanIntervalTicks))
                SetAirTarget(FindTarget());
            Thing current = currentTarget.Thing;
            // 只有"这一轮 burst 确实是本组件自己的对空射击"时才交给下面的对空分支。
            // 独立模式（宿主没有第二门炮，见 CarrierMode）下，本组件那把枪同时负责地面火力：
            // 对地连发打到一半时 current 是地面目标，CanEngage 必然为 false —— 不区分就会
            // 把 24 连发的地面 burst 打断成单发。加这一条后，地面 burst 落到下面的
            // "WeaponVerb.state == Bursting → GroundTick()" 分支，由原版逻辑继续推进。
            if (WeaponVerb.state == VerbState.Bursting && current != null && IsAirTarget(current))
            {
                if (!CanEngage(current))
                {
                    FinishBurst();
                    ResetAirTarget();
                    SetAirTarget(null);
                }
                else
                {
                    curRotation = (current.DrawPos - parent.DrawPos).AngleFlat() + Props.angleOffset;
                    WeaponVerb.VerbTick();
                    return;
                }
            }
            if (!CanOperate || airTarget == null || !CanEngage(airTarget))
            {
                if (!CanOperate)
                    SetAirTarget(null);
                if (IsAirTarget(currentTarget.Thing))
                    ResetAirTarget();
                GroundTick();
                return;
            }
            if (WeaponVerb.state == VerbState.Bursting)
            {
                GroundTick();
                return;
            }
            if (currentTarget.Thing != airTarget)
            {
                currentTarget = new LocalTargetInfo(airTarget);
                burstWarmupTicksLeft = 0;
            }
            curRotation = (airTarget.DrawPos - parent.DrawPos).AngleFlat() + Props.angleOffset;
            WeaponVerb.VerbTick();
            if (burstWarmupTicksLeft > 0)
            {
                burstWarmupTicksLeft--;
                if (burstWarmupTicksLeft == 0 && CanEngage(airTarget) && WeaponVerb.Available())
                    BeginAirBurst(airTarget);
                return;
            }
            if (!CarrierMode && burstCooldownTicksLeft > 0)
                burstCooldownTicksLeft--;
            if (burstCooldownTicksLeft > 0 || !WeaponVerb.Available())
                return;
            float aimingFactor = parent is Pawn pawn ? pawn.GetStatValue(StatDefOf.AimingDelayFactor) : 1f;
            burstWarmupTicksLeft = Mathf.Max(1, (WeaponVerb.WarmupTime * aimingFactor).SecondsToTicks());
            if (airTarget is DropPodIncoming)
                burstWarmupTicksLeft = Math.Min(burstWarmupTicksLeft, Props.dropPodWarmupTicks);
        }

        /// <summary>
        /// 载体模式刻意不调用 base：宿主自己的枪由 <c>Building_TurretGun.ExposeData</c> 负责存档，
        /// 而 base 在 PostLoadInit 发现 gun 为空时会再造一把（CompTurretGun 的兜底逻辑）——
        /// 那正是载体模式要避免的第二把枪。这里改为只存组件自己的对空状态。
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref airMode, "rukayaAirMode");
            Scribe_References.Look(ref airTarget, "rukayaTrackedSkyTarget");
            // 炮塔顶朝向用的"本次已发射弹道"：纯表现状态，但存下来读档时炮塔顶不会突然回正。
            Scribe_References.Look(ref committedShotTarget, "rukayaCommittedAirShotTarget");
            Scribe_Values.Look(ref committedShotAngle, "rukayaCommittedAirShotAngle", 0f);
            Scribe_Values.Look(ref airRoundsLeft, "rukayaAirRoundsLeft", -1);
            Scribe_Values.Look(ref magazineReadyTick, "rukayaAirMagazineReadyTick", -1);
        }

        /// <summary>
        /// 造枪照旧交给 base（CompTurretGun 按 <c>Props.turretDef</c> 造**本组件自己的**对空炮身）。
        /// 载体模式下宿主那把枪由宿主自己造、自己用，两者互不相干。
        /// </summary>
        public override void PostPostMake()
        {
            base.PostPostMake();
            airMode = Props.airModeOnDefault;
            RefreshMagazine();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
                airMode = Props.airModeOnDefault;
            RefreshMagazine();
        }

        /// <summary>
        /// 地面索敌。载体模式下宿主自己的 <c>Building_TurretGun.Tick</c> 已经在做这件事，
        /// 这里必须空转——否则同一座炮塔会对地面目标开两次火（这正是原设计禁止把本组件
        /// 挂到 Building_TurretGun 上的原因；由本方法解除，而不是去共用宿主的枪）。
        /// 对空不受影响：那走的是本组件自己的 gun 与自己的 Verb。
        /// </summary>
        private void GroundTick()
        {
            if (CarrierMode)
                return;
            base.CompTick();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            SetAirTarget(null);
            base.PostDeSpawn(map, mode);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 对空吊舱模式（机械体宿主 + 同一底盘上的另一门地面炮）：地面那门炮的 comp 已经给出了
            // 一个「炮塔开关」按钮，它管的是地面开火。这里若再给一个同名按钮，玩家会分不清哪个管
            // 地面、哪个管对空——而且关掉它等于把对空也一起关了（CanOperateCore 会读到 false）。
            // 所以吊舱模式只保留下面这个「启用对空模式」开关。
            if (!AirOnlyCompanion)
            {
                foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                    yield return gizmo;
            }
            if (parent.Faction != Faction.OfPlayer)
                yield break;
            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = "Rukaya_SkyInterceptorMode".Translate(),
                defaultDesc = "Rukaya_AirDefenseCoverage".Translate(Props.airDefenseRange),
                icon = TexCommand.Attack,
                isActive = () => airMode,
                toggleAction = ToggleMode
            };
            if (!airMode && BindingError() != null)
                command.Disable("Rukaya_SkyInterceptorUnsupported".Translate());
            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            string result = airMode ? "Rukaya_SkyInterceptorMode".Translate().ToString() : null;
            if (UsesAirMagazine)
            {
                string magazine = "Rukaya_AirMagazine".Translate(AvailableAirRounds, Props.airMagazineCapacity);
                if (AvailableAirRounds == 0)
                    magazine += " — " + "Rukaya_AirMagazineReloading".Translate(
                        Math.Max(0, magazineReadyTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
                result = result.NullOrEmpty() ? magazine : result + "\n" + magazine;
            }
            return result;
        }

        /// <summary>
        /// 选中时画出**绿色防空范围圈**（半径 = <see cref="CompProperties_RukayaSkyInterceptor.airDefenseRange"/>）。
        ///
        /// 用 <see cref="ThingComp.PostDrawExtraSelectionOverlays"/> 而不是打 Harmony 补丁：
        /// <c>ThingWithComps.DrawExtraSelectionOverlays</c> 本来就会遍历所有 comp 调这个方法，
        /// 而 <c>Building_TurretGun.DrawExtraSelectionOverlays</c> 会先调 base 走进这条链，
        /// 所以炮塔宿主和将来的 Pawn 宿主都能自动生效，且无需碰原版方法。
        ///
        /// 颜色分两档：能作战时是亮绿，不能作战（断电/断料/开关关掉/被屋顶挡住）时是暗绿——
        /// 这样"为什么它不打空中目标"能直接从颜色上看出来。
        /// </summary>
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (parent.Faction != Faction.OfPlayer || !airMode || !parent.Spawned || parent.Map == null)
                return;
            GenDraw.DrawRadiusRing(parent.Position, Props.airDefenseRange,
                CanOperateCore ? AirRangeRingColorActive : AirRangeRingColorIdle);
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (CarrierMode)
                return;   // 载体模式下炮身由宿主自己的 TurretTop 绘制，这里再画一层就是双影
            if (parent is Building && gun != null)
                gun.Graphic.DrawWorker(parent.DrawPos + Altitudes.AltIncVect, Rot4.North, gun.def, gun, curRotation);
        }

        public bool CanEngage(Thing target)
        {
            return CanOperate && CanTarget(target, parent.Faction, parent.Map)
                && AllowsTarget(target) && IsWithinAirRange(target) && !IsClaimedByOther(target)
                && !Projectile_RukayaSkyInterceptor.HasShotFor(target);
        }

        public bool FireAirShot(Verb_RukayaSkyInterceptorShoot verb)
        {
            // 视觉连发：本轮 burst 的**真拦截**已经在前面的某一发里定论了（airBurstResolved）。
            // 目标此刻多半已经被拦下（甚至已经销毁），所以这里只把"还在连续开火"的画面打出去：
            // 激光型补一道光束，机炮型飞一发不结算的曳光弹。永远返回 true ——
            // 返回 false 会让原版 burst 状态机（Verb.TryCastNextBurstShot）把连发直接掐掉。
            if (airBurstActive && airBurstResolved && !UsesAirMagazine)
                return EmitAirBurstVisualShot(verb);
            if (!HasAirResources || UsesAirMagazine && airShotsThisBurst >= 4)
                return false;
            Thing target = verb.CurrentTarget.Thing;
            if (UsesAirMagazine && !CanEngage(target))
            {
                target = FindTarget();
                SetAirTarget(target);
                verb.SetAirBurstTarget(target);
            }
            if (!CanEngage(target) || !verb.Available())
                return false;
            ThingDef projectile = verb.Projectile;
            if (projectile?.projectile == null)
            {
                Log.ErrorOnce($"[Rukaya] {parent.def.defName}: invalid air interceptor projectile {projectile?.defName ?? "null"}.", parent.thingIDNumber ^ 187312);
                return false;
            }
            RukayaLaserBeamExtension beam = projectile.GetModExtension<RukayaLaserBeamExtension>();
            // 口径：**拦截行为与拦截动画解耦**。
            // * 拦截在开火这一刻（上面的 CanEngage 通过）就已经定论：这一发必定拦下。
            // * 飞出去的弹体只是**动画载体**：把"弹体飞过去 + 目标继续下落到交汇点"播完，
            //   播完（接触或到期限）才把结果落地；它追不上、目标状态变化都不会让拦截失效。
            // * 目标只剩 <=1 tick 就落地时没有时间播动画，直接结算（否则目标先落地会把这次
            //   已经定论的拦截吃掉）。
            // instant 分支保留给将来的激光型单位（instantBeam / Projectile_RukayaLaserPulse）。
            bool instant = Props.instantBeam || projectile.thingClass == typeof(Projectile_RukayaLaserPulse);
            RukayaSkyCrashSettings settings = new RukayaSkyCrashSettings(Props);
            // 口径：**一发 = 一次带半径的判定**（需求方 2026-10-08 反馈"拦截效率过低"）。
            // 主目标照旧"开火即定论"；半径内的次级目标共用同一次射击、同一份装填（弹药只扣一次），
            // 但每个次级目标仍要各自过一次 CanTarget —— 它们从来没被承诺过，拦不下就跳过。
            // 半径基准与发射弹道角都取开火**之前**的值：即时结算分支会把目标当场转成坠毁体/碎块，
            // 之后再读 target.Position / DrawPos 就是"已销毁实体的位置"（会刷红字）。
            IntVec3 blastCenter = target.Position;
            Vector3 targetPosition = target.DrawPos;
            float launchAngle = (targetPosition - parent.DrawPos).AngleFlat() + Props.angleOffset;
            if (!LaunchInterceptorAt(target, verb, projectile, settings))
                return false;
            NotifyAirRoundLaunched();
            parent.GetComp<CompRukayaLaserHeat>()?.NotifyAirShot();
            committedShotTarget = target;
            committedShotAngle = launchAngle;
            // 机炮/激光后续只补视觉；有备弹限制的导弹每发重新结算。
            airBurstResolved = true;
            airBurstAimPosition = targetPosition;
            airBurstAimAngle = launchAngle;
            int secondaryLimit = target is DropPodIncoming ? Props.dropPodMaxSecondaryTargets : Props.maxSecondaryTargets;
            // 真实导弹一发只对应一个目标，不能用次级动画绕过四枚备弹限制。
            if (!UsesAirMagazine && secondaryLimit > 0)
            {
                int secondaries = 0;
                foreach (Thing candidate in BlastCandidates(target, blastCenter))
                {
                    if (secondaries >= secondaryLimit)
                        break;
                    if (LaunchInterceptorAt(candidate, verb, projectile, settings))
                        secondaries++;
                }
            }
            verb.EquipmentSource?.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
            verb.EquipmentSource?.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            if (instant)
            {
                Vector3 laserOrigin = parent.DrawPos.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
                // 只改画出来的光束：起点前移到炮口并随整轮节拍左右摆，终点反向摆。
                // 拦截结果在上面的 LaunchInterceptorAt 里已经定论，这里不碰任何判定。
                RukayaBarrageSway.BeamSway(projectile, parent, laserOrigin, targetPosition,
                    Find.TickManager.TicksGame, out Vector3 beamOrigin, out Vector3 beamDestination);
                parent.Map.GetComponent<MapComponent_RukayaLaserLines>().ScheduleLaserLine(beamOrigin, beamDestination,
                    beam?.beamDurationTicks ?? Props.beamDurationTicks, beam?.beamWidth ?? Props.beamWidth,
                    beam?.beamGlowWidth ?? Props.beamGlowWidth, beam?.beamColor ?? Props.beamColor, beam?.impactGlowSize ?? 1f);
            }
            return true;
        }

        /// <summary>
        /// 机炮/激光首发结算、后续补视觉；导弹每发是真实拦截，最多四发。
        /// 发射节拍由炮座 XML 的 burstShotCount / ticksBetweenBurstShots 决定。
        /// </summary>
        private void BeginAirBurst(Thing target)
        {
            Vector3 targetPosition = target.DrawPos;
            airBurstAimPosition = targetPosition;
            airBurstAimAngle = (targetPosition - parent.DrawPos).AngleFlat() + Props.angleOffset;
            airBurstResolved = false;
            airShotsThisBurst = 0;
            airBurstWasDropPod = target is DropPodIncoming;
            airBurstActive = true;
            burstWarmupTicksLeft = 0;
            WeaponVerb.BeginAirBurst(target);
            // 单发炮座第一发就退出 burst，这里顺手收尾。
            if (WeaponVerb.state != VerbState.Bursting)
                EndAirBurst();
        }

        private void EndAirBurst()
        {
            if (airBurstActive && airBurstWasDropPod && !UsesAirMagazine)
                burstCooldownTicksLeft = Math.Min(burstCooldownTicksLeft, Props.dropPodCooldownTicks);
            airBurstActive = false;
            airBurstResolved = false;
            airBurstWasDropPod = false;
        }

        /// <summary>
        /// 视觉连发的那几发：把"还在开火"画出来，但不结算、不 claim 任何东西。
        /// * 激光型（instantBeam）= 朝已定论的拦截点再补一道光束；
        /// * 机炮型 = 飞一发只播弹道、不结算的曳光弹（见
        ///   <see cref="Projectile_RukayaSkyInterceptor.InitializeVisual"/>）。
        /// </summary>
        private bool EmitAirBurstVisualShot(Verb_RukayaSkyInterceptorShoot verb)
        {
            ThingDef projectile = verb.Projectile;
            if (projectile?.projectile == null)
                return false;
            bool instant = Props.instantBeam || projectile.thingClass == typeof(Projectile_RukayaLaserPulse);
            if (instant)
            {
                RukayaLaserBeamExtension beam = projectile.GetModExtension<RukayaLaserBeamExtension>();
                Vector3 laserOrigin = parent.DrawPos.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
                // 纯视觉摆动：后续几发只画不打，光束端点可以自由摆，拦截结果早已定论。
                RukayaBarrageSway.BeamSway(projectile, parent, laserOrigin, airBurstAimPosition,
                    Find.TickManager.TicksGame, out Vector3 beamOrigin, out Vector3 beamDestination);
                parent.Map.GetComponent<MapComponent_RukayaLaserLines>().ScheduleLaserLine(beamOrigin, beamDestination,
                    beam?.beamDurationTicks ?? Props.beamDurationTicks, beam?.beamWidth ?? Props.beamWidth,
                    beam?.beamGlowWidth ?? Props.beamGlowWidth, beam?.beamColor ?? Props.beamColor,
                    beam?.impactGlowSize ?? 1f);
                return true;
            }
            // 机炮型的后续视觉连发：只把这发曳光弹的**目标落点**横向扇开，让它看起来在扫。
            // 这些弹体本来就是"只播动画、不结算"的，所以改落点不会影响任何判定。
            Vector3 visualAimPosition = airBurstAimPosition + RukayaBarrageSway.AirAimOffset(
                projectile, parent, parent.DrawPos, airBurstAimPosition, Find.TickManager.TicksGame);
            // 目标贴着地图边缘时，横向扇开可能把视觉落点推出地图；退回真实拦截点，避免绘制/落点越界。
            if (!visualAimPosition.ToIntVec3().InBounds(parent.Map))
                visualAimPosition = airBurstAimPosition;
            Projectile_RukayaSkyInterceptor airProjectile = null;
            try
            {
                ThingDef airProjectileDef = DefDatabase<ThingDef>.GetNamed("Rukaya_SkyInterceptorAirProjectile");
                airProjectile = (Projectile_RukayaSkyInterceptor)ThingMaker.MakeThing(airProjectileDef);
                if (GenSpawn.Spawn(airProjectile, parent.Position, parent.Map) != airProjectile || !airProjectile.Spawned)
                    throw new InvalidOperationException("Air burst visual projectile could not be spawned.");
                LocalTargetInfo aimCell = new LocalTargetInfo(visualAimPosition.ToIntVec3());
                airProjectile.Launch(parent, parent.DrawPos, aimCell, aimCell, ProjectileHitFlags.None,
                    equipment: verb.EquipmentSource);
                airProjectile.InitializeVisual(visualAimPosition, projectile);
            }
            catch (Exception exception)
            {
                if (airProjectile != null && !airProjectile.Destroyed)
                    airProjectile.Destroy(DestroyMode.Vanish);
                Log.Error($"[Rukaya] {parent.def.defName}: could not play the air burst visual shot: {exception}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 把一次拦截"发出去"：动画分支生成可见拦截弹（飞完或到期限才结算），即时分支当场结算。
        /// 主目标与半径内的次级目标共用本方法；**弹药记账不在这里**，所以一发只扣一次装填。
        /// </summary>
        private bool LaunchInterceptorAt(Thing target, Verb_RukayaSkyInterceptorShoot verb, ThingDef projectile,
            RukayaSkyCrashSettings settings)
        {
            bool instant = Props.instantBeam || projectile.thingClass == typeof(Projectile_RukayaLaserPulse);
            int resolveDeadlineTicks = ResolveDeadlineTicks(target);
            bool animate = !instant && (resolveDeadlineTicks == 0 || resolveDeadlineTicks > 1);
            Vector3 origin = parent.DrawPos.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            Map map = parent.Map;
            if (animate)
            {
                if (projectile.projectile.SpeedTilesPerTick <= 0f)
                {
                    Log.ErrorOnce($"[Rukaya] {parent.def.defName}: projectile {projectile.defName} has no air interception speed.", parent.thingIDNumber ^ 187313);
                    return false;
                }
                Projectile_RukayaSkyInterceptor airProjectile = null;
                try
                {
                    ThingDef airProjectileDef = DefDatabase<ThingDef>.GetNamed("Rukaya_SkyInterceptorAirProjectile");
                    airProjectile = (Projectile_RukayaSkyInterceptor)ThingMaker.MakeThing(airProjectileDef);
                    if (GenSpawn.Spawn(airProjectile, parent.Position, map) != airProjectile || !airProjectile.Spawned)
                        throw new InvalidOperationException("Air projectile could not be spawned.");
                    LocalTargetInfo airTargetInfo = new LocalTargetInfo(target);
                    airProjectile.Launch(parent, parent.DrawPos, airTargetInfo, airTargetInfo,
                        ProjectileHitFlags.IntendedTarget, equipment: verb.EquipmentSource);
                    airProjectile.Initialize(target, projectile, parent.Faction, settings, resolveDeadlineTicks);
                }
                catch (Exception exception)
                {
                    if (airProjectile != null && !airProjectile.Destroyed)
                        airProjectile.Destroy(DestroyMode.Vanish);
                    Log.Error($"[Rukaya] {parent.def.defName}: could not launch air projectile at {target.def.defName}: {exception}");
                    return false;
                }
                return true;
            }
            return RukayaAirImpact.TryIntercept(target, parent, parent.Faction, settings, origin);
        }

        /// <summary>
        /// 一发拦截弹的"半径判定"候选：与主目标（<paramref name="primaryCell"/>，取开火前的格子）
        /// 的水平距离在 <see cref="CompProperties_RukayaSkyInterceptor.interceptBlastRadius"/> 之内、
        /// 且本组件本来就有权打的其它空中目标（按距离由近到远）。数量上限由 <c>maxSecondaryTargets</c> 在外层截断。
        /// </summary>
        private List<Thing> BlastCandidates(Thing primary, IntVec3 primaryCell)
        {
            List<Thing> candidates = new List<Thing>();
            Map map = parent.Map;
            if (map == null || !primaryCell.IsValid)
                return candidates;
            float radius = primary is DropPodIncoming ? Props.dropPodBlastRadius : Props.interceptBlastRadius;
            float radiusSquared = radius * radius;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == primary || !CanTarget(thing, parent.Faction, map) || !AllowsTarget(thing) || !IsWithinAirRange(thing)
                    || IsClaimedByOther(thing) || Projectile_RukayaSkyInterceptor.HasShotFor(thing))
                    continue;
                if ((thing.Position - primaryCell).LengthHorizontalSquared > radiusSquared)
                    continue;
                candidates.Add(thing);
            }
            candidates.Sort((left, right) => (left.Position - primaryCell).LengthHorizontalSquared
                .CompareTo((right.Position - primaryCell).LengthHorizontalSquared));
            return candidates;
        }

        /// <summary>
        /// "这次拦截必须在多少 tick 内把结果播出去"。
        ///
        /// 拦截本身在开火那一刻就定论了，所以这个期限不是"成功条件"，而是**动画的收尾上限**：
        /// 超过它就让拦截弹加速追上（见 <see cref="Projectile_RukayaSkyInterceptor.Initialize"/>），
        /// 而不是让目标先落地、把一次注定命中的拦截吃掉。取目标自身剩余下落时间再减 1 tick，
        /// 保证结算发生在它落地之前。
        ///
        /// 返回 0 表示目标没有明确的下落期限（例如 PawnFlyer），此时只按弹体接触结算。
        /// </summary>
        private static int ResolveDeadlineTicks(Thing target)
        {
            if (target is Skyfaller skyfaller)
                return Mathf.Max(1, skyfaller.ticksToImpact - 1);
            if (target is Projectile projectile)
            {
                int remaining = Thing_RukayaAirborneDebris.RemainingFlightTicks(projectile);
                if (remaining > 0)
                    return Mathf.Max(1, remaining - 1);
            }
            return 0;
        }

        public static bool IsLiveTarget(Thing target, Map map)
        {
            if (target == null || !target.Spawned || target.Map != map || target.Destroyed)
                return false;
            if (target is Skyfaller skyfaller)
                return IsLiveTarget(skyfaller, map);
            if (target is PawnFlyer flyer)
                return flyer.FlyingPawn != null;
            return target is Projectile projectile && !(target is Projectile_RukayaSkyInterceptor)
                && projectile.def.projectile != null && CategoryOf(projectile) != RukayaAirTargetCategory.None
                && Thing_RukayaAirborneDebris.IsFlyingProjectile(projectile);
        }

        public static bool IsLiveTarget(Skyfaller target, Map map)
        {
            if (target == null || !target.Spawned || target.Map != map || target.ticksToImpact <= 0
                || target.def.skyfaller == null || target.def.skyfaller.reversed)
                return false;
            if (RukayaAirImpact.RequiresProjectileHit(target) && !RukayaAirImpact.HasMeteoriteInterceptWindow(target.ticksToImpact))
                return false;
            Type type = target.GetType();
            return type == typeof(Skyfaller) || target is DropPodIncoming || type == typeof(LavaRockIncoming);
        }

        public static bool CanTarget(Skyfaller target, Faction faction, Map map)
        {
            if (faction == null || !IsLiveTarget(target, map))
                return false;
            if (RukayaBossgroupDropPodProtection.IsProtected(target))
                return false;
            bool hostileShell = target.Faction != null && target.Faction.HostileTo(faction);
            if (target.Faction != null && !hostileShell)
                return false;
            bool hasPawn = false;
            foreach (Thing thing in target.innerContainer)
            {
                if (thing is ActiveTransporter transporter)
                {
                    foreach (Thing cargo in transporter.Contents.innerContainer)
                    {
                        if (cargo is Pawn pawn)
                        {
                            hasPawn = true;
                            if (pawn.Faction == null || !pawn.Faction.HostileTo(faction))
                                return false;
                        }
                        else if (cargo is IThingHolder)
                            return false;
                    }
                }
                else if (thing is Pawn pawn)
                {
                    hasPawn = true;
                    if (pawn.Faction == null || !pawn.Faction.HostileTo(faction))
                        return false;
                }
                else if (thing is IThingHolder)
                    return false;
            }
            if (hasPawn)
                return true;
            if (target is DropPodIncoming)
                return hostileShell;
            return hostileShell || Thing_RukayaSkyCrash.IsNatural(target.def);
        }

        public static bool CanTarget(Thing target, Faction faction, Map map)
        {
            if (faction == null || !IsLiveTarget(target, map))
                return false;
            if (target is Skyfaller skyfaller)
                return CanTarget(skyfaller, faction, map);
            if (target is PawnFlyer flyer)
                return flyer.FlyingPawn.Faction != null && flyer.FlyingPawn.Faction.HostileTo(faction);
            if (target is Projectile projectile)
                return projectile.Launcher?.Faction != null && projectile.Launcher.Faction.HostileTo(faction);
            return false;
        }

        public static RukayaAirTargetCategory CategoryOf(Thing target)
        {
            if (target is Skyfaller skyfaller)
            {
                if (skyfaller is Skyfaller_FlyingPawn)
                    return RukayaAirTargetCategory.None;
                if (skyfaller is DropPodIncoming)
                    return RukayaAirTargetCategory.DropPods;
                return Thing_RukayaSkyCrash.IsNatural(skyfaller.def)
                    ? RukayaAirTargetCategory.NaturalSkyfallers : RukayaAirTargetCategory.OtherSkyfallers;
            }
            if (target is PawnFlyer)
                return RukayaAirTargetCategory.Flyers;
            if (target is Projectile projectile && !(projectile is Projectile_RukayaSkyInterceptor))
            {
                if (projectile.def.projectile?.flyOverhead == true)
                    return RukayaAirTargetCategory.OverheadProjectiles;
                if (projectile.def.projectile?.arcHeightFactor > 0f)
                    return RukayaAirTargetCategory.ArcingProjectiles;
            }
            return RukayaAirTargetCategory.None;
        }

        public static CompRukayaSkyInterceptor ForWeapon(Verb verb)
        {
            if (!(verb?.caster is ThingWithComps host))
                return null;
            foreach (ThingComp comp in host.AllComps)
            {
                if (comp is CompRukayaSkyInterceptor interceptor && interceptor.WeaponVerb == verb)
                    return interceptor;
            }
            return null;
        }

        private void ToggleMode()
        {
            if (!airMode && BindingError() != null)
                return;
            airMode = !airMode;
            SetAirTarget(null);
            if (!airMode && IsAirTarget(currentTarget.Thing))
            {
                FinishBurst();
                EndAirBurst();
                ResetAirTarget();
            }
        }

        private Thing FindTarget()
        {
            Thing best = null;
            int bestUrgency = int.MaxValue;
            float bestDistance = float.MaxValue;
            foreach (Thing thing in parent.Map.listerThings.AllThings)
            {
                if (!CanTarget(thing, parent.Faction, parent.Map) || !AllowsTarget(thing)
                    || IsClaimedByOther(thing) || Projectile_RukayaSkyInterceptor.HasShotFor(thing))
                    continue;
                float distance = (thing.Position - parent.Position).LengthHorizontalSquared;
                if (!IsWithinAirRange(thing))
                    continue;
                int urgency = thing is Skyfaller skyfaller ? skyfaller.ticksToImpact : int.MaxValue / 2;
                if (best == null || urgency < bestUrgency || (urgency == bestUrgency && distance < bestDistance))
                {
                    best = thing;
                    bestUrgency = urgency;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public bool IsWithinAirRange(Thing target)
        {
            if (target == null || !target.Spawned || target.Map != parent.Map)
                return false;
            RukayaAirTargetCategory category = CategoryOf(target);
            return category == RukayaAirTargetCategory.DropPods || category == RukayaAirTargetCategory.NaturalSkyfallers
                || (target.Position - parent.Position).LengthHorizontalSquared <= Props.airDefenseRange * Props.airDefenseRange;
        }

        private void RefreshMagazine()
        {
            if (!UsesAirMagazine)
                return;
            if (airRoundsLeft < 0 || airRoundsLeft == 0 && magazineReadyTick >= 0
                && Find.TickManager.TicksGame >= magazineReadyTick)
            {
                airRoundsLeft = Props.airMagazineCapacity;
                magazineReadyTick = -1;
            }
        }

        private void NotifyAirRoundLaunched()
        {
            airShotsThisBurst++;
            if (!UsesAirMagazine)
                return;
            airRoundsLeft--;
            if (airRoundsLeft == 0)
                magazineReadyTick = Find.TickManager.TicksGame + Props.magazineReloadTicks;
        }

        private static bool IsAirTarget(Thing target)
        {
            return CategoryOf(target) != RukayaAirTargetCategory.None;
        }

        private bool AllowsTarget(Thing target)
        {
            return Props.AllowsTarget(CategoryOf(target), target?.def);
        }

        private bool IsClaimedByOther(Thing target)
        {
            if (!claims.TryGetValue(target, out System.WeakReference<CompRukayaSkyInterceptor> reference))
                return false;
            if (reference.TryGetTarget(out CompRukayaSkyInterceptor owner) && owner != this
                && owner.parent.Spawned && owner.airMode && owner.airTarget == target && owner.CanOperate)
                return true;
            if (owner != this)
                claims.Remove(target);
            return false;
        }

        private void ClaimAirTarget(Thing target)
        {
            if (target == null)
                return;
            claims.Remove(target);
            claims.Add(target, new System.WeakReference<CompRukayaSkyInterceptor>(this));
        }

        private void SetAirTarget(Thing target)
        {
            if (airTarget == target)
                return;
            if (airTarget != null && claims.TryGetValue(airTarget, out System.WeakReference<CompRukayaSkyInterceptor> reference)
                && reference.TryGetTarget(out CompRukayaSkyInterceptor owner) && owner == this)
                claims.Remove(airTarget);
            airTarget = target;
            ClaimAirTarget(target);
        }

        private void ResetAirTarget()
        {
            currentTarget = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
        }

        private void FinishBurst()
        {
            Verb verb = WeaponVerb;
            if (verb == null || verb.state != VerbState.Bursting)
                return;
            Action completed = verb.castCompleteCallback;
            verb.Reset();
            completed?.Invoke();
            verb.castCompleteCallback = completed;
            if (verb.verbProps.consumeFuelPerBurst > 0f)
                parent.GetComp<CompRefuelable>()?.ConsumeFuel(verb.verbProps.consumeFuelPerBurst);
        }

        private string BindingError()
        {
            string error = null;
            if (!(parent is Pawn) && !(parent is Building))
                error = "Sky interceptor weapon component requires a Pawn or Building host.";
            else if (WeaponVerb == null)
                error = $"Unsupported primary verb {gun?.TryGetComp<CompEquippable>()?.PrimaryVerb?.GetType().FullName ?? "null"}; "
                    + "use Verb_RukayaSkyInterceptorShoot or a subclass of it.";
            else if (CarrierMode && parent is Building_TurretGun turret && ReferenceEquals(gun, turret.gun))
                error = "Carrier mode must keep its own air-defense gun; turretDef points at the host's building.turretGunDef.";
            else if (fireAtWillField == null)
                error = "CompTurretGun.fireAtWill was not found; air fire is disabled.";
            if (error != null)
                Log.ErrorOnce($"[Rukaya] {parent.def.defName}: {error}", parent.thingIDNumber ^ 187311);
            return error;
        }

        private bool airMode;
        private bool airBurstWasDropPod;
        private int airRoundsLeft = -1;
        private int magazineReadyTick = -1;
        private int airShotsThisBurst;
        private Thing airTarget;
        /// <summary>最近一次"已定论"的拦截目标（<see cref="AirAimAngle"/> 用它判断弹体是否还在飞）。</summary>
        private Thing committedShotTarget;
        /// <summary>上述那发拦截弹的发射弹道角（度）。</summary>
        private float committedShotAngle;
        /// <summary>
        /// 本轮对空 burst 是否正在进行。一旦为 true，<see cref="CompTick"/> 就只推 burst 状态机、
        /// 不再重新索敌、也不让目标消失把连发掐掉 —— 见 <see cref="BeginAirBurst"/> 的口径说明。
        /// </summary>
        private bool airBurstActive;
        /// <summary>本轮 burst 的"真拦截"是否已经打出去；打出去之后的那几发只做视觉连发。</summary>
        private bool airBurstResolved;
        /// <summary>本轮 burst 已定论的拦截点（视觉连发朝这里打）。</summary>
        private Vector3 airBurstAimPosition;
        /// <summary>本轮 burst 开火那一刻的弹道角（连发期间炮塔保持这个朝向）。</summary>
        private float airBurstAimAngle;
        /// <summary>可作战时的防空范围圈颜色（亮绿）。放置预览（<c>PlaceWorker_RukayaTurretRadius</c>）复用它。</summary>
        public static readonly Color AirRangeRingColorActive = new Color(0.25f, 0.95f, 0.35f);
        /// <summary>不可作战时（断电/断料/开关关掉/被屋顶挡住）的防空范围圈颜色（暗绿）。</summary>
        public static readonly Color AirRangeRingColorIdle = new Color(0.30f, 0.45f, 0.32f);
        private static readonly FieldInfo fireAtWillField = typeof(CompTurretGun).GetField("fireAtWill", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly ConditionalWeakTable<Thing, System.WeakReference<CompRukayaSkyInterceptor>> claims = new ConditionalWeakTable<Thing, System.WeakReference<CompRukayaSkyInterceptor>>();
    }
}
