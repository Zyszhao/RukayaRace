using System.Collections.Generic;
using RimWorld;
using HarmonyLib;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompProperties_RukayaMechTurretGun : CompProperties_TurretGun
    {
        public bool retainTargetBetweenBursts;

        public CompProperties_RukayaMechTurretGun()
        {
            compClass = typeof(CompRukayaMechTurretGun);
        }
    }

    public class CompRukayaMechTurretGun : CompTurretGun
    {
        private static readonly AccessTools.FieldRef<CompTurretGun, bool> FireAtWill =
            AccessTools.FieldRefAccess<CompTurretGun, bool>("fireAtWill");

        public bool HasWeapon => gun?.TryGetComp<CompEquippable>()?.PrimaryVerb != null;

        // Shared with the anti-air mount; null means this chassis has no heat pool.
        public CompRukayaLaserHeat LaserHeat => parent.TryGetComp<CompRukayaLaserHeat>();

        public bool LaserHeatCanFire => LaserHeat?.CanFire ?? true;

        public bool CanOperate => HasWeapon && parent is Pawn pawn && pawn.Spawned && !pawn.Dead && !pawn.Downed
            && pawn.Awake() && !pawn.stances.stunner.Stunned && !TurretDestroyed
            && (parent.TryGetComp<CompCanBeDormant>()?.Awake ?? true);

        public bool CanStartCast => CanOperate && LaserHeatCanFire && burstCooldownTicksLeft <= 0
            && AttackVerb.state != VerbState.Bursting
            && !(parent is Pawn pawn && pawn.stances.curStance is Stance_Warmup warmup
                && warmup.verb == AttackVerb);

        public bool AutoFireEnabled => !(parent is Pawn pawn) || !pawn.IsColonyMechPlayerControlled
            || FireAtWill(this);

        public void NotifyCastStarting(LocalTargetInfo target)
        {
            currentTarget = target;
            burstWarmupTicksLeft = 0;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (!(parent is Pawn pawn) || !pawn.Spawned || !HasWeapon)
                yield break;

            // The gun lives inside CompTurretGun rather than the equipment tracker.
            // Use vanilla weapon commands so hovering previews the actual verb's
            // range, and orders go through the existing shared warmup/cooldown.
            foreach (Command command in GunCompEq.GetVerbsCommands())
            {
                command.defaultLabel = gun.LabelCap;
                if (TurretDestroyed)
                    command.Disable("IsIncapableOfShootingLower".Translate(pawn));
                else if (pawn.Dead || pawn.Downed)
                    command.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
                else if (pawn.Faction == Faction.OfPlayer && !pawn.IsColonyMechPlayerControlled)
                    command.Disable("CannotOrderNonControlled".Translate());
                else if (!LaserHeatCanFire)
                    command.Disable("Rukaya_LaserHeat_GizmoDisabled".Translate(
                        LaserHeat.Heat.ToString("F0"), LaserHeat.MaxHeat.ToString("F0")));
                yield return command;
            }
        }

        public override void CompTick()
        {
            // Vanilla rescans every 10 ticks until the verb starts bursting. A pawn's
            // warmup lives in its stance instead, so another TryStartCastOn resets it.
            // Let that stance finish (or cancel) before resuming the turret loop.
            if (parent is Pawn pawn
                && pawn.stances?.curStance is Stance_Warmup warmup
                && warmup.verb == AttackVerb)
            {
                curRotation = (warmup.focusTarg.CenterVector3 - parent.DrawPos).AngleFlat() + Props.angleOffset;
                return;
            }

            // While the shared laser pool is overheated, do not scan for targets or
            // start a warmup: vanilla re-acquires a target every 10 ticks, so a
            // blocked TryStartCastOn would otherwise restart the warmup forever and
            // look like the turret is charging without ever firing. Keep aiming at
            // the retained target and keep the burst cooldown running instead, so
            // firing resumes by itself the moment the heat drops below resumeHeat.
            if (!LaserHeatCanFire)
            {
                if (currentTarget.IsValid)
                    curRotation = (currentTarget.Cell.ToVector3Shifted() - parent.DrawPos).AngleFlat() + Props.angleOffset;
                if (burstCooldownTicksLeft > 0)
                    burstCooldownTicksLeft--;
                burstWarmupTicksLeft = 0;
                if (HasWeapon)
                    AttackVerb.VerbTick();
                return;
            }

            // Keep a valid laser target across short bursts. Only search again when it
            // dies, becomes unavailable, leaves range or loses line of sight.
            if (((CompProperties_RukayaMechTurretGun)props).retainTargetBetweenBursts
                && CanOperate && AutoFireEnabled && AttackVerb.state != VerbState.Bursting
                && currentTarget.HasThing
                && currentTarget.Thing.Spawned && !currentTarget.Thing.Destroyed
                && (!(currentTarget.Thing is Pawn targetPawn)
                    || !targetPawn.Dead && !targetPawn.Downed && !targetPawn.IsPsychologicallyInvisible())
                && parent.HostileTo(currentTarget.Thing)
                && AttackVerb.CanHitTarget(currentTarget))
            {
                if (burstCooldownTicksLeft > 0)
                    burstCooldownTicksLeft--;
                if (burstCooldownTicksLeft <= 0)
                    AttackVerb.TryStartCastOn(currentTarget, nonInterruptingSelfCast: true);
                return;
            }

            base.CompTick();
        }
    }
}
