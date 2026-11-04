using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RukayaRace.Weapons
{
    [StaticConstructorOnStartup]
    public class Gizmo_RukayaActiveMatterLoad : Gizmo
    {
        private const float GizmoWidth = 212f;
        private const float GizmoHeight = 75f;
        private readonly CompRukayaActiveMatterWeapon weaponComp;
        private static bool draggingBar;
        private static readonly Texture2D BarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.34f, 0.42f, 0.43f));
        private static readonly Texture2D BarHighlightTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.43f, 0.54f, 0.55f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.03f, 0.035f, 0.05f));
        private static readonly Texture2D TargetTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.74f, 0.97f, 0.8f));
        private static readonly Texture2D ThresholdTex = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.95f));

        public Gizmo_RukayaActiveMatterLoad(CompRukayaActiveMatterWeapon weaponComp)
        {
            this.weaponComp = weaponComp;
            this.Order = -100f;
        }

        public override float GetWidth(float maxWidth)
        {
            return GizmoWidth;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GizmoWidth, GizmoHeight);
            Rect contentRect = rect.ContractedBy(8f);
            Widgets.DrawWindowBackground(rect);

            bool mouseOverElement = false;
            Text.Font = GameFont.Small;

            Rect headerRect = contentRect;
            headerRect.height = Text.LineHeight;
            this.DrawHeader(headerRect, ref mouseOverElement);

            Rect barRect = contentRect;
            barRect.yMin = headerRect.yMax + 8f;

            float currentPercent = this.weaponComp.CurrentLoadPercent;
            float targetPercent = this.weaponComp.TargetLoadPercent;
            this.DrawBar(barRect, currentPercent, ref targetPercent);
            if (!Mathf.Approximately(targetPercent, this.weaponComp.TargetLoadPercent))
            {
                this.weaponComp.TargetLoadPercent = targetPercent;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, "Rukaya_ActiveMatterTargetBar".Translate(this.weaponComp.TargetUnits));
            Text.Anchor = TextAnchor.UpperLeft;

            if (Mouse.IsOver(rect) && !mouseOverElement)
            {
                Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect, new TipSignal(this.GetTooltip(), Gen.HashCombineInt(this.GetHashCode(), 8573612)));
            }

            return new GizmoResult(GizmoState.Clear);
        }

        private void DrawBar(Rect barRect, float currentPercent, ref float targetPercent)
        {
            bool mouseOver = Mouse.IsOver(barRect);
            Widgets.FillableBar(
                barRect,
                Mathf.Min(currentPercent, 1f),
                mouseOver ? BarHighlightTex : BarTex,
                EmptyBarTex,
                true);

            float draggedPercent = this.GetMouseTargetPercent(barRect);
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && mouseOver)
            {
                targetPercent = draggedPercent;
                draggingBar = true;
                currentEvent.Use();
            }

            if ((UnityGUIBugsFixer.MouseDrag(0) & draggingBar) && mouseOver)
            {
                if (Mathf.Abs(draggedPercent - targetPercent) > 1E-45f)
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                }

                targetPercent = draggedPercent;
                if (currentEvent.type == EventType.MouseDrag)
                {
                    currentEvent.Use();
                }
            }

            if ((currentEvent.type == EventType.MouseUp && currentEvent.button == 0) & draggingBar)
            {
                draggingBar = false;
                currentEvent.Use();
            }

            this.DrawThresholds(barRect);
            this.DrawTarget(barRect, draggingBar ? draggedPercent : targetPercent);
            GUI.color = Color.white;
        }

        private float GetMouseTargetPercent(Rect barRect)
        {
            int increments = Mathf.Max(1, this.weaponComp.MaxCapacity);
            float percent = (Event.current.mousePosition.x - barRect.x) / barRect.width;
            return Mathf.Clamp(Mathf.Round(percent * increments) / increments, 0f, 1f);
        }

        private void DrawThresholds(Rect barRect)
        {
            if (this.weaponComp.MaxCapacity <= 0)
            {
                return;
            }

            int stepUnits = Mathf.Max(1, this.weaponComp.UnitsPerContainer);
            for (int units = stepUnits; units < this.weaponComp.MaxCapacity; units += stepUnits)
            {
                float percent = (float)units / this.weaponComp.MaxCapacity;
                Rect position = new Rect
                {
                    x = barRect.x + 3f + (barRect.width - 8f) * percent,
                    y = barRect.y + barRect.height - 9f,
                    width = 2f,
                    height = 6f
                };
                GUI.DrawTexture(position, ThresholdTex);
            }
        }

        private void DrawTarget(Rect barRect, float percent)
        {
            float offset = Mathf.Round((barRect.width - 8f) * percent);
            GUI.DrawTexture(new Rect(barRect.x + 3f + offset, barRect.y, 2f, barRect.height), TargetTex);

            float xMin = Mathf.Floor(barRect.x + 2f + offset);
            float xMax = Mathf.Ceil(xMin + 4f);
            Rect capRect = new Rect
            {
                y = barRect.y - 3f,
                height = 5f,
                xMin = xMin,
                xMax = xMax
            };
            GUI.DrawTexture(capRect, TargetTex);

            Rect bottomCapRect = capRect;
            bottomCapRect.y = barRect.yMax - 2f;
            GUI.DrawTexture(bottomCapRect, TargetTex);
        }

        private string GetTooltip()
        {
            return "Rukaya_ActiveMatterLoadTooltip".Translate(
                this.weaponComp.CurrentUnits,
                this.weaponComp.MaxCapacity,
                this.weaponComp.TargetUnits,
                this.weaponComp.ActiveCostPerUse);
        }

        private void DrawHeader(Rect headerRect, ref bool mouseOverElement)
        {
            Rect left = headerRect;
            left.xMax -= 54f;
            Rect right = headerRect;
            right.xMin = left.xMax + 4f;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(left, "Rukaya_ActiveMatterLoadTitle".Translate());
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(right, this.weaponComp.CurrentUnits + " / " + this.weaponComp.MaxCapacity);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
