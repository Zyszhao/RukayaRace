using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Thing_RukayaAirborneDebris : ThingWithComps
    {
        public override Vector3 DrawPos => position;

        public static bool IsFlyingProjectile(Projectile projectile)
        {
            if (ticksToImpactField == null || landedField == null)
            {
                Log.ErrorOnce("[Rukaya] Projectile flight state fields were not found; projectile interception is disabled.", 173652142);
                return false;
            }
            return projectile != null && !(bool)landedField.GetValue(projectile)
                && (int)ticksToImpactField.GetValue(projectile) > 0;
        }

        /// <summary>
        /// 目标弹丸还剩几 tick 落地。用于给"拦截弹必须在目标落地前把结果播出来"定一个期限
        /// （见 <see cref="CompRukayaSkyInterceptor.FireAirShot"/>）。取不到时返回 -1。
        /// </summary>
        public static int RemainingFlightTicks(Projectile projectile)
        {
            if (ticksToImpactField == null || projectile == null)
                return -1;
            return (int)ticksToImpactField.GetValue(projectile);
        }

        public static bool TryIntercept(Projectile source, Vector3 shotPosition)
        {
            Map map = source.Map;
            ThingDef debrisDef = DefDatabase<ThingDef>.GetNamed("Rukaya_InterceptedAirborneDebris");
            Thing_RukayaAirborneDebris debris = (Thing_RukayaAirborneDebris)ThingMaker.MakeThing(debrisDef);
            debris.Initialize(source, shotPosition);
            GenSpawn.Spawn(debris, source.Position, map);
            source.Destroy(DestroyMode.Vanish);
            if (!source.Destroyed)
            {
                Log.Error($"[Rukaya] Intercepted projectile {source.def.defName} ({source.GetType().FullName}) could not be destroyed.");
                debris.Destroy(DestroyMode.Vanish);
                return false;
            }
            FleckMaker.ThrowLightningGlow(debris.DrawPos, map, 1f);
            return true;
        }

        public void Initialize(Projectile source, Vector3 shotPosition)
        {
            projectileDef = source.def;
            weaponDef = source.EquipmentDef;
            launcher = source.Launcher;
            start = source.DrawPos;
            position = start;
            Vector3 push = (start - shotPosition).Yto0();
            if (push.sqrMagnitude < 0.0001f && launcher != null)
                push = (start - launcher.DrawPos).Yto0();
            push = push.sqrMagnitude > 0.0001f ? push.normalized : Vector3.forward;
            end = start;
            for (int distance = 4; distance >= 0; distance--)
            {
                Vector3 candidate = start + push * distance;
                if (!candidate.ToIntVec3().InBounds(source.Map))
                    continue;
                end = candidate;
                break;
            }
            angle = (end - start).AngleFlat();
            damageDef = source.DamageDef;
            damageAmount = source.DamageAmount;
            armorPenetration = source.ArmorPenetration;
            if (source is Projectile_Explosive && projectileDef.projectile.explosionRadius > 0f && damageDef == null)
                Log.Error($"[Rukaya] Explosive projectile {projectileDef.defName} has no damage Def; intercepted debris will be destroyed without detonation.");
            if (projectileDef.graphicData == null)
                Log.Error($"[Rukaya] Intercepted projectile {projectileDef.defName} has no graphicData; airborne debris will be invisible.");
            explode = source is Projectile_Explosive && projectileDef.projectile.explosionRadius > 0f && damageDef != null;
            totalTicks = Mathf.Clamp((int)ticksToImpactField.GetValue(source) - 1, 1, 12);
            remainingTicks = totalTicks;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref projectileDef, "projectileDef");
            Scribe_Defs.Look(ref weaponDef, "weaponDef");
            Scribe_Defs.Look(ref damageDef, "damageDef");
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Values.Look(ref start, "start");
            Scribe_Values.Look(ref end, "end");
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref angle, "angle");
            Scribe_Values.Look(ref damageAmount, "damageAmount");
            Scribe_Values.Look(ref armorPenetration, "armorPenetration");
            Scribe_Values.Look(ref totalTicks, "totalTicks");
            Scribe_Values.Look(ref remainingTicks, "remainingTicks");
            Scribe_Values.Look(ref explode, "explode");
        }

        protected override void Tick()
        {
            base.Tick();
            if (projectileDef?.projectile == null)
            {
                Log.Error($"[Rukaya] Intercepted debris {ThingID} lost its projectile Def; debris will be destroyed.");
                Destroy(DestroyMode.Vanish);
                return;
            }
            if (remainingTicks > 0)
            {
                remainingTicks--;
                position = Vector3.Lerp(start, end, 1f - (float)remainingTicks / totalTicks);
                if (position.ToIntVec3().InBounds(Map))
                    Position = position.ToIntVec3();
                return;
            }
            Map map = Map;
            IntVec3 impactCell = Position;
            ProjectileProperties properties = projectileDef?.projectile;
            Destroy(DestroyMode.Vanish);
            if (!explode || properties == null || damageDef == null)
                return;
            GenExplosion.DoExplosion(impactCell, map, properties.explosionRadius, damageDef, launcher,
                damAmount: damageAmount, armorPenetration: armorPenetration,
                explosionSound: properties.soundExplode, weapon: weaponDef, projectile: projectileDef,
                postExplosionSpawnThingDef: properties.postExplosionSpawnThingDef,
                postExplosionSpawnChance: properties.postExplosionSpawnChance,
                postExplosionSpawnThingCount: properties.postExplosionSpawnThingCount,
                postExplosionGasType: properties.postExplosionGasType,
                applyDamageToExplosionCellsNeighbors: properties.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: properties.preExplosionSpawnThingDef,
                preExplosionSpawnChance: properties.preExplosionSpawnChance,
                preExplosionSpawnThingCount: properties.preExplosionSpawnThingCount,
                chanceToStartFire: properties.explosionChanceToStartFire,
                damageFalloff: properties.explosionDamageFalloff,
                doVisualEffects: properties.doExplosionVFX,
                propagationSpeed: damageDef.expolosionPropagationSpeed,
                postExplosionSpawnThingDefWater: properties.postExplosionSpawnThingDefWater,
                screenShakeFactor: properties.screenShakeFactor,
                postExplosionSpawnSingleThingDef: properties.postExplosionSpawnSingleThingDef,
                preExplosionSpawnSingleThingDef: properties.preExplosionSpawnSingleThingDef);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            projectileDef?.graphicData?.Graphic.DrawWorker(DrawPos, Rot4.North, projectileDef, this, angle);
        }

        private ThingDef projectileDef;
        private ThingDef weaponDef;
        private DamageDef damageDef;
        private Thing launcher;
        private Vector3 start;
        private Vector3 end;
        private Vector3 position;
        private float angle;
        private int damageAmount;
        private float armorPenetration;
        private int totalTicks;
        private int remainingTicks;
        private bool explode;
        private static readonly FieldInfo ticksToImpactField = typeof(Projectile).GetField("ticksToImpact", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo landedField = typeof(Projectile).GetField("landed", BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
