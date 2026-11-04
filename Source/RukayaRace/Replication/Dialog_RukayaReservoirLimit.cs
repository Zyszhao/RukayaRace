using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    /// <summary>
    /// Reservoir limit picker: the vanilla Dialog_Slider flow plus a numeric entry box on the right of
    /// the slider. The slider still works exactly like Command_SetTargetFuelLevel (live apply while
    /// dragging, leftmost position = follow the global mod setting), and the box lets the player type
    /// an exact container count. Enter commits, Escape cancels.
    /// </summary>
    public class Dialog_RukayaReservoirLimit : Window
    {
        private const float SliderHeight = 24f;
        private const float InputWidth = 74f;
        private const float BottomAreaHeight = 30f;

        private readonly string subjectLabel;
        private readonly int globalCapacity;
        private readonly int min;
        private readonly int max;
        private readonly Action<int> confirmAction;

        private int curValue;
        private string inputBuffer;

        public override Vector2 InitialSize => new Vector2(400f, 176f);

        protected override float Margin => 10f;

        public Dialog_RukayaReservoirLimit(
            string subjectLabel,
            int globalCapacity,
            int min,
            int max,
            int startingValue,
            Action<int> confirmAction)
        {
            this.subjectLabel = subjectLabel;
            this.globalCapacity = globalCapacity;
            this.min = min;
            this.max = max;
            this.confirmAction = confirmAction;
            curValue = Mathf.Clamp(startingValue, min, max);
            inputBuffer = curValue.ToString();

            doCloseX = false;
            forcePause = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            string caption = "Rukaya_ReservoirLimitDialog".Translate(subjectLabel, Command_SetRukayaReservoirLimit.LimitText(curValue, globalCapacity));
            float captionHeight = Text.CalcHeight(caption, inRect.width);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, captionHeight), caption);
            Text.Anchor = TextAnchor.UpperLeft;

            float y = inRect.y + captionHeight + 12f;

            // Slider occupies everything left of the entry box; the two stay on one visual line.
            Rect sliderRect = new Rect(inRect.x, y + 2f, inRect.width - InputWidth - 8f, SliderHeight);
            int sliderValue = Mathf.RoundToInt(Widgets.HorizontalSlider(
                sliderRect,
                curValue,
                min,
                max,
                true,
                null,
                null,
                null,
                1f));
            if (sliderValue != curValue)
            {
                curValue = sliderValue;
                inputBuffer = curValue.ToString();
                Apply(curValue);
            }

            Rect inputRect = new Rect(inRect.xMax - InputWidth, y, InputWidth, SliderHeight);
            HandleInputBox(inputRect);

            DrawSliderEndLabels(sliderRect, y + SliderHeight);

            float buttonY = inRect.yMax - BottomAreaHeight;
            float buttonWidth = (inRect.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(inRect.x, buttonY, buttonWidth, BottomAreaHeight), "CancelButton".Translate()))
            {
                Close(true);
            }

            if (Widgets.ButtonText(new Rect(inRect.x + buttonWidth + 10f, buttonY, buttonWidth, BottomAreaHeight), "OK".Translate()))
            {
                CommitBufferedValue();
                Close(true);
            }
        }

        private void DrawSliderEndLabels(Rect sliderRect, float y)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(sliderRect.x, y - 8f, sliderRect.width / 2f, Text.LineHeight), min.ToString());
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(sliderRect.x + sliderRect.width / 2f, y - 8f, sliderRect.width / 2f, Text.LineHeight), max.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = previousFont;
            GUI.color = Color.white;
        }

        private void HandleInputBox(Rect rect)
        {
            string edited = Widgets.TextField(rect, inputBuffer ?? string.Empty);
            if (edited != inputBuffer)
            {
                inputBuffer = edited;
            }

            // Sanitising on every keystroke keeps the field from ever holding something that cannot be
            // parsed, which is what makes the live preview of the applying value trustworthy.
            inputBuffer = Sanitize(inputBuffer);
            if (int.TryParse(inputBuffer, out int parsed))
            {
                int clamped = Mathf.Clamp(parsed, min, max);
                if (clamped != curValue)
                {
                    curValue = clamped;
                    Apply(curValue);
                }
            }

            Event current = Event.current;
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                CommitBufferedValue();
                current.Use();
            }
        }

        private void CommitBufferedValue()
        {
            if (int.TryParse(inputBuffer ?? string.Empty, out int parsed))
            {
                curValue = Mathf.Clamp(parsed, min, max);
            }

            inputBuffer = curValue.ToString();
            Apply(curValue);
        }

        private void Apply(int value)
        {
            confirmAction?.Invoke(value);
        }

        private static string Sanitize(string text)
        {
            if (text.NullOrEmpty())
            {
                return string.Empty;
            }

            string result = string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Only digits: the limit is a bare container count, and "-" / "." would either be
                // rejected by TryParse or silently truncate what the player typed. "-" is accepted at
                // position 0 only so that typing "-1" is visibly clamped instead of being eaten.
                if (char.IsDigit(c) || (c == '-' && result.Length == 0))
                {
                    result += c;
                }
            }

            if (result.Length > 6)
            {
                result = result.Substring(0, 6);
            }

            return result;
        }
    }
}
