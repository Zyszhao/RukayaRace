using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    /// <summary>
    /// Draws short-lived additive laser beams between two exact world positions.
    /// Core-only assets are used (mod-local streak texture + vanilla MoteGlow shader),
    /// so the effect has no DLC dependency. A beam appears instantly in full length on
    /// the firing tick and fades out over its configured duration.
    /// </summary>
    // 静态字段 MatPropertyBlock 属于 Unity 资源类型：RimWorld 要求所有资源在主线程加载，
    // 加上该属性可让静态构造函数在启动期（主线程）就执行完。
    [StaticConstructorOnStartup]
    public class MapComponent_RukayaLaserLines : MapComponent
    {
        private const string BeamTexPath = "Rukaya/FX/LaserBeam";
        private const string GlowTexPath = "Things/Mote/LightningGlow";

        private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();
        private static Material beamMat;
        private static Material glowMat;

        private readonly List<LaserBeamVisual> beams = new List<LaserBeamVisual>();

        public MapComponent_RukayaLaserLines(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            for (int i = this.beams.Count - 1; i >= 0; i--)
            {
                this.beams[i].remainingTicks--;
                if (this.beams[i].remainingTicks <= 0)
                {
                    this.beams.RemoveAt(i);
                }
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();

            for (int i = 0; i < this.beams.Count; i++)
            {
                this.DrawBeam(this.beams[i]);
            }
        }

        /// <summary>
        /// Schedules a whole laser line from <paramref name="origin"/> to <paramref name="destination"/>.
        /// Both endpoints are used exactly as given (world coordinates, altitude is fixed here),
        /// so the beam starts precisely at the firing point with no added offset.
        /// </summary>
        public void ScheduleLaserLine(Vector3 origin, Vector3 destination, int durationTicks, float width, float glowWidth, Color color, float impactGlowSize, float fadeFraction = 0.4f)
        {
            if (durationTicks <= 0)
            {
                return;
            }

            float altitude = AltitudeLayer.MoteOverhead.AltitudeFor();
            origin.y = altitude;
            destination.y = altitude;

            this.beams.Add(new LaserBeamVisual
            {
                origin = origin,
                destination = destination,
                remainingTicks = durationTicks,
                totalTicks = durationTicks,
                width = Mathf.Max(0.03f, width),
                glowWidth = Mathf.Max(width, glowWidth),
                color = color,
                fadeFraction = Mathf.Clamp(fadeFraction, 0.01f, 1f),
                impactGlowSize = Mathf.Max(0f, impactGlowSize)
            });
        }

        private void DrawBeam(LaserBeamVisual beam)
        {
            if (beam.origin == beam.destination)
            {
                return;
            }

            float fade = Mathf.Clamp01((float)beam.remainingTicks / beam.totalTicks / beam.fadeFraction);

            // Soft outer glow pass.
            Color glowColor = beam.color;
            glowColor.a *= fade * 0.5f;
            this.DrawTexturedSegment(beam.origin, beam.destination, beam.glowWidth, glowColor, GetBeamMat());
            // Bright inner core pass (closer to white = hotter centre).
            Color coreColor = Color.Lerp(beam.color, Color.white, 0.45f);
            coreColor.a *= fade;
            this.DrawTexturedSegment(beam.origin, beam.destination, beam.width, coreColor, GetBeamMat());

            if (beam.impactGlowSize > 0.01f && fade > 0f)
            {
                Color flashColor = Color.Lerp(Color.white, beam.color, 0.4f);
                flashColor.a *= fade;
                this.DrawImpactGlow(beam.destination, beam.impactGlowSize, flashColor);
            }
        }

        private static Material GetBeamMat()
        {
            if (beamMat == null)
            {
                beamMat = MaterialPool.MatFrom(BeamTexPath, ShaderDatabase.MoteGlow);
            }
            return beamMat;
        }

        private static Material GetGlowMat()
        {
            if (glowMat == null)
            {
                glowMat = MaterialPool.MatFrom(GlowTexPath, ShaderDatabase.MoteGlow);
            }
            return glowMat;
        }

        private void DrawTexturedSegment(Vector3 a, Vector3 b, float width, Color color, Material material)
        {
            float length = (a - b).MagnitudeHorizontal();
            if (length < 0.05f)
            {
                return;
            }

            Vector3 pos = (a + b) * 0.5f;
            pos.y = a.y;
            Quaternion rotation = Quaternion.LookRotation(a - b);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, rotation, new Vector3(width, 1f, length));
            MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0, null, 0, MatPropertyBlock);
        }

        private void DrawImpactGlow(Vector3 pos, float size, Color color)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size));
            MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, GetGlowMat(), 0, null, 0, MatPropertyBlock);
        }

        private class LaserBeamVisual
        {
            public Vector3 origin;
            public Vector3 destination;
            public int remainingTicks;
            public int totalTicks;
            public float width;
            public float glowWidth;
            public Color color;
            public float fadeFraction;
            public float impactGlowSize;
        }
    }
}
