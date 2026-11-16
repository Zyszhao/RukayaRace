using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RukayaAviation;

public sealed class AircraftTradeDialog : Dialog_Trade
{
    private readonly AircraftTrader aircraftTrader;
    public AircraftTradeDialog(Pawn negotiator, AircraftTrader trader) : base(negotiator, trader) { aircraftTrader = trader; AddCargo(); }
    private bool AddCargo()
    {
        bool changed = false;
        foreach (Thing t in aircraftTrader.ColonyThingsWillingToBuy(TradeSession.playerNegotiator))
        {
            if (!TradeUtility.PlayerSellableNow(t, aircraftTrader) || TradeSession.deal.AllTradeables.Any(x => x.thingsColony.Contains(t))) continue;
            Tradeable matching = TransferableUtility.TradeableMatching(t, TradeSession.deal.AllTradeables);
            if (matching == null) { matching = t is Pawn ? new Tradeable_Pawn() : new Tradeable(); TradeSession.deal.AllTradeables.Add(matching); }
            matching.AddThing(t, Transactor.Colony); changed = true;
        }
        return changed;
    }
    public override void DoWindowContents(UnityEngine.Rect rect)
    {
        if (AddCargo()) typeof(Dialog_Trade).GetMethod("CacheTradeables", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(this, null);
        base.DoWindowContents(rect);
    }
}

// Keep the vanilla settlement stock, prices, goodwill and trade dialog; exchange directly with the aircraft hold.
public sealed class AircraftTrader : ITrader
{
    private readonly Aircraft plane;
    private readonly Settlement settlement;
    public AircraftTrader(Aircraft plane, Settlement settlement) { this.plane = plane; this.settlement = settlement; }
    public TraderKindDef TraderKind => settlement.TraderKind;
    public IEnumerable<Thing> Goods => settlement.Goods;
    public int RandomPriceFactorSeed => settlement.RandomPriceFactorSeed;
    public string TraderName => settlement.TraderName;
    public bool CanTradeNow => settlement.CanTradeNow;
    public float TradePriceImprovementOffsetForPlayer => settlement.TradePriceImprovementOffsetForPlayer;
    public Faction Faction => settlement.Faction;
    public TradeCurrency TradeCurrency => settlement.TradeCurrency;
    public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn negotiator)
        => plane.Cargo.innerContainer.Where(t => !(t is Pawn p) || !p.IsFreeColonist);
    public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn negotiator)
    {
        // The tracker supports cargo items without a caravan; pawn sales need a caravan inventory transfer.
        if (!(toGive is Pawn)) { settlement.GiveSoldThingToTrader(toGive, countToGive, negotiator); return; }
        Thing thing = toGive.SplitOff(countToGive); thing.PreTraded(TradeAction.PlayerSells, negotiator, settlement);
        settlement.trader.GetDirectlyHeldThings().TryAddOrTransfer(thing, false);
    }
    public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn negotiator)
    {
        Thing thing = toGive.SplitOff(countToGive); thing.PreTraded(TradeAction.PlayerBuys, negotiator, settlement);
        plane.Cargo.innerContainer.TryAddOrTransfer(thing, false);
    }
}
