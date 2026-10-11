using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Apps.Casino.Venue;
using Aetherphone.Core.Casino;
using Aetherphone.Core.Localization;
using Xunit;

namespace Aetherphone.Tests;

public sealed class VenuePostersTests
{
    [Fact]
    public void TheRailShowsEveryHostableGameOnceInTheVenueOrder()
    {
        Assert.Equal(HostGames.All.Length, VenuePosters.Games.Length);
        Assert.Equal(HostGame.Blackjack, VenuePosters.Games[0]);
        Assert.Equal(HostGame.Holdem, VenuePosters.Games[1]);
        Assert.Equal(HostGame.DiceTable, VenuePosters.Games[2]);
        Assert.Equal(HostGame.Deathroll, VenuePosters.Games[3]);
        Assert.Equal(HostGame.Raffle, VenuePosters.Games[4]);
        for (var index = 0; index < VenuePosters.Games.Length; index++)
        {
            Assert.Equal(index, Array.IndexOf(VenuePosters.Games, VenuePosters.Games[index]));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryPlaysForLineAgreesWithWhatTheHostSheetAccepts(bool gilOpen)
    {
        for (var index = 0; index < VenuePosters.Games.Length; index++)
        {
            var game = VenuePosters.Games[index];
            var meta = VenuePosters.MetaOf(game, gilOpen).Key;
            var saysGil = Is(meta, L.VenueHub.PlaysGilPracticeChips) || Is(meta, L.VenueHub.PlaysGilPractice)
                          || Is(meta, L.VenueHub.PlaysGilFree);
            var saysChips = Is(meta, L.VenueHub.PlaysGilPracticeChips) || Is(meta, L.VenueHub.PlaysChipsPractice);
            Assert.Equal(HostGames.Accepts(game, CasinoCurrencies.Gil, gilOpen), saysGil);
            Assert.Equal(HostGames.Accepts(game, CasinoCurrencies.Chips, gilOpen), saysChips);
            Assert.True(HostGames.Accepts(game, CasinoCurrencies.Practice, gilOpen));
            Assert.Equal(saysGil, VenuePosters.ShowsGilBadge(game, gilOpen));
            Assert.Equal(saysGil, VenuePosters.PrefersGil(game, gilOpen));
        }
    }

    [Fact]
    public void TheRaffleNamesFreeTicketsAndHoldemNeverNamesGil()
    {
        Assert.Equal(L.VenueHub.PlaysGilFree.Key, VenuePosters.MetaOf(HostGame.Raffle, true).Key);
        Assert.Equal(L.VenueHub.PlaysFreeTickets.Key, VenuePosters.MetaOf(HostGame.Raffle, false).Key);
        Assert.Equal(L.VenueHub.PlaysChipsPractice.Key, VenuePosters.MetaOf(HostGame.Holdem, true).Key);
        Assert.Equal(L.VenueHub.PlaysGilPracticeChips.Key, VenuePosters.MetaOf(HostGame.Blackjack, true).Key);
        Assert.Equal(L.VenueHub.PlaysChipsPractice.Key, VenuePosters.MetaOf(HostGame.Blackjack, false).Key);
        Assert.Equal(L.VenueHub.PlaysGilPractice.Key, VenuePosters.MetaOf(HostGame.DiceTable, true).Key);
        Assert.Equal(L.VenueHub.PlaysPractice.Key, VenuePosters.MetaOf(HostGame.Deathroll, false).Key);
        Assert.False(VenuePosters.ShowsGilBadge(HostGame.Holdem, true));
        Assert.False(VenuePosters.ShowsGilBadge(HostGame.Blackjack, false));
    }

    private static bool Is(string key, LocString entry) => string.Equals(key, entry.Key, StringComparison.Ordinal);
}
