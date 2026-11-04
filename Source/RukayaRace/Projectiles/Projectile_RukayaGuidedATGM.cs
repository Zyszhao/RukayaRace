using RimWorld;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaGuidedATGM : Projectile_Explosive
    {
        // 离屏也逐 tick 修正制导，避免原版 15 tick 批处理跳过转向。
        public override int UpdateRateTicks => 1;
        private Pawn guidingPawn;
        private LocalTargetInfo guidanceTarget;
        private bool guidanceFailed;
        private bool guidanceHediffApplied;
        private bool radarLockActive;
        private IntVec3 missedImpactCell = IntVec3.Invalid;
        /// <summary>高抛抬升高度（格），来自扩展；0 = 平飞。只影响绘制，不影响逻辑坐标。</summary>
        private float loftHeight;
        /// <summary>已飞行 tick 数 / 起飞时估算的总飞行 tick 数（只用于高抛弧线的进度）。</summary>
        private int flightTicks;
        private int flightTicksTotal;

        public override Quaternion ExactRotation
        {
            get
            {
                Vector3 direction = (destination - origin).Yto0();
                if (direction.sqrMagnitude < 0.000001f)
                    return base.ExactRotation;

                // The loft is projected onto screen-forward in DrawPosition. Orient
                // the sprite along that visible curve, including ascent and descent.
                if (loftHeight > 0f && flightTicksTotal > 0 && !landed)
                {
                    float progress = Mathf.Clamp01((float)flightTicks / flightTicksTotal);
                    direction = direction.normalized * def.projectile.SpeedTilesPerTick
                        + Vector3.forward * (4f * loftHeight * (1f - 2f * progress) / flightTicksTotal);
                }
                return direction.sqrMagnitude > 0.000001f
                    ? Quaternion.LookRotation(direction) : base.ExactRotation;
            }
        }

        public override int DamageAmount
        {
            get
            {
                RukayaGuidedATGMProjectileExtension extension = this.Extension;
                if (this.radarLockActive && extension.radarLockDamageAmount >= 0)
                {
                    return extension.radarLockDamageAmount;
                }

                return base.DamageAmount;
            }
        }

        public override float ArmorPenetration
        {
            get
            {
                RukayaGuidedATGMProjectileExtension extension = this.Extension;
                if (this.radarLockActive && extension.radarLockArmorPenetration > base.ArmorPenetration)
                {
                    return extension.radarLockArmorPenetration;
                }

                return base.ArmorPenetration;
            }
        }

        /// <summary>
        /// 高抛混合弹道（机械体导弹型）：水平方向照旧由制导律负责，这里只在**绘制**阶段把弹体抬起来，
        /// 让它在半程爬到 <see cref="RukayaGuidedATGMProjectileExtension.loftHeight"/> 格高、再压回地面。
        ///
        /// 逻辑坐标（<see cref="Projectile.ExactPosition"/>，也是制导每 tick 重设 origin 用的那个）
        /// 恒为 <c>origin.Yto0() + 水平插值 + up * def.Altitude</c>（原版源码），所以：
        /// * 制导算距离时永远不会把视觉高度算进去；
        /// * 落点、爆炸格、拦截判定全都还是地面上的位置。
        /// 原版的抛物线也是这么做的（<c>ArcHeightFactor * InverseParabola</c> 只进绘制），
        /// 这里把高度投到可见位置 <see cref="DrawPosition"/>，并**关掉原版 arcHeightFactor**，
        /// 避免两条弧线叠加。
        /// </summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(this.DrawPosition, flip);
        }

        /// <summary>弹体的可见位置（= 逻辑位置 + 高抛高度）。烟迹、火花、光晕都用它，才跟得住弹体。</summary>
        private Vector3 DrawPosition
        {
            get
            {
                Vector3 position = base.ExactPosition;
                if (this.loftHeight <= 0f || this.flightTicksTotal <= 0)
                {
                    return position;
                }

                float progress = Mathf.Clamp01((float)this.flightTicks / this.flightTicksTotal);
                return position + Vector3.forward * (this.loftHeight * GenMath.InverseParabola(progress));
            }
        }

        public override void Launch(
            Thing launcher,
            Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire = false,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            this.guidingPawn = launcher as Pawn;
            this.guidanceTarget = intendedTarget.IsValid ? intendedTarget : usedTarget;
            this.loftHeight = this.Extension.loftHeight;
            this.flightTicks = 0;
            this.flightTicksTotal = this.loftHeight > 0f
                ? Mathf.Max(1, Mathf.RoundToInt(this.StartingTicksToImpact))
                : 0;
            this.UpdateRadarLockState();
            this.TryApplyGuidanceHediff();
            this.TryGuideTowardTrueTarget(true);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref this.guidingPawn, "guidingPawn");
            Scribe_TargetInfo.Look(ref this.guidanceTarget, "guidanceTarget");
            Scribe_Values.Look(ref this.guidanceFailed, "guidanceFailed", false);
            Scribe_Values.Look(ref this.guidanceHediffApplied, "guidanceHediffApplied", false);
            Scribe_Values.Look(ref this.radarLockActive, "radarLockActive", false);
            Scribe_Values.Look(ref this.missedImpactCell, "missedImpactCell", IntVec3.Invalid);
            Scribe_Values.Look(ref this.loftHeight, "rukayaLoftHeight");
            Scribe_Values.Look(ref this.flightTicks, "rukayaFlightTicks");
            Scribe_Values.Look(ref this.flightTicksTotal, "rukayaFlightTicksTotal");
        }

        protected override void TickInterval(int delta)
        {
            if (this.Spawned && !this.landed && this.Map != null)
            {
                this.flightTicks += delta;
                this.UpdateRadarLockState();

                if (!this.guidanceFailed && !this.TryGuideTowardTrueTarget(false))
                {
                    this.FailGuidance();
                }

                this.ThrowGuidanceVisuals(delta);
            }

            base.TickInterval(delta);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            this.CleanupGuidanceHediff();
            base.Impact(hitThing, blockedByShield);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            this.CleanupGuidanceHediff();
            base.Destroy(mode);
        }

        /// <summary>
        /// 将逻辑轨迹重新指向目标当前位置；视觉高度不会参与距离计算。
        /// </summary>
        private bool TryGuideTowardTrueTarget(bool force)
        {
            if (this.guidanceFailed || !this.CanContinueGuidance())
            {
                return false;
            }

            Vector3 currentPosition = this.ExactPosition.Yto0();
            Vector3 targetPosition = this.GetCurrentTargetPosition().Yto0();
            if (!force && (targetPosition - currentPosition).sqrMagnitude <= this.Extension.guidedHitRadius * this.Extension.guidedHitRadius)
            {
                this.destination = targetPosition;
                this.ticksToImpact = 1;
                return true;
            }

            this.origin = currentPosition;
            this.destination = targetPosition;
            this.usedTarget = this.guidanceTarget;
            this.intendedTarget = this.guidanceTarget;
            this.HitFlags = ProjectileHitFlags.IntendedTarget;
            this.ticksToImpact = Mathf.Max(1, Mathf.RoundToInt(this.StartingTicksToImpact));
            return true;
        }

        private void FailGuidance()
        {
            this.guidanceFailed = true;
            this.HitFlags = ProjectileHitFlags.All;

            Vector3 currentPosition = this.ExactPosition;
            IntVec3 targetCell = this.GetCurrentTargetCell();
            this.missedImpactCell = this.FindMissedImpactCell(targetCell);
            this.origin = currentPosition;
            this.destination = this.missedImpactCell.ToVector3Shifted();
            this.usedTarget = this.missedImpactCell;
            this.ticksToImpact = Mathf.Max(1, Mathf.RoundToInt(this.StartingTicksToImpact));
        }

        private bool CanContinueGuidance()
        {
            if (this.guidingPawn == null || this.guidingPawn.Destroyed || this.guidingPawn.Dead || this.guidingPawn.Map != this.Map)
            {
                return false;
            }

            IntVec3 targetCell = this.GetCurrentTargetCell();
            if (!targetCell.IsValid || !targetCell.InBounds(this.Map))
            {
                return false;
            }

            RukayaGuidedATGMProjectileExtension extension = this.Extension;
            if (extension.requireLineOfSight && !this.HasGuidanceLineOfSight(targetCell))
            {
                return false;
            }

            if (extension.smokeBreaksGuidance && this.HasGasInterference(this.guidingPawn.Position, targetCell))
            {
                return false;
            }

            return true;
        }

        private bool HasGuidanceLineOfSight(IntVec3 targetCell)
        {
            Thing targetThing = this.guidanceTarget.Thing;
            if (targetThing != null && targetThing.Spawned && targetThing.Map == this.guidingPawn.Map)
            {
                return GenSight.LineOfSightToThing(this.guidingPawn.Position, targetThing, this.guidingPawn.Map);
            }

            return GenSight.LineOfSight(this.guidingPawn.Position, targetCell, this.guidingPawn.Map, true, null, 0, 0);
        }

        private bool HasGasInterference(IntVec3 start, IntVec3 end)
        {
            if (this.Map == null)
            {
                return false;
            }

            bool foundGas = false;
            GenSight.PointsOnLineOfSight(start, end, cell =>
            {
                if (cell.InBounds(this.Map) && GasUtility.AnyGas(cell, this.Map, GasType.BlindSmoke))
                {
                    foundGas = true;
                }
            });

            return foundGas;
        }

        private Vector3 GetCurrentTargetPosition()
        {
            Thing targetThing = this.guidanceTarget.Thing;
            if (targetThing != null && targetThing.Spawned)
            {
                return targetThing.TrueCenter();
            }

            return this.GetCurrentTargetCell().ToVector3Shifted();
        }

        private IntVec3 GetCurrentTargetCell()
        {
            Thing targetThing = this.guidanceTarget.Thing;
            if (targetThing != null && targetThing.Spawned)
            {
                return targetThing.Position;
            }

            if (this.guidanceTarget.Cell.IsValid)
            {
                return this.guidanceTarget.Cell;
            }

            if (this.intendedTarget.Cell.IsValid)
            {
                return this.intendedTarget.Cell;
            }

            return this.DestinationCell;
        }

        private IntVec3 FindMissedImpactCell(IntVec3 targetCell)
        {
            RukayaGuidedATGMProjectileExtension extension = this.Extension;
            Map map = this.Map;
            if (map == null || !targetCell.IsValid)
            {
                return this.DestinationCell;
            }

            for (int i = 0; i < 24; i++)
            {
                IntVec3 cell = targetCell + GenRadial.RadialPattern.RandomElementByWeight(c => Mathf.Max(0.001f, c.LengthHorizontal));
                float distance = cell.DistanceTo(targetCell);
                if (distance < extension.missedImpactRadius || distance > extension.missDeviationRadius)
                {
                    continue;
                }

                if (cell.InBounds(map) && cell.Standable(map))
                {
                    return cell;
                }
            }

            return CellFinder.RandomClosewalkCellNear(targetCell, map, Mathf.CeilToInt(extension.missDeviationRadius));
        }

        private void UpdateRadarLockState()
        {
            Pawn targetPawn = this.guidanceTarget.Thing as Pawn;
            HediffDef lockHediff = this.Extension.radarLockHediff ?? RukayaRadarDefOf.Rukaya_RadarLockOn;
            this.radarLockActive = targetPawn?.health?.hediffSet?.HasHediff(lockHediff) == true;
        }

        private void TryApplyGuidanceHediff()
        {
            HediffDef hediffDef = this.Extension.guidingHediff ?? RukayaRadarDefOf.Rukaya_ATGMGuiding;
            if (this.guidingPawn?.health == null || hediffDef == null)
            {
                return;
            }

            if (!this.guidingPawn.health.hediffSet.HasHediff(hediffDef))
            {
                this.guidingPawn.health.AddHediff(hediffDef);
            }

            this.guidanceHediffApplied = true;
        }

        private void CleanupGuidanceHediff()
        {
            if (!this.guidanceHediffApplied || this.guidingPawn?.health?.hediffSet == null)
            {
                return;
            }

            this.guidanceHediffApplied = false;
            // 同一个发射者可以同时有好几枚在飞（机械体导弹型一轮 6 发齐射，8 tick 一发）。
            // 首弹落地时后面 5 枚还在天上，此时把线导 hediff 摘掉等于提前解除它们的制导束缚（移动/后坐）。
            // 只有"同一个发射者、同一个 hediff 的最后一枚"才真正摘除。
            if (this.HasOtherGuidedMissileInFlight())
            {
                return;
            }

            HediffDef hediffDef = this.Extension.guidingHediff ?? RukayaRadarDefOf.Rukaya_ATGMGuiding;
            Hediff hediff = this.guidingPawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff != null)
            {
                this.guidingPawn.health.RemoveHediff(hediff);
            }
        }

        /// <summary>同一个发射者身上、挂了同一个 guidingHediff 的其它导弹是否还在飞。</summary>
        private bool HasOtherGuidedMissileInFlight()
        {
            if (this.Map == null) return false;
            foreach (Thing thing in this.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                if (!(thing is Projectile_RukayaGuidedATGM other) || other == this || other.Destroyed || !other.Spawned)
                {
                    continue;
                }

                if (other.guidanceHediffApplied && other.guidingPawn == this.guidingPawn
                    && (other.Extension.guidingHediff ?? RukayaRadarDefOf.Rukaya_ATGMGuiding)
                        == (this.Extension.guidingHediff ?? RukayaRadarDefOf.Rukaya_ATGMGuiding))
                {
                    return true;
                }
            }

            return false;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            this.CleanupGuidanceHediff();
            base.DeSpawn(mode);
        }

        private void ThrowGuidanceVisuals(int delta)
        {
            RukayaGuidedATGMProjectileExtension extension = this.Extension;
            // 与原版一致的取模判断；delta 恒为 1 时每个间隔恰好抛一次。
            if (extension.guidanceSparkIntervalTicks > 0
                && this.HashOffsetTicks() % extension.guidanceSparkIntervalTicks < delta)
            {
                FleckMaker.ThrowMicroSparks(this.DrawPosition, this.Map);
            }

            if (extension.guidanceGlowSize > 0f
                && extension.guidanceGlowIntervalTicks > 0
                && this.HashOffsetTicks() % extension.guidanceGlowIntervalTicks < delta)
            {
                this.ThrowGuidanceGlow(extension);
            }
        }

        private void ThrowGuidanceGlow(RukayaGuidedATGMProjectileExtension extension)
        {
            Vector3 drawPosition = this.DrawPosition;
            FleckDef glowDef = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
            if (glowDef == null)
            {
                FleckMaker.ThrowLightningGlow(drawPosition, this.Map, extension.guidanceGlowSize);
                return;
            }

            FleckCreationData data = FleckMaker.GetDataStatic(drawPosition, this.Map, glowDef, extension.guidanceGlowSize);
            data.instanceColor = extension.guidanceGlowColor;
            this.Map.flecks.CreateFleck(data);
        }

        private RukayaGuidedATGMProjectileExtension Extension =>
            this.def.GetModExtension<RukayaGuidedATGMProjectileExtension>() ?? new RukayaGuidedATGMProjectileExtension();
    }
}
