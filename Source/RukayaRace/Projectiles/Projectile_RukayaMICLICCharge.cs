using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaMICLICCharge : Projectile_Explosive
    {
        private Vector3 start;
        private Vector3 end;
        private float releaseHeight;
        private float travelAngle;
        private int fallTicks;
        private int age;
        private int detonationTick;
        private Projectile_RukayaMICLICCharge previousCharge;

        public override int UpdateRateTicks => 1;
        private float Progress => Mathf.Clamp01(age / (float)Mathf.Max(1, fallTicks));
        public override Vector3 ExactPosition => Vector3.Lerp(start, end, Progress) + Vector3.up * def.Altitude;
        public Vector3 RopePosition => ExactPosition + Vector3.forward * (releaseHeight * (1f - Progress * Progress));
        public override Quaternion ExactRotation => Quaternion.AngleAxis(travelAngle, Vector3.up);

        public void Initialize(Vector3 start, Vector3 end, float height, int fallTicks,
            int detonationTick, float angle, Projectile_RukayaMICLICCharge previous)
        {
            this.start = start.Yto0();
            this.end = end.Yto0();
            if (!this.end.ToIntVec3().InBounds(Map))
                this.end = this.start;
            releaseHeight = Mathf.Max(0f, height);
            this.fallTicks = Mathf.Max(1, fallTicks);
            this.detonationTick = detonationTick;
            travelAngle = angle;
            previousCharge = previous;
            Position = this.start.ToIntVec3();
        }

        protected override void TickInterval(int delta)
        {
            age += delta;
            Position = ExactPosition.ToIntVec3();
            landed = age >= fallTicks;
            if (landed && Find.TickManager.TicksGame >= detonationTick)
                Explode();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (previousCharge != null && previousCharge.Spawned)
                DrawRope(RopePosition, previousCharge.RopePosition);
            base.DrawAt(RopePosition, flip);
        }

        internal static void DrawRope(Vector3 a, Vector3 b)
        {
            // Keep the cord below both standing and downed pawns.
            a.y = b.y = AltitudeLayer.FloorEmplacement.AltitudeFor();
            GenDraw.DrawLineBetween(a, b, SolidColorMaterials.SimpleSolidColorMaterial(Color.black), 0.04f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref start, "miclicStart");
            Scribe_Values.Look(ref end, "miclicEnd");
            Scribe_Values.Look(ref releaseHeight, "miclicReleaseHeight");
            Scribe_Values.Look(ref travelAngle, "miclicTravelAngle");
            Scribe_Values.Look(ref fallTicks, "miclicFallTicks");
            Scribe_Values.Look(ref age, "miclicAge");
            Scribe_Values.Look(ref detonationTick, "miclicDetonationTick");
            Scribe_References.Look(ref previousCharge, "miclicPreviousCharge");
        }
    }
}
