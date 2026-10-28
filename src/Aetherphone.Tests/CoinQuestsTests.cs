using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Apps;
using Aetherphone.Core.Coins;
using Aetherphone.Core.Localization;
using Xunit;

namespace Aetherphone.Tests;

public sealed class CoinQuestsTests
{
    private static readonly string[] CatalogIds =
    {
        "play.featured", "play.three", "play.long", "play.match", "social.chat", "social.comment", "social.post",
        "social.story", "social.call", "explore.radio", "explore.poll", "explore.muster",
    };

    private static CoinQuestDto Quest(string id, int progress, int target, bool claimed = false) =>
        new(id, "play", "games", progress, target, 15, claimed);

    [Fact]
    public void QuestIsClaimableOnlyWhenProgressReachesTargetAndItIsUnclaimed()
    {
        Assert.False(CoinQuests.IsClaimable(Quest("play.three", 2, 3)));
        Assert.True(CoinQuests.IsClaimable(Quest("play.three", 3, 3)));
        Assert.False(CoinQuests.IsClaimable(Quest("play.three", 3, 3, true)));
        Assert.False(CoinQuests.IsClaimable(Quest("play.three", 0, 0)));
    }

    [Fact]
    public void FractionIsClampedAndFullOnceClaimed()
    {
        Assert.Equal(0f, CoinQuests.Fraction(Quest("play.three", 0, 3)));
        Assert.Equal(1f / 3f, CoinQuests.Fraction(Quest("play.three", 1, 3)), 4);
        Assert.Equal(1f, CoinQuests.Fraction(Quest("play.three", 9, 3)));
        Assert.Equal(1f, CoinQuests.Fraction(Quest("play.three", 0, 3, true)));
        Assert.Equal(0f, CoinQuests.Fraction(Quest("play.three", 4, 0)));
    }

    [Fact]
    public void ProgressTextCountsWithASlash()
    {
        Assert.Equal("1 / 3", CoinQuests.ProgressText(Quest("play.three", 1, 3)));
    }

    [Fact]
    public void RadioProgressTextCountsMinutes()
    {
        Assert.Equal("12 / 20 min", CoinQuests.ProgressText(Quest(CoinQuests.RadioQuestId, 12, 20)));
    }

    [Fact]
    public void ProgressTextNeverShowsMoreThanTheTarget()
    {
        Assert.Equal("3 / 3", CoinQuests.ProgressText(Quest("play.three", 7, 3)));
        Assert.Equal("0 / 3", CoinQuests.ProgressText(Quest("play.three", -2, 3)));
    }

    [Fact]
    public void EveryCatalogQuestHasItsOwnTitle()
    {
        for (var index = 0; index < CatalogIds.Length; index++)
        {
            var title = CoinQuests.TitleFor(CatalogIds[index]);
            Assert.NotEqual(L.Coin.RuleQuest.Key, title.Key);
        }
    }

    [Fact]
    public void UnknownQuestFallsBackToTheRuleLabel()
    {
        Assert.Equal(L.Coin.RuleQuest.Key, CoinQuests.TitleFor("explore.unknown").Key);
    }

