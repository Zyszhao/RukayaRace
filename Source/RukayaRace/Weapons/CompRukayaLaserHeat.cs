using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    // Shared heat pool for a laser mech's ground beam and its anti-air beam.
    // Both weapons report their real shots here; the pool is the only thing that
    // gates firing, so the two mounts can never overheat independently.
    public class CompProperties_RukayaLaserHeat : CompProperties
    {
        public float heatPerGroundShot = 10f;
        public float heatPerAirShot = 20f;
        public float maxHeat = 100f;
        public float coolingPerTick = 0.2f;
        public float resumeHeat = 25f;

        public CompProperties_RukayaLaserHeat()
        {
            compClass = typeof(CompRukayaLaserHeat);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (!float.IsFinite(heatPerGroundShot) || heatPerGroundShot < 0f)
                yield return "heatPerGroundShot must be a non-negative number.";
            if (!float.IsFinite(heatPerAirShot) || heatPerAirShot < 0f)
                yield return "heatPerAirShot must be a non-negative number.";
            if (!float.IsFinite(maxHeat) || maxHeat <= 0f)
                yield return "maxHeat must be greater than zero.";
            if (!float.IsFinite(coolingPerTick) || coolingPerTick < 0f)
                yield return "coolingPerTick must be a non-negative number.";
            if (!float.IsFinite(resumeHeat) || resumeHeat < 0f)
                yield return "resumeHeat must be a non-negative number.";
            else if (maxHeat > 0f && resumeHeat >= maxHeat)
                yield return "resumeHeat must be lower than maxHeat.";
            if (heatPerGroundShot <= 0f && heatPerAirShot <= 0f)
                yield return "At least one of heatPerGroundShot and heatPerAirShot must be greater than zero, otherwise the laser can never overheat.";
            if (coolingPerTick <= 0f && (heatPerGroundShot > 0f || heatPerAirShot > 0f))
                yield return "coolingPerTick must be greater than zero, otherwise an overheated laser can never cool down.";
        }
    }

    public class CompRukayaLaserHeat : ThingComp
    {
        private const float HeatEpsilon = 0.001f;

        private float heat;
        private bool overheated;

        public CompProperties_RukayaLaserHeat Props => (CompProperties_RukayaLaserHeat)props;

        public float Heat => heat;

        public float MaxHeat => Props.maxHeat;

        public float ResumeHeat => Props.resumeHeat;

        public bool Overheated => overheated;

        // The single gate both mounts and the ground turret AI must respect.
        public bool CanFire => Props.maxHeat > 0f && !overheated && heat < Props.maxHeat - HeatEpsilon;

        public void NotifyGroundShot()
        {
            AddHeat(Props.heatPerGroundShot);
        }

        public void NotifyAirShot()
        {
            AddHeat(Props.heatPerAirShot);
        }

        private void AddHeat(float amount)
        {
            if (amount <= 0f || Props.maxHeat <= 0f)
                return;

            heat += amount;
            if (heat >= Props.maxHeat - HeatEpsilon)
            {
                heat = Props.maxHeat;
                overheated = true;
            }
        }

        public override void CompTick()
        {
            // Runs every tick for a spawned pawn, including while downed, sleeping
            // or otherwise unable to act, so the shared pool always keeps cooling.
            if (heat > 0f)
            {
                float cooling = Props.coolingPerTick;
                if (cooling > 0f)
                    heat -= cooling;

                if (heat < 0f)
                    heat = 0f;
                else if (Props.maxHeat > 0f && heat > Props.maxHeat)
                    heat = Props.maxHeat;
            }

            if (overheated && heat <= Props.resumeHeat + HeatEpsilon)
                overheated = false;
        }

        public override string CompInspectStringExtra()
        {
            string text = "Rukaya_LaserHeat_Inspect".Translate(heat.ToString("F0"), Props.maxHeat.ToString("F0"));
            if (overheated)
                text += "\n" + "Rukaya_LaserHeat_Overheated".Translate(Props.resumeHeat.ToString("F0"));
            return text;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref heat, "rukayaLaserHeat", 0f);
            Scribe_Values.Look(ref overheated, "rukayaLaserHeatOverheated", false);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (Props.maxHeat > 0f)
            {
                heat = Mathf.Clamp(heat, 0f, Props.maxHeat);
                if (heat >= Props.maxHeat - HeatEpsilon)
                    overheated = true;
                else if (heat <= Props.resumeHeat + HeatEpsilon)
                    overheated = false;
            }
            else
            {
                heat = 0f;
                overheated = false;
            }
        }
    }
}
