using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    public class Dialog_RukayaDevFillReservoir : Window
    {
        private readonly CompRukayaActiveMatterReservoir reservoir;
        private string countBuffer;

        public override Vector2 InitialSize => new Vector2(440f, 230f);

        public Dialog_RukayaDevFillReservoir(CompRukayaActiveMatterReservoir reservoir)
        {
            this.reservoir = reservoir;
            countBuffer = Mathf.Max(1, reservoir.FreeCapacity).ToString();
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 76f),
                "Rukaya_DevFillReservoirPrompt".Translate(reservoir.parent.LabelShortCap, reservoir.FreeCapacity));
            countBuffer = Widgets.TextField(new Rect(0f, 82f, inRect.width, 30f), countBuffer);

            float buttonWidth = (inRect.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, inRect.height - 35f, buttonWidth, 35f), "CancelButton".Translate()))
            {
                Close();
            }

            bool valid = Prefs.DevMode && reservoir.parent.Spawned && !reservoir.parent.Destroyed
                && int.TryParse(countBuffer, out int count) && count > 0 && reservoir.FreeCapacity > 0;
            if (Widgets.ButtonText(new Rect(buttonWidth + 10f, inRect.height - 35f, buttonWidth, 35f),
                "Rukaya_DevFillReservoir".Translate(), active: valid))
            {
                // Reuse physical-container creation; clamp against the current per-building limit.
                int amount = Mathf.Min(int.Parse(countBuffer), reservoir.FreeCapacity);
                if (reservoir.TryRefund(amount))
                {
                    Close();
                }
            }
        }
    }
}
