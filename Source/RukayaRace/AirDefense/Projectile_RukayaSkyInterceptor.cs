using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using RukayaRace.Projectiles;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Projectile_RukayaSkyInterceptor : Projectile
    {
        public override Vector3 ExactPosition => position;
        public override Quaternion ExactRotation => Quaternion.AngleAxis(angle, Vector3.up);
        public override int UpdateRateTicks => 1;

        public static bool HasShotFor(Thing skyTarget)
        {
            if (skyTarget == null || !shots.TryGetValue(skyTarget, out List<System.WeakReference<Projectile_RukayaSkyInterceptor>> references))
                return false;
            for (int i = references.Count - 1; i >= 0; i--)
            {
                if (!references[i].TryGetTarget(out Projectile_RukayaSkyInterceptor shot)
                    || !shot.Spawned || shot.Destroyed || shot.target != skyTarget)
                    references.RemoveAt(i);
            }
            if (references.Count > 0)
                return true;
            shots.Remove(skyTarget);
            return false;
        }

        /// <summary>
        /// 拦截在开火那一刻就已经定论（见 <see cref="CompRukayaSkyInterceptor.FireAirShot"/>），
        /// 本弹体只负责把这段飞行**播出来**。所以：
        /// * 飞行时间在这里定死 = <c>travelTicks</c>，速度反推成"距离 / travelTicks"：
        ///   近目标也不会一闪而过（有 <see cref="MinTravelTicks"/> 下限），
        ///   远目标也不会拖到目标先落地（受 <paramref name="resolveDeadlineTicks"/> 上限约束）。
        /// * <paramref name="resolveDeadlineTicks"/> &gt; 0 是硬期限：到点就地结算，
        ///   "追不上"不再是失败路径。传 0 表示目标没有明确的下落期限（例如 PawnFlyer），只按接触结算。
        /// * 若 <paramref name="visualProjectile"/> 挂着 <see cref="RukayaGuidedATGMProjectileExtension"/>，
        ///   本弹体改走**本模组已有的导引律**（纯追踪，见 <see cref="RukayaGuidance"/>）：
        ///   每 tick 朝目标当前位置重算航向，进到 guidedHitRadius 以内就收尾。
        ///   对空拦截在开火时就已定论，所以导引律里的 requireLineOfSight / smokeBreaksGuidance
        ///   在这里**不生效**（那两条只决定"能不能开火"），弹体不会半路被判失的。
        /// </summary>
        public void Initialize(Thing skyTarget, ThingDef visualProjectile, Faction firingFaction,
            RukayaSkyCrashSettings crashSettings, int resolveDeadlineTicks = 0)
        {
            target = skyTarget;
            visualDef = visualProjectile;
            faction = firingFaction;
            settings = crashSettings;
            ResolveGuidanceLaw();
            position = origin.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            previousTargetPosition = target.DrawPos.WithY(position.y);
            float nominalSpeed = Mathf.Max(0.01f, visualDef.projectile.SpeedTilesPerTick);
            float distance = Vector3.Distance(position, previousTargetPosition);
            int minTravelTicks = guided ? MinGuidedTravelTicks : MinTravelTicks;
            int travelTicks = Mathf.Max(minTravelTicks, Mathf.CeilToInt(distance / nominalSpeed));
            if (resolveDeadlineTicks > 0 && travelTicks > resolveDeadlineTicks)
                travelTicks = Mathf.Max(1, resolveDeadlineTicks);
            ticksLeftToResolve = travelTicks;
            speed = Mathf.Max(distance / travelTicks, 0.01f);
            angle = (previousTargetPosition - position).AngleFlat();
            RegisterShot();
        }

        /// <summary>
        /// 纯视觉拦截弹：一轮对空 burst 里"真拦截那一发已经打出去"之后的几发
        /// （机炮型的曳光弹连发，见 <see cref="CompRukayaSkyInterceptor.FireAirShot"/>）。
        /// 它只把弹道播到**已定论的拦截点**，不注册 claim、不结算任何东西，播完就消失。
        /// </summary>
        public void InitializeVisual(Vector3 aimPosition, ThingDef visualProjectile)
        {
            target = null;
            settings = null;
            visualOnly = true;
            visualDef = visualProjectile;
            ResolveGuidanceLaw();
            position = origin.WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            visualAimPosition = aimPosition.WithY(position.y);
            float nominalSpeed = Mathf.Max(0.01f, visualDef.projectile.SpeedTilesPerTick);
            float distance = Vector3.Distance(position, visualAimPosition);
            int travelTicks = Mathf.Max(MinVisualTravelTicks, Mathf.CeilToInt(distance / nominalSpeed));
            ticksLeftToResolve = travelTicks;
            speed = Mathf.Max(distance / travelTicks, 0.01f);
            angle = (visualAimPosition - position).AngleFlat();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            ResolveGuidanceLaw();
            RegisterShot();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref target, "rukayaSkyTarget");
            Scribe_References.Look(ref faction, "rukayaFiringFaction");
            Scribe_Defs.Look(ref visualDef, "rukayaVisualProjectile");
            Scribe_Deep.Look(ref settings, "rukayaCrashSettings");
            Scribe_Values.Look(ref position, "rukayaAirPosition");
            Scribe_Values.Look(ref previousTargetPosition, "rukayaPreviousTargetPosition");
            Scribe_Values.Look(ref speed, "rukayaAirSpeed");
            Scribe_Values.Look(ref angle, "rukayaAirAngle");
            Scribe_Values.Look(ref ticksLeftToResolve, "rukayaTicksLeftToResolve");
            Scribe_Values.Look(ref visualOnly, "rukayaVisualOnly");
            Scribe_Values.Look(ref visualAimPosition, "rukayaVisualAimPosition");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResolveGuidanceLaw();
                RegisterShot();
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (target != null && shots.TryGetValue(target, out List<System.WeakReference<Projectile_RukayaSkyInterceptor>> references))
            {
                for (int i = references.Count - 1; i >= 0; i--)
                {
                    if (!references[i].TryGetTarget(out Projectile_RukayaSkyInterceptor shot) || shot == this)
                        references.RemoveAt(i);
                }
                if (references.Count == 0)
                    shots.Remove(target);
            }
            base.Destroy(mode);
        }

        protected override void TickInterval(int delta)
        {
            if (visualDef?.projectile == null)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }
            if (visualOnly)
            {
                TickVisualOnly(delta);
                return;
            }
            // 目标已经不存在（被别人清掉 / 自己落地消失）→ 没东西可拦，安静退场。
            // 注意这里**不再**因为"追不上"或"目标不再满足 CanTarget"而退场：
            // 拦截结果在开火那一刻已定论，弹体只负责把动画播完，播完就地结算。
            if (settings == null || target == null || target.Destroyed || !target.Spawned || target.Map != Map)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }
            for (int i = 0; i < delta; i++)
            {
                Vector3 targetPosition = target.DrawPos.WithY(position.y);
                Vector3 previousPosition = position;
                bool hit;
                if (guided)
                {
                    // 本模组已有的导引律：每 tick 朝目标**当前位置**重算航向（纯追踪）。
                    hit = RukayaGuidance.StepToward(ref position, targetPosition, speed, guidedHitRadius, out angle);
                }
                else
                {
                    Vector3 movement = AimPosition(targetPosition) - position;
                    float distance = movement.magnitude;
                    position += distance > speed ? movement * (speed / distance) : movement;
                    angle = movement.AngleFlat();
                    hit = SegmentTouches(previousPosition - previousTargetPosition, position - targetPosition, 0.35f);
                }
                previousTargetPosition = targetPosition;
                if (position.InBounds(Map))
                    Position = position.ToIntVec3();
                // 飞行烟迹：拦截弹很快，没有烟迹时几乎看不清（这正是之前"看不到飞行过程"的原因之一）。
                // 每 tick 丢一朵小烟，配合弹体速度就能看出一道弹道。
                FleckMaker.ThrowSmoke(position, Map, guided ? 0.6f : 0.5f);
                ThrowGuidanceVisuals();
                if (ticksLeftToResolve > 0)
                    ticksLeftToResolve--;
                // 接触即结算；到期限也要结算——这就是"动画结束 ≠ 拦截成功前提"的那一点。
                if (!hit && ticksLeftToResolve > 0)
                    continue;
                RukayaAirImpact.TryIntercept(target, launcher, faction, settings, previousPosition, alreadyCommitted: true);
                Destroy(DestroyMode.Vanish);
                return;
            }
        }

        /// <summary>纯视觉连发的那几发：飞到拦截点就消失，不结算、不 claim。</summary>
        private void TickVisualOnly(int delta)
        {
            for (int i = 0; i < delta; i++)
            {
                Vector3 movement = visualAimPosition - position;
                float distance = movement.magnitude;
                position += distance > speed ? movement * (speed / distance) : movement;
                if (distance > 0.0001f)
                    angle = movement.AngleFlat();
                if (position.InBounds(Map))
                    Position = position.ToIntVec3();
                FleckMaker.ThrowSmoke(position, Map, 0.4f);
                if (ticksLeftToResolve > 0)
                    ticksLeftToResolve--;
                if (distance <= speed || ticksLeftToResolve <= 0)
                {
                    FleckMaker.ThrowLightningGlow(position, Map, 0.5f);
                    Destroy(DestroyMode.Vanish);
                    return;
                }
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (visualDef?.graphicData == null)
                return;
            // Match vanilla projectile rendering: apply the flight rotation directly
            // to the mesh rather than relying on the generic Graphic draw path.
            Graphics.DrawMesh(MeshPool.GridPlane(visualDef.graphicData.drawSize), position,
                ExactRotation, visualDef.graphicData.Graphic.MatSingle, 0);
        }

        /// <summary>
        /// 导引律标记：模板 Def 上挂着 <see cref="RukayaGuidedATGMProjectileExtension"/>
        /// 就走纯追踪；没挂就沿用原来的"提前量追击"。
        /// </summary>
        private void ResolveGuidanceLaw()
        {
            guidanceExtension = visualDef?.GetModExtension<RukayaGuidedATGMProjectileExtension>();
            guided = guidanceExtension != null;
            guidedHitRadius = guided ? Mathf.Max(0.1f, guidanceExtension.guidedHitRadius) : 0f;
        }

        /// <summary>
        /// 制导弹体的光点/火花（参数与 ATGM 同一套 DefModExtension 字段），
        /// 让"它在制导"这件事在画面上一眼能看出来。
        /// </summary>
        private void ThrowGuidanceVisuals()
        {
            if (!guided)
                return;
            int hash = this.HashOffsetTicks();
            if (guidanceExtension.guidanceSparkIntervalTicks > 0
                && hash % guidanceExtension.guidanceSparkIntervalTicks == 0)
                FleckMaker.ThrowMicroSparks(ExactPosition, Map);
            if (guidanceExtension.guidanceGlowSize <= 0f || guidanceExtension.guidanceGlowIntervalTicks <= 0
                || hash % guidanceExtension.guidanceGlowIntervalTicks != 0)
                return;
            FleckDef glowDef = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
            if (glowDef == null)
            {
                FleckMaker.ThrowLightningGlow(ExactPosition, Map, guidanceExtension.guidanceGlowSize);
                return;
            }
            FleckCreationData data = FleckMaker.GetDataStatic(ExactPosition, Map, glowDef, guidanceExtension.guidanceGlowSize);
            data.instanceColor = guidanceExtension.guidanceGlowColor;
            Map.flecks.CreateFleck(data);
        }

        private Vector3 AimPosition(Vector3 currentTargetPosition)
        {
            Vector3 velocity = currentTargetPosition - previousTargetPosition;
            if (velocity.sqrMagnitude < 0.0001f)
                return currentTargetPosition;
            float limit = target is Skyfaller skyfaller
                ? Mathf.Max(0f, skyfaller.ticksToImpact - 1f) : 30f;
            float leadTicks = Mathf.Min(Vector3.Distance(position, currentTargetPosition) / speed, limit);
            for (int i = 0; i < 3; i++)
                leadTicks = Mathf.Min(Vector3.Distance(position, currentTargetPosition + velocity * leadTicks) / speed, limit);
            return currentTargetPosition + velocity * leadTicks;
        }

        private void RegisterShot()
        {
            if (target == null || visualOnly)
                return;
            if (!shots.TryGetValue(target, out List<System.WeakReference<Projectile_RukayaSkyInterceptor>> references))
            {
                references = new List<System.WeakReference<Projectile_RukayaSkyInterceptor>>();
                shots.Add(target, references);
            }
            foreach (System.WeakReference<Projectile_RukayaSkyInterceptor> reference in references)
            {
                if (reference.TryGetTarget(out Projectile_RukayaSkyInterceptor shot) && shot == this)
                    return;
            }
            references.Add(new System.WeakReference<Projectile_RukayaSkyInterceptor>(this));
        }

        private static bool SegmentTouches(Vector3 relativeStart, Vector3 relativeEnd, float radius)
        {
            Vector3 direction = relativeEnd - relativeStart;
            float squaredLength = direction.sqrMagnitude;
            float fraction = squaredLength > 0.000001f
                ? Mathf.Clamp01(-Vector3.Dot(relativeStart, direction) / squaredLength)
                : 0f;
            return (relativeStart + direction * fraction).sqrMagnitude <= radius * radius;
        }

        /// <summary>可见弹道的最短飞行时间（tick）：20 tick ≈ 0.33 秒，配合烟迹足够看清一次拦截。</summary>
        private const int MinTravelTicks = 20;
        /// <summary>制导弹体的最短飞行时间：导引律本身要求"看得到它在修正"，10 tick 够看清一段弧线。</summary>
        private const int MinGuidedTravelTicks = 10;
        /// <summary>视觉连发曳光弹的最短飞行时间。</summary>
        private const int MinVisualTravelTicks = 12;
        private Thing target;
        private Faction faction;
        private ThingDef visualDef;
        private RukayaSkyCrashSettings settings;
        private Vector3 position;
        private Vector3 previousTargetPosition;
        private float speed;
        private float angle;
        /// <summary>剩余飞行时间。减到 0 时无论有没有接触到目标都就地结算——动画不构成拦截成功的前提。</summary>
        private int ticksLeftToResolve;
        /// <summary>本轮 burst 的"后续视觉发"：只播弹道，不结算、不 claim。</summary>
        private bool visualOnly;
        private Vector3 visualAimPosition;
        /// <summary>模板 Def 上挂了 <see cref="RukayaGuidedATGMProjectileExtension"/> 时走导引律。</summary>
        private bool guided;
        private float guidedHitRadius;
        private RukayaGuidedATGMProjectileExtension guidanceExtension;
        private static readonly ConditionalWeakTable<Thing, List<System.WeakReference<Projectile_RukayaSkyInterceptor>>> shots = new ConditionalWeakTable<Thing, List<System.WeakReference<Projectile_RukayaSkyInterceptor>>>();
    }
}
