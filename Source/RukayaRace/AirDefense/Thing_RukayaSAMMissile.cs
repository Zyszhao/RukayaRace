using RimWorld;
using RukayaRace.Projectiles;
using UnityEngine;
using Verse;

namespace RukayaRace.AirDefense
{
    public class Thing_RukayaSAMMissile : Thing
    {
        public override Vector3 DrawPos => position;

        // The arrival scheduler uses the same steps as Tick, independent of camera position.
        public int RemainingFlightTicks
        {
            get
            {
                Vector3 projected = position;
                for (int ticks = 1; ticks <= MaximumFlightTicks - flightTicks; ticks++)
                {
                    projected += direction * SpeedAt(flightTicks + ticks);
                    if (!projected.InBounds(Map))
                        return ticks;
                }
                return Mathf.Max(1, MaximumFlightTicks - flightTicks);
            }
        }

        public void Initialize(Vector3 origin, Vector3 flightDirection, float drawAngle)
        {
            position = origin;
            direction = flightDirection.normalized;
            angle = drawAngle;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref position, "rukayaSAMMissilePosition");
            Scribe_Values.Look(ref direction, "rukayaSAMMissileDirection");
            Scribe_Values.Look(ref angle, "rukayaSAMMissileAngle");
            Scribe_Values.Look(ref flightTicks, "rukayaSAMMissileFlightTicks");
        }

        protected override void Tick()
        {
            base.Tick();
            flightTicks++;
            position += direction * SpeedAt(flightTicks);
            if (!position.InBounds(Map) || flightTicks >= MaximumFlightTicks)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }
            Position = position.ToIntVec3();
            if (flightTicks % 2 == 0)
                EmitSmoke();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            RukayaRocketExhaustUtility.Draw(position, Quaternion.Euler(0f, angle, 0f), def.graphicData.drawSize.y, 1.8f);
            Graphic.DrawWorker(position, Rot4.North, def, this, angle);
        }

        private static float SpeedAt(int tick) => Mathf.Min(0.2f + tick * 0.012f, 2.4f);

        private void EmitSmoke()
        {
            Vector3 nozzle = RukayaRocketExhaustUtility.NozzlePosition(position,
                Quaternion.Euler(0f, angle, 0f), def.graphicData.drawSize.y);
            if (!nozzle.InBounds(Map) || !nozzle.ShouldSpawnMotesAt(Map))
                return;
            // FleckStatic.Setup also uses Rand internally; isolate the entire creation.
            float pulse = Mathf.Sin(flightTicks * 1.7f + thingIDNumber);
            FleckCreationData smoke = FleckMaker.GetDataStatic(nozzle, Map, FleckDefOf.Smoke, 1.8f + pulse * 0.25f);
            smoke.rotationRate = pulse * 18f;
            smoke.velocity = -direction * 1.4f;
            Rand.PushState(unchecked(thingIDNumber * 397 ^ flightTicks));
            try { Map.flecks.CreateFleck(smoke); }
            finally { Rand.PopState(); }
        }

        private const int MaximumFlightTicks = 600;
        private Vector3 position;
        private Vector3 direction;
        private float angle;
        private int flightTicks;
    }
}
