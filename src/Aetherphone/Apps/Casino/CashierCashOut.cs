using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;

namespace Aetherphone.Apps.Casino;

internal sealed class CashierCashOut
{
    private const long RiskCheckTimeoutMilliseconds = 4_000;

    private readonly CasinoStore store;
    private readonly CasinoTextCache texts = new();
    private readonly CashierField amount = new("##cashierCashOutCoins");
    private readonly long[] tiles = new long[CashierAmounts.TileCount];
    private int tileCount;
    private long checkedCoins;
    private long riskCheckAtTick;

    public CashierCashOut(CasinoStore store)
    {
        this.store = store;
    }

    public CashierField Field => amount;

    public bool Partial => store.HasFeature(CasinoFeatures.PartialCashOut);

    public bool Checking => riskCheckAtTick != 0;

    public void Clear()
    {
        amount.Clear();
    }

    public void Cancel()
    {
        riskCheckAtTick = 0;
    }

    public void Sync(long stackChips, long rate, bool partial)
    {
        if (!partial)
        {
            amount.Set(ChipValue.Coins(stackChips, rate));
        }
    }

    public int Tiles(long wholeCoins, bool partial)
    {
        tileCount = CashierAmounts.CashOutTiles(wholeCoins, partial, tiles);
        return tileCount;
    }

    public long Tile(int index) => tiles[index];

    public string TileLabel(int index) => index == tileCount - 1
        ? Loc.T(L.CashierDesk.TileAll)
        : texts.Number(L.CashierDesk.TilePercent, CashierAmounts.CashOutPercents[index]);

    public void Pick(int index)
    {
        amount.Set(tiles[index]);
    }

    public CashOutPlan Plan(long stackChips, long rate, bool partial) =>
        CashierAmounts.PlanCashOut(amount.Coins, stackChips, rate, partial);

    public string Status(in CashOutPlan plan, long stackChips, long rate, out CashierTone tone)
    {
        switch (plan.State)
        {
            case CashierAmountState.Ready:
                tone = CashierTone.Ready;
                return texts.Number(L.CashierDesk.CashOutReady,
                    plan.Route == CashOutRoute.Whole ? stackChips : plan.Coins * rate);
            case CashierAmountState.Over:
                tone = CashierTone.Warn;
                return texts.Number(L.CashierDesk.CashOutOver, ChipValue.Coins(stackChips, rate));
            default:
                tone = CashierTone.Idle;
                return texts.Number(L.CashierDesk.CashOutIdle, ChipValue.Coins(stackChips, rate));
        }
    }

    public string Primary(in CashOutPlan plan, bool compact)
    {
        if (plan.State != CashierAmountState.Ready)
        {
            return Loc.T(L.Casino.CashOut);
        }

        return compact
            ? texts.Compact(L.CashierDesk.CashOutAction, plan.Coins)
            : texts.Number(L.CashierDesk.CashOutAction, plan.Coins);
    }

    public string ResultLine(long coins) => texts.Number(L.Strip.CashedOut, coins);

    public void BeginCheck(long coins)
    {
        checkedCoins = coins;
        riskCheckAtTick = Environment.TickCount64;
        store.RefreshNow();
    }

    public bool ResolveCheck(out long coins, out bool atRisk)
    {
        coins = checkedCoins;
        atRisk = false;
        if (riskCheckAtTick == 0)
        {
            return false;
        }

        var fresh = store.StateLoadedAtTick >= riskCheckAtTick;
        if (!fresh && Environment.TickCount64 - riskCheckAtTick < RiskCheckTimeoutMilliseconds)
        {
            return false;
        }

        riskCheckAtTick = 0;
        atRisk = (store.State?.AtRisk ?? 0) > 0;
        return true;
    }

    public bool Execute(in CashOutPlan plan)
    {
        switch (plan.Route)
        {
            case CashOutRoute.Whole when !store.MovingMoney:
                store.CloseSitting();
                return true;
            case CashOutRoute.Partial:
                return store.CashOutChips(plan.Coins);
            default:
                return false;
        }
    }
}