    [Fact]
    public void EveryCatalogQuestHasItsOwnHintAndUnknownOnesHaveNone()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < CatalogIds.Length; index++)
        {
            Assert.True(CoinQuests.TryHintFor(CatalogIds[index], out var hint));
            Assert.True(seen.Add(hint.Key));
        }

        Assert.False(CoinQuests.TryHintFor("explore.unknown", out _));
        Assert.Equal(string.Empty, CoinQuests.Hint(Quest("explore.unknown", 0, 1)));
        Assert.Equal("Post a story in Aethergram", CoinQuests.Hint(Quest("social.story", 0, 1)));
    }

    [Fact]
    public void ClaimReasonsMapToTheirOwnLinesAndStaleOnesRefreshInstead()
    {
        Assert.Equal(L.Coin.PausedTitle.Key, CoinQuests.ReasonFor(CoinQuests.PausedReason).Key);
        Assert.Equal(L.Coin.FrozenTitle.Key, CoinQuests.ReasonFor(CoinQuests.FrozenReason).Key);
        Assert.Equal(L.Coin.CapReached.Key, CoinQuests.ReasonFor(CoinQuests.DailyCapReason).Key);
        Assert.Equal(L.Coin.QuestLimitReached.Key, CoinQuests.ReasonFor(CoinQuests.RuleCapReason).Key);
        Assert.Equal(L.Coin.QuestUnavailable.Key, CoinQuests.ReasonFor("unavailable").Key);
        Assert.Equal(L.Coin.QuestUnavailable.Key, CoinQuests.ReasonFor(string.Empty).Key);

        Assert.True(CoinQuests.IsStale(CoinQuests.IncompleteReason));
        Assert.True(CoinQuests.IsStale(CoinQuests.NotTodayReason));
        Assert.False(CoinQuests.IsStale(CoinQuests.AlreadyClaimedReason));
        Assert.False(CoinQuests.IsStale(CoinQuests.PausedReason));
    }

    [Fact]
    public void TitlesCarryTheServerTarget()
    {
        Assert.Equal("Listen to community radio for 20 minutes",
            CoinQuests.Title(Quest(CoinQuests.RadioQuestId, 0, 20)));
        Assert.Equal("Play a game for three minutes, 3 times", CoinQuests.Title(Quest("play.three", 0, 3)));
        Assert.Equal("Post a story", CoinQuests.Title(Quest("social.story", 0, 1)));
    }

    [Fact]
    public void WithClaimedMarksOnlyTheClaimedQuest()
    {
        var board = new CoinQuestBoardDto(10, 1000,
            new[] { Quest("play.three", 3, 3), Quest("social.chat", 1, 2), Quest("explore.poll", 1, 1) });

        var updated = CoinQuests.WithClaimed(board, "play.three");

        Assert.True(updated.Quests[0].Claimed);
        Assert.False(updated.Quests[1].Claimed);
        Assert.False(updated.Quests[2].Claimed);
        Assert.False(board.Quests[0].Claimed);
    }

    [Fact]
    public void DailyQuestRuleHasALabelAndHint()
    {
        Assert.Equal(L.Coin.RuleQuest.Key, CoinRuleLabels.For("quest.daily").Key);
        Assert.True(CoinRuleLabels.TryHint("quest.daily", out var hint));
        Assert.Equal(L.Coin.RuleQuestHint.Key, hint.Key);
    }

    [Fact]
    public void PostAndStoryQuestsLaunchTheirComposerUntilDone()
    {
        Assert.True(CoinQuests.TryLaunchFor(Quest("social.post", 0, 1), out var post));
        Assert.Equal(SocialLinkKind.Compose, post.Kind);
        Assert.True(CoinQuests.TryLaunchFor(Quest("social.story", 0, 1), out var story));
        Assert.Equal(SocialLinkKind.ComposeStory, story.Kind);
        Assert.False(CoinQuests.TryLaunchFor(Quest("social.post", 1, 1), out _));
        Assert.False(CoinQuests.TryLaunchFor(Quest("social.story", 0, 1, true), out _));
        Assert.False(CoinQuests.TryLaunchFor(Quest("social.comment", 0, 2), out _));
    }

    private static CoinQuestDto Standing(int progress, bool claimed) =>
        new(CoinQuests.ReelQuestId, "standing", "aethergram", progress, 1, 35, claimed, true);

    [Fact]
    public void TheReelQuestMovesFromOpenToAwaitingPayToPaid()
    {
        Assert.Equal(CoinQuestState.InProgress, CoinQuests.StateOf(Standing(0, false)));
        Assert.Equal(CoinQuestState.AwaitingAutoPay, CoinQuests.StateOf(Standing(1, false)));
        Assert.Equal(CoinQuestState.Paid, CoinQuests.StateOf(Standing(1, true)));
    }

    [Fact]
    public void ADailyQuestWithoutAutoPayIsClaimableWhenDone()
    {
        Assert.Equal(CoinQuestState.InProgress, CoinQuests.StateOf(Quest("play.three", 2, 3)));
        Assert.Equal(CoinQuestState.Claimable, CoinQuests.StateOf(Quest("play.three", 3, 3)));
        Assert.Equal(CoinQuestState.Paid, CoinQuests.StateOf(Quest("play.three", 3, 3, true)));
    }

    [Fact]
    public void AnAutoPaidQuestIsNeverClaimedByTheClient()
    {
        Assert.False(CoinQuests.IsClaimable(Standing(1, false)));
    }

    [Fact]
    public void StandingQuestsFollowTheDailyThree()
    {
        var board = new CoinQuestBoardDto(10, 1000,
            new[] { Quest("play.three", 0, 3), Quest("social.chat", 0, 2), Quest("explore.poll", 0, 1) },
            new[] { Standing(0, false) });

        Assert.Equal(4, CoinQuests.RowCount(board));
        Assert.Equal("play.three", CoinQuests.RowAt(board, 0).Id);
        Assert.Equal(CoinQuests.ReelQuestId, CoinQuests.RowAt(board, 3).Id);
    }

    [Fact]
    public void ABoardFromAnOlderServerHasNoStandingRows()
    {
        var board = new CoinQuestBoardDto(10, 1000, new[] { Quest("play.three", 0, 3) });

        Assert.Equal(1, CoinQuests.RowCount(board));
        Assert.Empty(CoinQuests.Standing(board));
    }

    [Fact]
    public void TheReelQuestHasItsTitleHintAndOpensTheReelComposer()
    {
        Assert.Equal("Post a reel", CoinQuests.Title(Standing(0, false)));
        Assert.True(CoinQuests.TryHintFor(CoinQuests.ReelQuestId, out _));
        Assert.True(CoinQuests.TryLaunchFor(Standing(0, false), out var link));
        Assert.Equal(SocialLinkKind.ComposeReel, link.Kind);
        Assert.False(CoinQuests.TryLaunchFor(Standing(1, false), out _));
    }
}
