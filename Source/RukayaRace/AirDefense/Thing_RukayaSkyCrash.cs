using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Thing_RukayaSkyCrash : ThingWithComps, IThingHolder
    {
        public Thing_RukayaSkyCrash()
        {
            payload = new ThingOwner<Thing>(this);
        }

        public override Vector3 DrawPos
        {
            get
            {
                float progress = Mathf.Clamp01(1f - (float)remainingTicks / settings.durationTicks);
                return Vector3.Lerp(start, Position.ToVector3Shifted(), progress)
                    .WithY(AltitudeLayer.Skyfaller.AltitudeFor());
            }
        }

        /// <summary>
        /// 把一个非自然天降物（空投仓等）换成"被击落的坠毁体"。
        ///
        /// <paramref name="alreadyValidated"/> = true 表示拦截在开火那一刻就已定论、
        /// 上游已经校验过目标合法性（见 <see cref="RukayaAirImpact.TryIntercept"/> 的 alreadyCommitted），
        /// 这里不再重复校验——否则拦截弹飞到跟前时目标状态稍有变化就会让这次拦截失效。
        /// </summary>
        public static bool TryIntercept(Skyfaller target, Thing instigator, Faction faction,
            RukayaSkyCrashSettings crashSettings, bool alreadyValidated = false)
        {
            Map map = target?.Map;
            if (map == null || (!alreadyValidated && !CompRukayaSkyInterceptor.CanTarget(target, faction, map)))
                return false;
            IntVec3 landing = FindLandingCell(target.Position, map, crashSettings.offsetRadius);
            if (!landing.IsValid)
            {
                Log.Error($"[Rukaya] No safe crash cell for {target.def.defName} ({target.GetType().FullName}); original descent retained.");
                return false;
            }
            ThingDef crashDef = DefDatabase<ThingDef>.GetNamed("Rukaya_InterceptedSkyCrash");
            Thing_RukayaSkyCrash crash = (Thing_RukayaSkyCrash)ThingMaker.MakeThing(crashDef);
            crash.Initialize(target, instigator, crashSettings);
            GenSpawn.Spawn(crash, landing, map);
            if (!crash.TryTakePayload(target))
            {
                if (crash.GetDirectlyHeldThings().Count == 0)
                    crash.Destroy();
                return false;
            }
            target.Destroy(DestroyMode.Vanish);
            FleckMaker.ThrowLightningGlow(crash.DrawPos, map, 1.5f);
            return true;
        }

        public static bool IsNatural(ThingDef def)
        {
            return def.defName == "MeteoriteIncoming" || def.defName == "MeteoriteCraterIncoming"
                || def.defName == "LavaRockIncoming";
        }

        public void Initialize(Skyfaller source, Thing instigator, RukayaSkyCrashSettings crashSettings)
        {
            sourceDef = source.def;
            graphicDef = source.def.graphicData != null || source.innerContainer.Count == 0
                ? source.def : source.innerContainer[0].def;
            start = source.DrawPos;
            drawAngle = source.def.skyfaller.rotationCurve != null
                ? source.DrawAngle()
                : (source.def.skyfaller.rotateGraphicTowardsDirection ? source.angle : 0f)
                    + (source.RandomizeDirectionComp?.ExtraDrawAngle ?? 0f);
            Rotation = source.Rotation;
            launcher = instigator;
            settings = crashSettings;
            remainingTicks = settings.durationTicks;
        }

        public bool TryTakePayload(Skyfaller source)
        {
            List<Thing> items = new List<Thing>();
            List<ThingOwner> owners = new List<ThingOwner>();
            foreach (Thing thing in source.innerContainer)
            {
                if (thing is ActiveTransporter transporter)
                {
                    foreach (Thing cargo in transporter.Contents.innerContainer)
                    {
                        items.Add(cargo);
                        owners.Add(transporter.Contents.innerContainer);
                    }
                }
                else
                {
                    items.Add(thing);
                    owners.Add(source.innerContainer);
                }
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (owners[i].TryTransferToContainer(items[i], payload, false))
                    continue;
                Log.Error($"[Rukaya] Payload transfer failed: {source.def.defName} ({source.GetType().FullName}), item {items[i]}. Rolling back; original descent retained.");
                for (int j = i - 1; j >= 0; j--)
                {
                    if (!payload.TryTransferToContainer(items[j], owners[j], false))
                        Log.Error($"[Rukaya] Payload rollback failed for {items[j]} from {source.def.defName}; retained in crash carrier {ThingID}.");
                }
                return false;
            }
            return true;
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
            Scribe_Defs.Look(ref graphicDef, "graphicDef");
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Values.Look(ref start, "start");
            Scribe_Values.Look(ref drawAngle, "drawAngle");
            Scribe_Values.Look(ref remainingTicks, "remainingTicks");
            Scribe_Values.Look(ref landed, "landed");
            Scribe_Collections.Look(ref damagedCargo, "damagedCargo", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                damagedCargo = damagedCargo ?? new List<Thing>();
                damagedCargo.RemoveAll(t => t == null || t.Destroyed);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (payload.Count > 0)
            {
                Log.Error($"[Rukaya] Refusing to destroy crash carrier {ThingID} ({sourceDef?.defName}) with {payload.Count} unplaced payload entries.");
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
            if (!landed)
            {
                landed = true;
                FleckMaker.ThrowDustPuff(Position.ToVector3Shifted(), Map, 3f);
                FleckMaker.ThrowLightningGlow(Position.ToVector3Shifted(), Map, 2f);
                ReleasePayload();
            }
            else if (this.IsHashIntervalTick(60))
                ReleasePayload();
            if (payload.Count == 0)
                Destroy();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (shadowMaterial == null && sourceDef?.skyfaller != null && !sourceDef.skyfaller.shadow.NullOrEmpty())
                shadowMaterial = MaterialPool.MatFrom(sourceDef.skyfaller.shadow, ShaderDatabase.Transparent);
            if (shadowMaterial != null)
                Skyfaller.DrawDropSpotShadow(DrawPos, Rotation, shadowMaterial, sourceDef.skyfaller.shadowSize, remainingTicks);
            graphicDef?.graphicData?.Graphic.DrawWorker(DrawPos, Rotation, graphicDef, null, drawAngle);
        }

        private static IntVec3 FindLandingCell(IntVec3 original, Map map, float radius)
        {
            if (original.InBounds(map) && original.Standable(map) && !original.Roofed(map))
                return original;
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(original, radius, true))
            {
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map))
                    candidates.Add(cell);
            }
            if (candidates.Count > 0)
                return candidates[0];
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(original, Math.Max(radius, 12f), true))
            {
                if (cell.InBounds(map) && cell.Standable(map))
                    return cell;
            }
            return IntVec3.Invalid;
        }

        private void ReleasePayload()
        {
            for (int i = payload.Count - 1; i >= 0; i--)
            {
                Thing thing = payload[i];
                if (!(thing is Pawn) && !damagedCargo.Contains(thing))
                {
                    damagedCargo.Add(thing);
                    int survivors = RukayaSkyCrashSettings.SurvivingCount(thing.stackCount, settings.cargoLossFraction, Rand.Value);
                    int lost = thing.stackCount - survivors;
                    if (lost > 0)
                    {
                        thing.SplitOff(lost).Destroy(DestroyMode.Vanish);
                        if (survivors == 0)
                        {
                            damagedCargo.Remove(thing);
                            continue;
                        }
                    }
                }
                if (!payload.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out Thing dropped,
                    nearPlaceValidator: cell => cell.Standable(Map)))
                {
                    Log.ErrorOnce($"[Rukaya] Cannot place payload {thing} from {sourceDef?.defName}; retained in {ThingID} and will retry.", thing.thingIDNumber ^ thingIDNumber);
                    continue;
                }
                damagedCargo.Remove(thing);
                if (dropped is Pawn pawn)
                {
                    for (int hit = 0; hit < settings.pawnDamageCount && !pawn.Dead && !pawn.Destroyed; hit++)
                        pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, settings.pawnDamageAmount, instigator: launcher));
                }
            }
        }

        private ThingOwner<Thing> payload;
        private RukayaSkyCrashSettings settings;
        private ThingDef sourceDef;
        private ThingDef graphicDef;
        private Thing launcher;
        private Vector3 start;
        private float drawAngle;
        private int remainingTicks;
        private bool landed;
        private List<Thing> damagedCargo = new List<Thing>();
        private Material shadowMaterial;
    }
}
