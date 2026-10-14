using Aetherphone.Apps.Casino;
using Aetherphone.Apps.Casino.Live;
using Aetherphone.Apps.Casino.Strip;
using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Xunit;

namespace Aetherphone.Tests;

public sealed class LivePageTests
{
    [Fact]
    public void JoinLightsUpForACodeOrAPastedInviteOnly()
    {
        Assert.Equal(LiveJoinInput.Code, LiveBoard.Classify("YUCPHJ"));
        Assert.Equal(LiveJoinInput.Code, LiveBoard.Classify("yuc-phj"));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify("YUCPH"));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify("YUCPHJK"));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify("YUCPH0"));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify(string.Empty));
        Assert.Equal(LiveJoinInput.Token, LiveBoard.Classify(CasinoShare.Compose("table-0123456789abcdef")));
        Assert.Equal(LiveJoinInput.Token, LiveBoard.Classify("  table-0123456789abcdef  "));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify("table-0123"));
        Assert.Equal(LiveJoinInput.None, LiveBoard.Classify("[aep.casino.v1:not a table id]"));
    }

    [Fact]
    public void TheCountdownRingDrainsOverThePhaseWindow()
    {
        var window = LiveBoard.WindowSeconds(CasinoGames.Wheel, CasinoRoomPhases.Open);

        Assert.Equal(CasinoRoomCadence.WheelOpenSeconds, window);
        Assert.Equal(1f, LiveBoard.RingFraction(window * 1000L, window));
        Assert.Equal(0.5f, LiveBoard.RingFraction(window * 500L, window), 3);
        Assert.Equal(1f, LiveBoard.RingFraction(window * 4000L, window));
        Assert.Equal(0f, LiveBoard.RingFraction(0, window));
        Assert.Equal(0f, LiveBoard.RingFraction(1_000, 0));
        Assert.Equal(CasinoRoomCadence.BingoLockedSeconds,
            LiveBoard.WindowSeconds(CasinoGames.Bingo, CasinoRoomPhases.Locked));
        Assert.Equal(CasinoRoomCadence.RaceResultSeconds,
            LiveBoard.WindowSeconds(CasinoGames.Race, CasinoRoomPhases.Result));
    }

    [Fact]
    public void YourTableCountsTheDoorWhenItIsCachedAndTheCardOtherwise()
    {
        var row = Hosted() with { SeatedCount = 2, Occupancy = 5 };
        var door = new CasinoTableDoorDto("t-1", true,
            Knocks: new[] { new CasinoTableKnockDto("u-knock"), new CasinoTableKnockDto("u-other") },
            Seated: new[] { new CasinoTableSeatedDto("u-anna") },
            Present: new[] { new CasinoTablePresentDto("u-bran", Attached: true) });

        Assert.Equal(2, LiveBoard.HereOf(row, door));
        Assert.Equal(2, LiveBoard.KnockingOf(door));
        Assert.Equal(5, LiveBoard.HereOf(row, null));
        Assert.Equal(0, LiveBoard.KnockingOf(null));
        Assert.Equal(4, LiveBoard.HereOf(row with { Occupancy = 0, SeatedCount = 4 }, null));
    }

    [Fact]
    public void HereCountsEachAttachedPlayerOnceAndNeverTheHost()
    {
        var row = Hosted();
        var door = new CasinoTableDoorDto("t-1", true,
            Seated: new[]
            {
                new CasinoTableSeatedDto(row.OwnerUserId), new CasinoTableSeatedDto("u-anna"),
                new CasinoTableSeatedDto("u-anna"),
            },
            Present: new[]
            {
                new CasinoTablePresentDto("u-anna", Attached: true), new CasinoTablePresentDto("u-bran", Attached: true),
                new CasinoTablePresentDto("u-bran", Attached: true), new CasinoTablePresentDto("u-away"),
                new CasinoTablePresentDto(row.OwnerUserId, Attached: true),
            });

        Assert.Equal(2, LiveBoard.HereOf(row, door));
        Assert.Equal(0, LiveBoard.HereOf(row, new CasinoTableDoorDto("t-1", true)));
    }

    [Fact]
    public void OnlyASeatAtAHostedTablePinsYourTable()
    {
        var hosted = Hosted();
        var rack = new CasinoSittingDto(TableId: "t-1");

        Assert.True(LiveBoard.IsHostedSeat(hosted, rack));
        Assert.False(LiveBoard.IsHostedSeat(hosted with { Kind = CasinoTableKinds.House }, rack));
        Assert.False(LiveBoard.IsHostedSeat(hosted, rack with { TableId = "t-2" }));
        Assert.False(LiveBoard.IsHostedSeat(null, rack));
        Assert.False(LiveBoard.IsHostedSeat(hosted, null));
    }

    [Fact]
    public void ANamedTableNamesItsHostInTheLine()
    {
        var view = LiveBoard.ViewOf(Hosted() with { Name = "Velvet Room 21", Currency = CasinoCurrencies.Gil });

        Assert.Equal("Velvet Room 21", view.Name);
        Assert.Equal(Loc.T(L.LivePage.LineJoin, "Ysolde Vance", Loc.T(L.Casino.TableSeats, "3", "6")), view.Line);
        Assert.Equal(CasinoCurrencies.Gil, view.Currency);
        Assert.False(view.Full);
    }

    [Fact]
    public void AnUnnamedChipTableLeadsWithTheGameAndItsStakes()
    {
        var view = LiveBoard.ViewOf(Hosted());
        var stakes = Loc.T(L.LivePage.Stakes, NumberText.Compact(25_000), NumberText.Compact(1_000_000));
        var lead = Loc.T(L.LivePage.LineJoin, Loc.T(L.Casino.GameBlackjack), stakes);

        Assert.Equal(Loc.T(L.Casino.TableHostedBy, "Ysolde Vance"), view.Name);
        Assert.Equal(Loc.T(L.LivePage.LineJoin, lead, Loc.T(L.Casino.TableSeats, "3", "6")), view.Line);
        Assert.Equal(CasinoCurrencies.Chips, view.Currency);
    }

    [Fact]
    public void HoldemShowsBlindsAndVenueRoomsCountTheRoom()
    {
        var holdem = LiveBoard.ViewOf(Hosted() with { GameKind = HoldemRules.Kind, MinBet = 50, MaxBet = 100 });
        Assert.Contains(Loc.T(L.LivePage.Blinds, NumberText.Compact(50), NumberText.Compact(100)), holdem.Line,
            StringComparison.Ordinal);

        var raffle = LiveBoard.ViewOf(Hosted() with
        {
            GameKind = VenueKinds.Raffle,
            Name = "Lucky Raffle",
            Currency = CasinoCurrencies.Gil,
            Occupancy = 12,
            SeatedCount = 0,
            MaxSeats = 0,
        });
        Assert.Equal(Loc.T(L.LivePage.LineJoin, "Ysolde Vance", Loc.T(L.LivePage.InRoom, "12")), raffle.Line);
        Assert.False(raffle.Full);
    }

    [Fact]
    public void ABookReadsAsALedgerAndAPracticeTableAsFreePlay()
    {
        var book = LiveBoard.ViewOf(Hosted() with
        {
            GameKind = CasinoWire.BookKind,
            Currency = CasinoCurrencies.Gil,
            MaxSeats = 0,
            SeatedCount = 0,
        });
        Assert.Equal(Loc.T(L.Book.HostedBy, "Ysolde Vance"), book.Name);
        Assert.Equal(Loc.T(L.Book.Plays), book.Line);
        Assert.False(book.Full);

        var practice = LiveBoard.ViewOf(Hosted() with { Currency = CasinoCurrencies.Practice, Practice = true });
        Assert.Equal(CasinoCurrencies.Practice, practice.Currency);
        Assert.Equal(L.LivePage.FilterFreePlay, LiveArt.CurrencyName(practice.Currency));
        Assert.DoesNotContain(Loc.T(L.LivePage.Stakes, NumberText.Compact(25_000), NumberText.Compact(1_000_000)),
            practice.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void KnockListingsFullTablesAndPausesShowOnTheRow()
    {
        Assert.True(LiveBoard.ViewOf(Hosted() with { Listing = CasinoListings.Knock }).Knock);
        Assert.False(LiveBoard.ViewOf(Hosted() with { Listing = CasinoListings.Open }).Knock);
        Assert.True(LiveBoard.ViewOf(Hosted() with { SeatedCount = 6 }).Full);
        Assert.True(LiveBoard.ViewOf(Hosted() with { Paused = true }).Paused);
    }

    [Fact]
    public void EveryFilterHasItsOwnEmptyLine()
    {
        Assert.Equal(L.LivePage.EmptyAll, LiveBoard.EmptyOf(LiveFilter.All));
        Assert.Equal(L.LivePage.EmptyChips, LiveBoard.EmptyOf(LiveFilter.Chips));
        Assert.Equal(L.LivePage.EmptyGil, LiveBoard.EmptyOf(LiveFilter.Gil));
        Assert.Equal(L.LivePage.EmptyFreePlay, LiveBoard.EmptyOf(LiveFilter.FreePlay));
        Assert.Equal(L.LivePage.FilterFreePlay, LiveBoard.LabelOf(LiveFilter.FreePlay));
    }

    [Fact]
    public void TheHoldemSheetSeatsUntilARoomIsFullOrLocked()
    {
        var open = HoldemPit.HouseView(HoldemRules.LowTier, null, true);
        var full = HoldemPit.HouseView(HoldemRules.MidTier,
            new CasinoTableRowDto(HoldemRules.MidRoom, HoldemRules.Kind, CasinoTableKinds.House, MaxSeats: 6,
                SeatedCount: 6), true);
        var royal = HoldemPit.HouseView(HoldemRules.RoyalTier, null, true);

        Assert.True(HoldemRoomsSheet.Seats(open));
        Assert.False(HoldemRoomsSheet.Seats(full));
        Assert.False(HoldemRoomsSheet.Seats(royal));
        Assert.True(HoldemRoomsSheet.Seats(HoldemPit.HouseView(HoldemRules.RoyalTier, null, false)));
    }

    private static CasinoTableRowDto Hosted() =>
        new("t-1", CasinoWire.BlackjackKind, CasinoTableKinds.Private, OwnerUserId: "u-ysolde",
            OwnerName: "Ysolde Vance", MinBet: 25_000, MaxBet: 1_000_000, MaxSeats: 6, SeatedCount: 3, Occupancy: 3);
}
