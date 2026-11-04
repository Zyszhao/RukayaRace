using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class MapComponent_RukayaLightningArcs : MapComponent
    {
        private readonly List<LightningArcVisual> arcs = new List<LightningArcVisual>();

        public MapComponent_RukayaLightningArcs(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            for (int i = this.arcs.Count - 1; i >= 0; i--)
            {
                this.arcs[i].remainingTicks--;
                if (this.arcs[i].remainingTicks <= 0)
                {
                    this.arcs.RemoveAt(i);
                }
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();

            for (int i = 0; i < this.arcs.Count; i++)
            {
                this.DrawArc(this.arcs[i]);
            }
        }

        public void ScheduleArc(Vector3 origin, Vector3 destination, int segments, float jitterRadius, int durationTicks)
        {
            if (durationTicks <= 0 || segments <= 0)
            {
                return;
            }

            origin.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            destination.y = AltitudeLayer.MetaOverlays.AltitudeFor();

            List<Vector3> points = new List<Vector3>(segments + 1);
            points.Add(origin);

            Vector3 direction = destination - origin;
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
            if (perpendicular.sqrMagnitude > 0.001f)
            {
                perpendicular.Normalize();
            }

            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point = Vector3.Lerp(origin, destination, t);
                if (i < segments && jitterRadius > 0f)
                {
                    point += perpendicular * Rand.Range(-jitterRadius, jitterRadius);
                }

                point.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                points.Add(point);
            }

            this.arcs.Add(new LightningArcVisual
            {
                points = points,
                remainingTicks = durationTicks
            });
        }

        private void DrawArc(LightningArcVisual arc)
        {
            for (int i = 0; i < arc.points.Count - 1; i++)
            {
                GenDraw.DrawLineBetween(arc.points[i], arc.points[i + 1], SimpleColor.Cyan, 0.2f);
                GenDraw.DrawLineBetween(arc.points[i], arc.points[i + 1], SimpleColor.White, 0.08f);
            }
        }

        private class LightningArcVisual
        {
            public List<Vector3> points;
            public int remainingTicks;
        }
    }
}
