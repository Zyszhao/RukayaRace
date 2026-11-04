using UnityEngine;
using Verse;

namespace RukayaRace.Projectiles
{
    public class Projectile_RukayaRocket : Projectile_Explosive
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!landed)
                RukayaRocketExhaustUtility.Draw(drawLoc, ExactRotation, def.graphicData.drawSize.y);
            base.DrawAt(drawLoc, flip);
        }
    }

    [StaticConstructorOnStartup]
    internal static class RukayaRocketExhaustUtility
    {
        private static readonly Material ExhaustMaterial =
            MaterialPool.MatFrom("Things/Mote/LightningGlow", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();

        internal static Vector3 NozzlePosition(Vector3 position, Quaternion rotation, float canvasSize)
        {
            return position - rotation * Vector3.forward * (canvasSize * 0.4f);
        }

        internal static void Draw(Vector3 position, Quaternion rotation, float canvasSize, float exhaustScale = 1f)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 nozzle = NozzlePosition(position, rotation, canvasSize);
            nozzle.y -= 0.001f;
            float flicker = 0.9f + 0.1f * Mathf.Sin(Find.TickManager.TicksGame * 1.7f);
            DrawFlame(nozzle - forward * (0.36f * exhaustScale), rotation, 0.38f * exhaustScale, 0.95f * flicker * exhaustScale,
                new Color(1f, 0.38f, 0.06f, 0.85f));
            DrawFlame(nozzle - forward * (0.12f * exhaustScale), rotation, 0.2f * exhaustScale, 0.5f * flicker * exhaustScale,
                new Color(1f, 0.9f, 0.55f, 1f));
        }

        private static void DrawFlame(Vector3 position, Quaternion rotation, float width, float length, Color color)
        {
            Properties.SetColor(ShaderPropertyIDs.Color, color);
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, matrix, ExhaustMaterial, 0, null, 0, Properties);
        }
    }
}
