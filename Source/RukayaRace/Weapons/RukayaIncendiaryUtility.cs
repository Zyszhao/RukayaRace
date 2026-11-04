using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Weapons
{
    public static class RukayaIncendiaryUtility
    {
        public static void PlaceFuelAndIgnite(IEnumerable<IntVec3> cells, Map map, Thing instigator, ThingDef fuelDef, FloatRange fireSizeRange)
        {
            if (map == null)
            {
                return;
            }

            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                if (fuelDef != null)
                {
                    FilthMaker.TryMakeFilth(cell, map, fuelDef, 1, FilthSourceFlags.None, true);
                }

                IgniteCellForced(cell, map, instigator, fireSizeRange.RandomInRange);
            }
        }

        public static bool IgniteCellForced(IntVec3 cell, Map map, Thing instigator, float fireSize)
        {
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain == null || terrain.IsWater || terrain.exposesToVacuum)
            {
                return false;
            }

            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable && edifice.OccupiedRect().ContractedBy(1).Contains(cell))
            {
                return false;
            }

            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing is Fire existingFire)
                {
                    existingFire.fireSize = Mathf.Max(existingFire.fireSize, Mathf.Clamp(fireSize, 0.1f, 1.75f));
                    return true;
                }

                if (thing.def.category == ThingCategory.Filth && !thing.def.filth.allowsFire)
                {
                    return false;
                }
            }

            Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
            fire.fireSize = Mathf.Clamp(fireSize, 0.1f, 1.75f);
            fire.instigator = instigator;
            GenSpawn.Spawn(fire, cell, map, WipeMode.Vanish);
            return true;
        }
    }
}
