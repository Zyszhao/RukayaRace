using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Thing_RukayaFallingFlyer : ThingWithComps, IThingHolder
    {
        public Thing_RukayaFallingFlyer()
        {
            payload = new ThingOwner<Thing>(this);
        }

        public override Vector3 DrawPos
        {
            get
            {
                float progress = Mathf.Clamp01(1f - (float)remainingTicks / durationTicks);
                return Vector3.Lerp(start,
                    Position.ToVector3Shifted().WithY(AltitudeLayer.Pawn.AltitudeFor()), progress);
            }
        }

        public static bool TryIntercept(PawnFlyer flyer, Thing launcher, RukayaSkyCrashSettings settings,
            Vector3 shotPosition)
        {
            Map map = flyer.Map;
            Vector3 start = flyer.DrawPos;
            IntVec3 original = flyer.Position;
            Vector3 push = (start - shotPosition).Yto0();
            push = push.sqrMagnitude > 0.0001f ? push.normalized : Vector3.forward;
            IntVec3 landing = IntVec3.Invalid;
            for (int distance = 3; distance >= 0; distance--)
            {
                IntVec3 candidate = (original.ToVector3Shifted() + push * distance).ToIntVec3();
                if (candidate.InBounds(map) && candidate.Standable(map))
                {
                    landing = candidate;
                    break;
                }
            }
            if (!landing.IsValid && !CellFinder.TryFindRandomCellNear(original, map, 4,
                cell => cell.InBounds(map) && cell.Standable(map), out landing))
            {
                Log.Error($"[Rukaya] No landing cell for {flyer.def.defName} ({flyer.GetType().FullName}); original flight retained.");
                return false;
            }
            ThingDef carrierDef = DefDatabase<ThingDef>.GetNamed("Rukaya_InterceptedFallingFlyer");
            Thing_RukayaFallingFlyer carrier = (Thing_RukayaFallingFlyer)ThingMaker.MakeThing(carrierDef);
            carrier.Initialize(start, flyer, launcher, settings);
            GenSpawn.Spawn(carrier, landing, map);
            flyer.DeSpawn(DestroyMode.WillReplace);
            if (carrier.payload.TryAdd(flyer, false))
            {
                FleckMaker.ThrowLightningGlow(start, map, 1f);
                return true;
            }
            Log.Error($"[Rukaya] Could not transfer {flyer.def.defName} ({flyer.GetType().FullName}) to falling carrier; original flight restored.");
            GenSpawn.Spawn(flyer, original, map);
            carrier.Destroy(DestroyMode.Vanish);
            return false;
        }

        public void Initialize(Vector3 origin, PawnFlyer flyer, Thing instigator,
            RukayaSkyCrashSettings crashSettings)
        {
            start = origin;
            sourceDef = flyer.def;
            Rotation = flyer.Rotation;
            launcher = instigator;
            settings = crashSettings;
            durationTicks = Math.Max(1, crashSettings.durationTicks);
            remainingTicks = durationTicks;
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return payload;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, payload);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref payload, "payload", this);
            Scribe_Deep.Look(ref settings, "settings");
            Scribe_Defs.Look(ref sourceDef, "sourceDef");
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Values.Look(ref start, "start");
            Scribe_Values.Look(ref durationTicks, "durationTicks");
            Scribe_Values.Look(ref remainingTicks, "remainingTicks");
            Scribe_Values.Look(ref landed, "landed");
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (payload.Count > 0)
            {
                Log.Error($"[Rukaya] Refusing to destroy falling flyer carrier {ThingID} ({sourceDef?.defName}) with {payload.Count} held entries.");
                return;
            }
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            if (remainingTicks > 0)
            {
                remainingTicks--;
                return;
            }
            if (!landed || this.IsHashIntervalTick(60))
            {
                landed = true;
                ReleaseFlyer();
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (shadowMaterial == null)
                shadowMaterial = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent);
            Skyfaller.DrawDropSpotShadow(DrawPos, Rotation, shadowMaterial, Vector2.one, remainingTicks);
            if (payload.Count > 0 && payload[0] is PawnFlyer flyer)
                flyer.FlyingPawn?.DrawNowAt(DrawPos, flip);
        }

        private void ReleaseFlyer()
        {
            if (payload.Count == 0)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }
            if (destinationField == null || respawnMethod == null)
            {
                Log.ErrorOnce("[Rukaya] PawnFlyer landing members were not found; falling flyer remains in its carrier.", 173652141);
                return;
            }
            PawnFlyer flyer = payload[0] as PawnFlyer;
            if (flyer == null)
            {
                Log.Error($"[Rukaya] Falling flyer carrier {ThingID} contains unexpected {payload[0]} from {sourceDef?.defName}.");
                return;
            }
            Pawn pawn = flyer.FlyingPawn;
            if (!payload.TryDrop(flyer, Position, Map, ThingPlaceMode.Direct, out Thing dropped))
            {
                Log.ErrorOnce($"[Rukaya] Could not place {flyer.def.defName} from falling carrier {ThingID}; will retry.", thingIDNumber ^ flyer.thingIDNumber);
                return;
            }
            try
            {
                destinationField.SetValue(flyer, Position);
                respawnMethod.Invoke(flyer, null);
            }
            catch (Exception exception)
            {
                Log.Error($"[Rukaya] Original flyer landing failed for {flyer.def.defName} ({flyer.GetType().FullName}): {exception}");
            }
            if (pawn != null && pawn.Spawned)
            {
                if (flyer.GetDirectlyHeldThings().Count == 0)
                    flyer.Destroy(DestroyMode.Vanish);
                else
                    Log.Error($"[Rukaya] Flyer {flyer.def.defName} landed but retains {flyer.GetDirectlyHeldThings().Count} carried entries; flyer remains spawned.");
                for (int hit = 0; hit < settings.pawnDamageCount && !pawn.Dead && !pawn.Destroyed; hit++)
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, settings.pawnDamageAmount, instigator: launcher));
            }
            else
                Log.Error($"[Rukaya] Pawn from {flyer.def.defName} was not spawned by original landing; flyer remains spawned for retry.");
            Destroy(DestroyMode.Vanish);
        }

        private ThingOwner<Thing> payload;
        private RukayaSkyCrashSettings settings;
        private ThingDef sourceDef;
        private Thing launcher;
        private Vector3 start;
        private int durationTicks;
        private int remainingTicks;
        private bool landed;
        private Material shadowMaterial;
        private static readonly FieldInfo destinationField = typeof(PawnFlyer).GetField("destCell", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo respawnMethod = typeof(PawnFlyer).GetMethod("RespawnPawn", BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
