using Aetherphone.Apps.Casino;
using Aetherphone.Apps.Casino.Strip;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Onboarding;
using Xunit;

namespace Aetherphone.Tests;

public sealed class FloorPageTests
{
    private const float PhoneContent = 328f;

    [Fact]
    public void CardsAndSlotsTakeThreeColumnsAndQuickBetsTakeFour()
    {
        Assert.Equal(3, StripCatalog.ColumnsOf(StripShelf.Cards));
        Assert.Equal(3, StripCatalog.ColumnsOf(StripShelf.Machines));
        Assert.Equal(4, StripCatalog.ColumnsOf(StripShelf.QuickBets));
        Assert.Equal(104f, StripGrid.CellWidth(PhoneContent, StripShelves.CardColumns, StripShelves.GridGap), 3);
        Assert.Equal(76f, StripGrid.CellWidth(PhoneContent, StripShelves.QuickColumns, StripShelves.GridGap), 3);
    }

    [Fact]
    public void EveryShelfPicksItsPosterLayoutAndSubline()
    {
        Assert.True(StripCatalog.LayoutOf(StripShelf.Cards).CapsuleBand);
        Assert.False(StripCatalog.LayoutOf(StripShelf.Machines).CapsuleBand);
        Assert.True(StripCatalog.LayoutOf(StripShelf.QuickBets).CapsuleBand);
        Assert.True(StripCatalog.LayoutOf(StripShelf.QuickBets).Centered);
        Assert.Equal(L.FloorPage.CardsSubline.Key, StripCatalog.SublineOf(StripShelf.Cards).Key);
        Assert.Equal(L.FloorPage.SlotsSubline.Key, StripCatalog.SublineOf(StripShelf.Machines).Key);
        Assert.Equal(L.FloorPage.QuickSubline.Key, StripCatalog.SublineOf(StripShelf.QuickBets).Key);
    }

    [Fact]
    public void DealerHoldemWearsAShortSignSoItReadsInAThirdOfThePhone()
    {
        var cards = StripCatalog.EntriesOf(StripShelf.Cards);
        Assert.Equal("21", StripCatalog.SignOf(cards[0]).ToString());
        Assert.Equal("DEALER", StripCatalog.SignOf(cards[2]).ToString());
        Assert.True(CasinoSigns.Measure(StripCatalog.SignOf(cards[2]), 1f)
                    < CasinoSigns.Measure(CasinoSign.DealerHoldem, 1f));
    }

    [Fact]
    public void TheNeonSignBuzzesButNeverGoesDark()
    {
        Assert.Equal(1f, NeonFlicker.Level(0f, 6.5f), 3);
        Assert.Equal(0.55f, NeonFlicker.Level(0.46f * 6.5f, 6.5f), 3);
        Assert.Equal(1f, NeonFlicker.Level(6.5f, 6.5f), 3);
        Assert.Equal(1f, NeonFlicker.Level(3f, 0f));
        for (var step = 0; step < 200; step++)
        {
            Assert.InRange(NeonFlicker.Level(step * 0.07f, 5f), 0.55f, 1f);
        }
    }

    [Fact]
    public void LiveRoomBarsCountDownTheWindowOfTheRoundPhase()
    {
        Assert.Equal(0.5f, FloorRoomCard.Countdown(12_500, 25), 3);
        Assert.Equal(0f, FloorRoomCard.Countdown(-5, 25));
        Assert.Equal(1f, FloorRoomCard.Countdown(90_000, 25));
        Assert.Equal(0f, FloorRoomCard.Countdown(1_000, 0));
        Assert.Equal(CasinoRoomCadence.BingoOpenSeconds, CasinoApp.RoomWindow(CasinoGames.Bingo, CasinoRoomPhases.Open));
        Assert.Equal(CasinoRoomCadence.RaceLockedSeconds,
            CasinoApp.RoomWindow(CasinoGames.Race, CasinoRoomPhases.Locked));
        Assert.Equal(CasinoRoomCadence.WheelResultSeconds,
            CasinoApp.RoomWindow(CasinoGames.Wheel, CasinoRoomPhases.Result));
    }

    [Fact]
    public void FloorRoomCardsGrowWithTheirTextButNeverShrinkBelowTheDesign()
    {
        Assert.Equal(FloorRoomCard.MinHeight, FloorRoomCard.Height(20f, 17f, 1f));
        var tall = FloorRoomCard.Height(44f, 36f, 1f);
        Assert.True(tall > FloorRoomCard.MinHeight);
        Assert.Equal(tall * 1.5f, FloorRoomCard.Height(66f, 54f, 1.5f), 3);
        Assert.Equal(84f, FloorRoomCard.TextWidth(104f, 1f));
    }

    [Fact]
    public void ShortcutTilesGrowForATwoLineLabel()
    {
        Assert.Equal(FloorShortcuts.TileHeight, FloorShortcuts.Height(18f, 1f));
        Assert.Equal(12f + 36f + 7f + 60f, FloorShortcuts.Height(60f, 1f));
        Assert.Equal(72f, FloorShortcuts.LabelWidth(76f, 1f));
        Assert.Equal(1f, FloorShortcuts.LabelWidth(2f, 1f));
    }

    [Fact]
    public void SectionSublinesSitBesideTheTitleOnlyWhenBothFit()
    {
        Assert.True(FloorHeader.SublineInline(100f, 150f, PhoneContent, FloorHeader.TrailingGap));
        Assert.False(FloorHeader.SublineInline(200f, 150f, PhoneContent, FloorHeader.TrailingGap));
    }

    [Fact]
    public void TheJackpotMarqueeKeepsItsCapsuleHeightUnlessTheAmountNeedsMore()
    {
        Assert.Equal(JackpotMarquee.Height, JackpotMarquee.HeightFor(24f, 1f));
        Assert.Equal(40f + JackpotMarquee.PadY * 2f, JackpotMarquee.HeightFor(40f, 1f));
    }

    [Fact]
    public void TheCasinoTourPointsAtTheShortcutsInsteadOfTheRecordsRow()
    {
        Assert.True(TourRegistry.TryGetAppTour("casino", out var tour));
        Assert.Equal(5, tour.ContentVersion);
        var anchors = new List<string?>();
        for (var index = 0; index < tour.Steps.Length; index++)
        {
            anchors.Add(tour.Steps[index].AnchorKey);
        }

        Assert.Contains("casino.shelves", anchors);
        Assert.Contains("casino.tab.live", anchors);
        Assert.Equal("casino.shortcuts", anchors[^1]);
        Assert.DoesNotContain("casino.records", anchors);
    }
}
