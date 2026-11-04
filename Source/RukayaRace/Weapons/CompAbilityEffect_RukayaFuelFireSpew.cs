using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public class CompAbilityEffect_RukayaFuelFireSpew : CompAbilityEffect
    {
        private readonly List<IntVec3> tmpCells = new List<IntVec3>();

        private new CompProperties_AbilityRukayaFuelFireSpew Props => (CompProperties_AbilityRukayaFuelFireSpew)this.props;

        private Pawn Pawn => this.parent.pawn;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            IntVec3 cell = target.Cell;
            Map map = this.Pawn.MapHeld;
            List<IntVec3> affectedCells = this.AffectedCells(target);

            GenExplosion.DoExplosion(
                cell,
                map,
                0f,
                DamageDefOf.Flame,
                this.Pawn,
                this.Props.damAmount,
                -1f,
                null,
                null,
                null,
                null,
                null,
                0f,
                1,
                null,
                null,
                255,
                false,
                null,
                0f,
                1,
                1f,
                false,
                null,
                null,
                null,
                false,
                0.6f,
                0f,
                false,
                null,
                1f,
                this.parent.verb.verbProps.flammabilityAttachFireChanceCurve,
                affectedCells,
                null,
                null);

            RukayaIncendiaryUtility.PlaceFuelAndIgnite(affectedCells, map, this.Pawn, this.Props.FuelDef, this.Props.fireSizeRange);
            base.Apply(target, dest);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            GenDraw.DrawFieldEdges(this.AffectedCells(target), 2900);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            if (this.Pawn.Faction != null)
            {
                foreach (IntVec3 c in this.AffectedCells(target))
                {
                    List<Thing> thingList = c.GetThingList(this.Pawn.Map);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        if (thingList[i].Faction == this.Pawn.Faction)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private List<IntVec3> AffectedCells(LocalTargetInfo target)
        {
            this.tmpCells.Clear();
            Vector3 casterPos = this.Pawn.Position.ToVector3Shifted().Yto0();
            IntVec3 targetCell = target.Cell.ClampInsideMap(this.Pawn.Map);
            if (this.Pawn.Position == targetCell)
            {
                return this.tmpCells;
            }

            float distance = (targetCell - this.Pawn.Position).LengthHorizontal;
            float dirX = (targetCell.x - this.Pawn.Position.x) / distance;
            float dirZ = (targetCell.z - this.Pawn.Position.z) / distance;
            targetCell.x = Mathf.RoundToInt(this.Pawn.Position.x + dirX * this.Props.range);
            targetCell.z = Mathf.RoundToInt(this.Pawn.Position.z + dirZ * this.Props.range);

            float targetAngle = Vector3.SignedAngle(targetCell.ToVector3Shifted().Yto0() - casterPos, Vector3.right, Vector3.up);
            float halfWidth = this.Props.lineWidthEnd / 2f;
            float coneLength = Mathf.Sqrt(Mathf.Pow((targetCell - this.Pawn.Position).LengthHorizontal, 2f) + Mathf.Pow(halfWidth, 2f));
            float coneAngle = 57.29578f * Mathf.Asin(halfWidth / coneLength);

            int numCells = GenRadial.NumCellsInRadius(this.Props.range);
            for (int i = 0; i < numCells; i++)
            {
                IntVec3 current = this.Pawn.Position + GenRadial.RadialPattern[i];
                if (this.CanUseCell(current) &&
                    Mathf.Abs(Mathf.DeltaAngle(Vector3.SignedAngle(current.ToVector3Shifted().Yto0() - casterPos, Vector3.right, Vector3.up), targetAngle)) <= coneAngle)
                {
                    this.tmpCells.Add(current);
                }
            }

            List<IntVec3> lineCells = GenSight.BresenhamCellsBetween(this.Pawn.Position, targetCell);
            for (int i = 0; i < lineCells.Count; i++)
            {
                IntVec3 current = lineCells[i];
                if (!this.tmpCells.Contains(current) && this.CanUseCell(current))
                {
                    this.tmpCells.Add(current);
                }
            }

            return this.tmpCells;
        }

        private bool CanUseCell(IntVec3 cell)
        {
            return cell.InBounds(this.Pawn.Map)
                && cell != this.Pawn.Position
                && (this.Props.canHitFilledCells || !cell.Filled(this.Pawn.Map))
                && cell.InHorDistOf(this.Pawn.Position, this.Props.range)
                && this.parent.verb.TryFindShootLineFromTo(this.parent.pawn.Position, cell, out ShootLine _, false);
        }
    }
}
