using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RukayaRace.Trade
{
    public class RukayaTransportExtension : DefModExtension
    {
        public float caravanCapacity = 300f;
    }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class Patch_RukayaTransportCapacity
    {
        public static bool Prefix(Pawn p, StringBuilder explanation, ref float __result)
        {
            RukayaTransportExtension transport = p.def.GetModExtension<RukayaTransportExtension>();
            if (transport == null)
                return true;

            __result = MassUtility.CanEverCarryAnything(p) ? transport.caravanCapacity : 0f;
            if (explanation != null)
            {
                if (explanation.Length > 0)
                    explanation.AppendLine();
                explanation.Append("  - " + p.LabelShortCap + ": " + __result.ToStringMassOffset());
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TraderCaravanUtility), nameof(TraderCaravanUtility.GetTraderCaravanRole))]
    public static class Patch_RukayaEmptyCarrierRole
    {
        public static void Postfix(Pawn p, ref TraderCaravanRole __result)
        {
            if (p.def.GetModExtension<RukayaTransportExtension>() != null
                && p.Faction?.def.defName == "Rukaya_DrifterAlliance"
                && p.GetLord()?.LordJob is LordJob_TradeWithColony)
                __result = TraderCaravanRole.Carrier;
        }
    }

    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GenerateCarriers")]
    public static class Patch_RukayaGenerateCarriers
    {
        // Vanilla distributes stacks without checking their mass. Only the Alliance's dedicated
        // transports use this allocator; all other factions and all sold pawns keep vanilla behavior.
        public static bool Prefix(PawnGroupMakerParms parms, PawnGroupMaker groupMaker,
            List<Thing> wares, List<Pawn> outPawns)
        {
            if (parms.faction?.def.defName != "Rukaya_DrifterAlliance")
                return true;

            PawnKindDef kind = groupMaker.carriers.Select(x => x.kind)
                .FirstOrDefault(x => x.race.GetModExtension<RukayaTransportExtension>() != null);
            if (kind == null)
                return true;

            List<Thing> cargo = wares.Where(x => !(x is Pawn)).OrderByDescending(
                x => x.GetStatValue(StatDefOf.Mass) * x.stackCount).ToList();
            List<Pawn> carriers = new List<Pawn>();
            Pawn AddCarrier()
            {
                Pawn carrier = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind,
                    parms.faction, PawnGenerationContext.NonPlayer, parms.tile,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    fixedIdeo: parms.ideo, inhabitant: parms.inhabitants));
                carriers.Add(carrier);
                outPawns.Add(carrier);
                return carrier;
            }

            int initialCount = Math.Max(1, (cargo.Count + 7) / 8);
            for (int i = 0; i < initialCount; i++)
                AddCarrier();

            foreach (Thing stack in cargo)
            {
                while (!stack.Destroyed && stack.stackCount > 0)
                {
                    float unitMass = stack.GetStatValue(StatDefOf.Mass);
                    Pawn carrier = carriers.OrderByDescending(MassUtility.FreeSpace).First();
                    int count = FitCount(MassUtility.FreeSpace(carrier), unitMass, stack.stackCount);
                    if (count == 0)
                    {
                        carrier = AddCarrier();
                        count = FitCount(MassUtility.FreeSpace(carrier), unitMass, stack.stackCount);
                        if (count == 0)
                        {
                            // An indivisible modded item can exceed one vehicle's capacity. Keep it
                            // rather than loop forever or silently delete a merchant's stock.
                            Log.Warning("[Rukaya] A single cargo item exceeds transport capacity: " + stack.def.defName);
                            count = 1;
                        }
                    }

                    Thing portion = count == stack.stackCount ? stack : stack.SplitOff(count);
                    if (!carrier.inventory.innerContainer.TryAdd(portion, false))
                        throw new InvalidOperationException("[Rukaya] Could not load merchant cargo: " + portion);
                    if (ReferenceEquals(portion, stack))
                        break;
                }
            }
            return false;
        }

        public static int FitCount(float freeMass, float unitMass, int available)
        {
            if (unitMass <= 0f)
                return available;
            return Math.Max(0, Math.Min(available, (int)Math.Floor((freeMass + 0.0001f) / unitMass)));
        }
    }
}
