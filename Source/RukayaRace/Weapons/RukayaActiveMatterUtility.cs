using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RukayaRace.Weapons
{
    [StaticConstructorOnStartup]
    public static class RukayaActiveMatterUtility
    {
        public const string ModeOnIconPath = "Rukaya/UI/Rukaya_WeaponModeSwitch_true";
        public const string ModeOffIconPath = "Rukaya/UI/Rukaya_WeaponModeSwitch_false";
        public const string AutoReloadOnIconPath = "Rukaya/UI/Rukaya_WeaponReloadEnabled_true";
        public const string AutoReloadOffIconPath = "Rukaya/UI/Rukaya_WeaponReloadEnabled_false";

        private static Texture2D modeOnIcon;
        private static Texture2D modeOffIcon;
        private static Texture2D autoReloadOnIcon;
        private static Texture2D autoReloadOffIcon;

        public static Texture2D ModeOnIcon => modeOnIcon ?? (modeOnIcon = ContentFinder<Texture2D>.Get(ModeOnIconPath));

        public static Texture2D ModeOffIcon => modeOffIcon ?? (modeOffIcon = ContentFinder<Texture2D>.Get(ModeOffIconPath));

        public static Texture2D AutoReloadOnIcon =>
            autoReloadOnIcon ?? (autoReloadOnIcon = ContentFinder<Texture2D>.Get(AutoReloadOnIconPath));

        public static Texture2D AutoReloadOffIcon =>
            autoReloadOffIcon ?? (autoReloadOffIcon = ContentFinder<Texture2D>.Get(AutoReloadOffIconPath));

        public static Pawn GetEquipmentHolder(ThingWithComps weapon)
        {
            Pawn_EquipmentTracker tracker = weapon?.ParentHolder as Pawn_EquipmentTracker;
            return tracker?.pawn;
        }

        public static bool CanUseReloadSource(Thing thing, Pawn pawn, bool requireStorage)
        {
            if (thing == null || pawn == null || thing.Destroyed || thing.Map != pawn.Map)
            {
                return false;
            }

            if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing))
            {
                return false;
            }

            if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some))
            {
                return false;
            }

            if (requireStorage && !thing.IsInValidStorage())
            {
                return false;
            }

            return thing.stackCount > 0;
        }

        public static Thing FindClosestReloadSource(Pawn pawn, ThingDef thingDef, bool requireStorage)
        {
            if (pawn?.Map == null || thingDef == null)
            {
                return null;
            }

            List<Thing> candidates = pawn.Map.listerThings.ThingsOfDef(thingDef);
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                pawn.Map,
                candidates,
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Some),
                9999f,
                t => CanUseReloadSource(t, pawn, requireStorage));
        }

        public static void ShowModeSwitchMessage(Pawn pawn, bool enabled)
        {
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                return;
            }

            Messages.Message(
                (enabled ? "Rukaya_ActiveMatterModeEnabled" : "Rukaya_ActiveMatterModeDisabled").Translate(pawn.Named("PAWN")),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void ShowAutoReloadMessage(Pawn pawn, bool enabled)
        {
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                return;
            }

            Messages.Message(
                (enabled ? "Rukaya_ActiveMatterAutoReloadEnabled" : "Rukaya_ActiveMatterAutoReloadDisabled")
                .Translate(pawn.Named("PAWN")),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void ShowInsufficientMessage(Pawn pawn, Thing weapon)
        {
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                return;
            }

            Messages.Message(
                "Rukaya_ActiveMatterInsufficient".Translate(
                    pawn.Named("PAWN"),
                    (weapon?.LabelShortCap ?? "Weapon".Translate()).Named("WEAPON")),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }
    }
}
