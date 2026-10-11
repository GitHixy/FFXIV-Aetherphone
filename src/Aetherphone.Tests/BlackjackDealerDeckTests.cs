using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Xunit;

namespace Aetherphone.Tests;

public sealed class BlackjackDealerDeckTests
{
    private const string Owner = "owner";
    private const string CoDealer = "co";
    private const string Player = "player";

    private static readonly CasinoBlackjackRoomStateDto HostDealt = new(Currency: CasinoCurrencies.Gil,
        DealerMode: CasinoDealerModes.Host, DealerUserId: Owner, CoDealers: new[] { CoDealer },
        Phase: BlackjackPhases.Betting);

    [Fact]
    public void TheOwnerOfAHostDealtTableGetsTheDealerDeckInsteadOfASeat()
    {
        var dealing = BlackjackHosting.DealsFromDeck(HostDealt, Owner, Owner, false);

        Assert.True(dealing);
        Assert.Equal(BlackjackDeck.Dealer, BlackjackDeckFlow.Pick(Facts(dealing: dealing, onBoard: false,
            holdsSeat: false)));
        Assert.True(BlackjackHosting.DealsFromDeck(HostDealt with { Currency = CasinoCurrencies.Practice },
            Owner, string.Empty, false));
    }

    [Fact]
    public void EveryoneElseKeepsTheSeatTheyWouldHaveHad()
    {
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt, CoDealer, Owner, false));
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt, Player, Owner, false));
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt, string.Empty, Owner, false));
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt with { DealerMode = CasinoDealerModes.House },
            Owner, Owner, false));
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt with { Currency = CasinoCurrencies.Chips },
            Owner, Owner, false));
        Assert.Equal(BlackjackDeck.Sit, BlackjackDeckFlow.Pick(Facts(onBoard: false, holdsSeat: false)));
    }

    [Fact]
    public void AnOwnerWhoIsAlreadySeatedPlaysTheirHandOut()
    {
        Assert.False(BlackjackHosting.DealsFromDeck(HostDealt, Owner, Owner, true));
        Assert.Equal(BlackjackDeck.Bet, BlackjackDeckFlow.Pick(Facts(canBet: true)));
    }

    [Fact]
    public void TheDealerDeckOffersDealOnlyOnceABetIsDown()
    {
        Assert.Equal(DealerDeck.WaitingBets, BlackjackHosting.DealerDeckOf(HostDealt));

        var betDown = HostDealt with
        {
            Seats = new[] { new CasinoBlackjackSeatDto(SeatIndex: 0, UserId: Player, Committed: 500) },
        };
        Assert.Equal(DealerDeck.Deal, BlackjackHosting.DealerDeckOf(betDown));
        Assert.Equal(DealerDeck.Paused, BlackjackHosting.DealerDeckOf(betDown with { Paused = true }));
        Assert.Equal(DealerDeck.HandLive,
            BlackjackHosting.DealerDeckOf(betDown with { Phase = BlackjackPhases.PlayerTurns }));
        Assert.Equal(DealerDeck.HandLive,
            BlackjackHosting.DealerDeckOf(betDown with { Phase = BlackjackPhases.DealerPlay }));
        Assert.Equal(DealerDeck.HandOver,
            BlackjackHosting.DealerDeckOf(betDown with { Phase = BlackjackPhases.Settlement }));
    }

    [Fact]
    public void ThePlayerDeckKeepsItsOrder()
    {
        Assert.Equal(BlackjackDeck.Sit, BlackjackDeckFlow.Pick(Facts(bought: false, canRebuy: true, canAct: true)));
        Assert.Equal(BlackjackDeck.Rebuy, BlackjackDeckFlow.Pick(Facts(canRebuy: true, gilBroke: true,
            insurance: true)));
        Assert.Equal(BlackjackDeck.Ledger, BlackjackDeckFlow.Pick(Facts(gilBroke: true, insurance: true)));
        Assert.Equal(BlackjackDeck.Insurance, BlackjackDeckFlow.Pick(Facts(insurance: true, canBet: true)));
        Assert.Equal(BlackjackDeck.Act, BlackjackDeckFlow.Pick(Facts(canBet: true, committed: true, canAct: true)));
        Assert.Equal(BlackjackDeck.Wait, BlackjackDeckFlow.Pick(Facts(canBet: true, committed: true)));
        Assert.Equal(BlackjackDeck.Wait, BlackjackDeckFlow.Pick(Facts(onBoard: false, canAct: true)));
    }

    private static BlackjackDeckFacts Facts(bool dealing = false, bool bought = true, bool onBoard = true,
        bool holdsSeat = true, bool canRebuy = false, bool gilBroke = false, bool insurance = false,
        bool canBet = false, bool committed = false, bool canAct = false) =>
        new(dealing, bought, onBoard, holdsSeat, canRebuy, gilBroke, insurance, canBet, committed, canAct);
}
