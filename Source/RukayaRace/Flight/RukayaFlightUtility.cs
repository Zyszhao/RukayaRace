using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Shared UI assets and messages for the flight stance command. Mirrors the weapon mode switch
    /// helper: icon paths live as constants and textures are resolved lazily on the main thread.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RukayaFlightUtility
    {
        public const string FlyingOnIconPath = "Rukaya/UI/Rukaya_FlyingEnabled_true";
        public const string FlyingOffIconPath = "Rukaya/UI/Rukaya_FlyingEnabled_false";

        private static Texture2D flyingOnIcon;
        private static Texture2D flyingOffIcon;

        public static Texture2D FlyingOnIcon =>
            flyingOnIcon ?? (flyingOnIcon = ContentFinder<Texture2D>.Get(FlyingOnIconPath));

        public static Texture2D FlyingOffIcon =>
            flyingOffIcon ?? (flyingOffIcon = ContentFinder<Texture2D>.Get(FlyingOffIconPath));

        public static Texture2D IconFor(RukayaFlightMode mode)
        {
            return mode == RukayaFlightMode.Always ? FlyingOnIcon : FlyingOffIcon;
        }

        public static string CommandLabelKey(RukayaFlightMode mode)
        {
            return mode == RukayaFlightMode.Always
                ? "Rukaya_FlightModeCommandOn"
                : "Rukaya_FlightModeCommandOff";
        }

        public static string CommandDescKey(RukayaFlightMode mode)
        {
            return mode == RukayaFlightMode.Always
                ? "Rukaya_FlightModeCommandFunctionalWingsDesc"
                : "Rukaya_FlightModeCommandOffDesc";
        }

        public static void PlaySwitchSound(RukayaFlightMode mode)
        {
            if (mode == RukayaFlightMode.Always)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        public static void ShowSwitchMessage(Pawn pawn, RukayaFlightMode mode)
        {
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                return;
            }

            string key = mode == RukayaFlightMode.Always
                ? "Rukaya_FlightModeSwitchedOn"
                : "Rukaya_FlightModeSwitchedOff";

            Messages.Message(
                key.Translate(pawn.Named("PAWN")),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        /// <summary>Turns a body part def name into the label HAR uses for a body part condition.</summary>
        public static List<BodyPartRecord> RecordsForDef(Pawn pawn, BodyPartDef def)
        {
            List<BodyPartRecord> result = new List<BodyPartRecord>();
            if (pawn?.RaceProps?.body == null || def == null)
            {
                return result;
            }

            List<BodyPartRecord> all = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].def == def)
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }
    }
}
