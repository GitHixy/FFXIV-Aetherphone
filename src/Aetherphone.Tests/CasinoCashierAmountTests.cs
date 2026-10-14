using Aetherphone.Apps.Casino;
using Xunit;

namespace Aetherphone.Tests;

public sealed class CasinoCashierAmountTests
{
    private const long Rate = 1_000;

    [Fact]
    public void TheFieldReadsWholeCoinsOnly()
    {
        Assert.Equal(0, CashierAmounts.Parse(string.Empty));
        Assert.Equal(0, CashierAmounts.Parse("0"));
        Assert.Equal(50, CashierAmounts.Parse("50"));
        Assert.Equal(50, CashierAmounts.Parse("0050"));
        Assert.Equal(0, CashierAmounts.Parse("1.5"));
        Assert.Equal(0, CashierAmounts.Parse("-3"));
        Assert.Equal(0, CashierAmounts.Parse(" 5"));
        Assert.Equal(1_234_567_890_123, CashierAmounts.Parse("1234567890123"));
        Assert.Equal(0, CashierAmounts.Parse("12345678901234"));
        Assert.Equal("2000", CashierAmounts.Format(2_000));
        Assert.Equal(string.Empty, CashierAmounts.Format(0));
    }

    [Fact]
    public void BuyTilesAreFiftyTwoHundredTwoThousandAndTheWallet()
    {
        var tiles = new long[CashierAmounts.TileCount];

        Assert.Equal(4, CashierAmounts.BuyTiles(12_480, tiles));
        Assert.Equal(new long[] { 50, 200, 2_000, 12_480 }, tiles);
        Assert.Equal(4, CashierAmounts.BuyTiles(-5, tiles));
        Assert.Equal(0, tiles[CashierAmounts.MaxTileIndex]);
    }

    [Fact]
    public void CashOutTilesSplitTheChipValueIntoQuarters()
    {
        var tiles = new long[CashierAmounts.TileCount];

        Assert.Equal(4, CashierAmounts.CashOutTiles(1_240, true, tiles));
        Assert.Equal(new long[] { 310, 620, 930, 1_240 }, tiles);
        Assert.Equal(4, CashierAmounts.CashOutTiles(3, true, tiles));
        Assert.Equal(new long[] { 0, 1, 2, 3 }, tiles);
    }

    [Fact]
    public void WithoutPartialCashOutOnlyTheAllTileShows()
    {
        var tiles = new long[CashierAmounts.TileCount];

        Assert.Equal(1, CashierAmounts.CashOutTiles(1_240, false, tiles));
        Assert.Equal(1_240, tiles[0]);
    }

    [Fact]
    public void ATileLightsOnlyWhenItMatchesTheField()
    {
        Assert.True(CashierAmounts.TileOn(50, 50));
        Assert.False(CashierAmounts.TileOn(50, 49));
        Assert.False(CashierAmounts.TileOn(0, 0));
    }

    [Fact]
    public void ABuyIsReadyUpToTheWalletAndOverPastIt()
    {
        Assert.Equal(CashierAmountState.Idle, CashierAmounts.BuyState(0, 100));
        Assert.Equal(CashierAmountState.Ready, CashierAmounts.BuyState(1, 100));
        Assert.Equal(CashierAmountState.Ready, CashierAmounts.BuyState(100, 100));
        Assert.Equal(CashierAmountState.Over, CashierAmounts.BuyState(101, 100));
        Assert.Equal(CashierAmountState.Over, CashierAmounts.BuyState(1, 0));
    }

    [Fact]
    public void TheWholeValueClosesTheBankrollAndLessIsAPartialCashOut()
    {
        const long stack = 1_240_500;

        var whole = CashierAmounts.PlanCashOut(1_240, stack, Rate, true);
        Assert.Equal(CashierAmountState.Ready, whole.State);
        Assert.Equal(CashOutRoute.Whole, whole.Route);

        var partial = CashierAmounts.PlanCashOut(310, stack, Rate, true);
        Assert.Equal(CashierAmountState.Ready, partial.State);
        Assert.Equal(CashOutRoute.Partial, partial.Route);
        Assert.Equal(310, partial.Coins);

        var over = CashierAmounts.PlanCashOut(1_241, stack, Rate, true);
        Assert.Equal(CashierAmountState.Over, over.State);
        Assert.Equal(CashOutRoute.None, over.Route);

        Assert.Equal(CashierAmountState.Idle, CashierAmounts.PlanCashOut(0, stack, Rate, true).State);
    }

    [Fact]
    public void AnOldServerNeverGetsAPartialCashOut()
    {
        const long stack = 1_240_500;

        Assert.Equal(CashOutRoute.Whole, CashierAmounts.PlanCashOut(1_240, stack, Rate, false).Route);
        var refused = CashierAmounts.PlanCashOut(310, stack, Rate, false);
        Assert.Equal(CashOutRoute.None, refused.Route);
        Assert.NotEqual(CashierAmountState.Ready, refused.State);
    }

    [Fact]
    public void ChipsBelowOneCoinCannotBeCashedOut()
    {
        Assert.Equal(CashierAmountState.Over, CashierAmounts.PlanCashOut(1, 999, Rate, true).State);
        Assert.Equal(CashierAmountState.Idle, CashierAmounts.PlanCashOut(0, 999, Rate, true).State);
    }

    [Fact]
    public void TheBuyPreviewShowsWhatMovesBeforeAnythingDoes()
    {
        var preview = CashierAmounts.BuyPreview(2_000, Rate, 12_480, 1_240_500);

        Assert.Equal(2_000, preview.Pay);
        Assert.Equal(2_000_000, preview.Get);
        Assert.Equal(10_480, preview.WalletAfter);
        Assert.Equal(3_240_500, preview.ChipsAfter);
    }

    [Fact]
    public void ThePartialPreviewTakesOnlyTheCoinsWorthOfChips()
    {
        var plan = CashierAmounts.PlanCashOut(310, 1_240_500, Rate, true);
        var preview = CashierAmounts.CashOutPreview(plan, Rate, 12_480, 1_240_500);

        Assert.Equal(310_000, preview.Pay);
        Assert.Equal(310, preview.Get);
        Assert.Equal(12_790, preview.WalletAfter);
        Assert.Equal(930_500, preview.ChipsAfter);
    }

    [Fact]
    public void TheWholePreviewTakesEveryChipIncludingTheRemainder()
    {
        var plan = CashierAmounts.PlanCashOut(1_240, 1_240_500, Rate, true);
        var preview = CashierAmounts.CashOutPreview(plan, Rate, 12_480, 1_240_500);

        Assert.Equal(1_240_500, preview.Pay);
        Assert.Equal(1_240, preview.Get);
        Assert.Equal(13_720, preview.WalletAfter);
        Assert.Equal(0, preview.ChipsAfter);
    }
}
