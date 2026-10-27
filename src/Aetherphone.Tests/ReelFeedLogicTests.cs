using System.Text.Json;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Reels;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReelPagingTests
{
    [Fact]
    public void AnEmptyDeckAsksForTheFirstPage()
    {
        var paging = new ReelPaging();

        Assert.True(paging.Wants(0, 0, 0));
    }

    [Fact]
    public void TheNextPageIsAskedForWhenTwoReelsRemain()
    {
        var paging = new ReelPaging();

        Assert.False(paging.Wants(1, 5, 0));
        Assert.True(paging.Wants(2, 5, 0));
    }

    [Fact]
    public void OnlyOnePageIsInFlight()
    {
        var paging = new ReelPaging();
        paging.Begin();

        Assert.False(paging.Wants(4, 5, 0));
    }

    [Fact]
    public void APageKeepsTheSessionAndCursor()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Accept("session1", "5", 5);

        Assert.Equal("session1", paging.Session);
        Assert.Equal("5", paging.Cursor);
        Assert.False(paging.Loading);
        Assert.False(paging.Exhausted);
    }

    [Fact]
    public void ARunDrySessionGivesWayToOneFreshSession()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Accept("session1", "5", 5);
        paging.Begin();
        paging.Accept("session1", null, 0);

        Assert.Null(paging.Session);
        Assert.Null(paging.Cursor);
        Assert.False(paging.Exhausted);
        Assert.True(paging.Wants(4, 5, 0));
    }

    [Fact]
    public void AnEmptyFreshSessionStopsTheFeed()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Accept("session1", null, 0);
        paging.Begin();
        paging.Accept("session2", null, 0);

        Assert.True(paging.Exhausted);
        Assert.False(paging.Wants(0, 0, 0));
    }

    [Fact]
    public void ReelsFromTheFreshSessionKeepTheFeedGoing()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Accept("session1", null, 0);
        paging.Begin();
        paging.Accept("session2", "5", 5);
        paging.Begin();
        paging.Accept("session2", null, 0);

        Assert.False(paging.Exhausted);
        Assert.Null(paging.Session);
    }

    [Fact]
    public void AFailureWaitsBeforeAskingAgain()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Fail(1000);

        Assert.False(paging.Loading);
        Assert.False(paging.Wants(0, 0, 1000 + ReelPaging.RetryDelayMilliseconds - 1));
        Assert.True(paging.Wants(0, 0, 1000 + ReelPaging.RetryDelayMilliseconds));
    }

    [Fact]
    public void AResetStartsOver()
    {
        var paging = new ReelPaging();
        paging.Begin();
        paging.Accept("session1", null, 0);
        paging.Begin();
        paging.Accept("session2", null, 0);
        paging.Reset();

        Assert.False(paging.Exhausted);
        Assert.Null(paging.Session);
        Assert.True(paging.Wants(0, 0, 0));
    }
}

public sealed class ReelPauseRulesTests
{
    private static readonly ReelPlaybackInputs Watching = new(true, true, ReelCover.None, false, false);

    [Fact]
    public void AVisibleReelPlays()
    {
        Assert.True(ReelPauseRules.ShouldPlay(Watching));
    }

    [Fact]
    public void CommentsKeepTheReelPlaying()
    {
        Assert.True(ReelPauseRules.ShouldPlay(Watching with { Cover = ReelCover.Comments }));
    }

    [Fact]
    public void AnyOtherSheetPausesIt()
    {
        Assert.False(ReelPauseRules.ShouldPlay(Watching with { Cover = ReelCover.Sheet }));
    }

    [Fact]
    public void AHiddenOrMinimizedPhonePausesIt()
    {
        Assert.False(ReelPauseRules.ShouldPlay(Watching with { PhoneVisible = false }));
    }

    [Fact]
    public void AnotherAppOrTabPausesIt()
    {
        Assert.False(ReelPauseRules.ShouldPlay(Watching with { SurfaceShown = false }));
    }

    [Fact]
    public void MogCastPlayingPausesIt()
    {
        Assert.False(ReelPauseRules.ShouldPlay(Watching with { OtherVideoPlaying = true }));
    }

    [Fact]
    public void AHoldPausesIt()
    {
        Assert.False(ReelPauseRules.ShouldPlay(Watching with { Held = true }));
    }

    [Fact]
    public void ASurfaceCountsAsShownForAMomentAfterItsLastFrame()
    {
        Assert.True(ReelPauseRules.SurfaceShown(1000, 1000 - ReelPauseRules.HiddenAfterMilliseconds + 1));
        Assert.False(ReelPauseRules.SurfaceShown(1000, 1000 - ReelPauseRules.HiddenAfterMilliseconds));
    }

    [Fact]
    public void ThePlayerIsReleasedAfterAMinuteAway()
    {
        Assert.False(ReelPauseRules.ShouldRelease(ReelPauseRules.ReleaseAfterMilliseconds - 1, 0));
        Assert.True(ReelPauseRules.ShouldRelease(ReelPauseRules.ReleaseAfterMilliseconds, 0));
    }
}

public sealed class FeedReelFocusTests
{
    [Fact]
    public void TheMostVisibleMostlyShownCardWins()
    {
        var focus = new FeedReelFocus();
        focus.Offer("a", 0.7f, 3);
        focus.Offer("b", 0.95f, 3);
        focus.Offer("c", 0.8f, 3);

        Assert.Equal("b", focus.Winner(3));
    }

    [Fact]
    public void AHalfShownCardDoesNotPlay()
    {
        var focus = new FeedReelFocus();
        focus.Offer("a", 0.5f, 3);

        Assert.Null(focus.Winner(3));
    }

    [Fact]
    public void ALastFrameWinnerDoesNotCarryOver()
    {
        var focus = new FeedReelFocus();
        focus.Offer("a", 0.9f, 3);

        Assert.Null(focus.Winner(4));
    }

    [Fact]
    public void VisibleShareIsTheClippedFraction()
    {
        Assert.Equal(1f, FeedReelFocus.VisibleShare(100f, 200f, 0f, 500f));
        Assert.Equal(0.25f, FeedReelFocus.VisibleShare(100f, 200f, 175f, 500f), 3);
        Assert.Equal(0f, FeedReelFocus.VisibleShare(100f, 200f, 300f, 500f));
    }
}

public sealed class ReelWireTests
{
    private const string ServerPage =
        "{\"items\":[{\"id\":\"p1\",\"authorId\":\"u1\",\"authorName\":\"A\",\"authorWorld\":\"W\","
        + "\"authorDisplayName\":\"A\",\"authorHandle\":\"a\",\"text\":\"hi\",\"createdAtUnix\":1,"
        + "\"reactionCounts\":[0,0],\"totalReactions\":0,\"myReaction\":-1,\"kind\":1,"
        + "\"mediaUrl\":\"https://cdn/p.jpg\",\"mediaWidth\":720,\"mediaHeight\":1280,\"authorAvatarUrl\":null,"
        + "\"commentCount\":0,\"isFollowing\":false,\"scanStatus\":\"clean\","
        + "\"video\":{\"url\":\"https://cdn/v.mp4\",\"posterUrl\":\"https://cdn/p.jpg\",\"durationMs\":12500,"
        + "\"width\":720,\"height\":1280}}],\"session\":\"s1\",\"nextCursor\":\"5\",\"notes\":null}";

    [Fact]
    public void AReelPageCarriesItsVideoAndSession()
    {
        var page = JsonSerializer.Deserialize(ServerPage, AethernetJsonContext.Default.ReelFeedPage);

        Assert.NotNull(page);
        Assert.Equal("s1", page.Session);
        Assert.Equal("5", page.NextCursor);
        var video = Assert.Single(page.Items).Video;
        Assert.NotNull(video);
        Assert.Equal("https://cdn/v.mp4", video.Url);
        Assert.Equal(12500, video.DurationMs);
        Assert.Equal(1280, video.Height);
    }

    [Fact]
    public void AWatchReportUsesTheServerFieldNames()
    {
        var request = new ReelWatchRequest(
            [new ReelWatchItem("p1", 4200, 12500, 1, true, false, ReelWatchLedger.ReelsSource)], "s1");

        var json = JsonSerializer.Serialize(request, AethernetJsonContext.Default.ReelWatchRequest);

        Assert.Contains("\"postId\":\"p1\"", json);
        Assert.Contains("\"watchedMs\":4200", json);
        Assert.Contains("\"durationMs\":12500", json);
        Assert.Contains("\"loops\":1", json);
        Assert.Contains("\"completed\":true", json);
        Assert.Contains("\"quickSkip\":false", json);
        Assert.Contains("\"source\":10", json);
        Assert.Contains("\"session\":\"s1\"", json);
    }

    [Fact]
    public void ViewsCarryTheReelsGroup()
    {
        var views = JsonSerializer.Deserialize(
            "{\"total\":9,\"followers\":1,\"nonFollowers\":2,\"home\":3,\"explore\":0,\"profile\":0,"
            + "\"hashtagsAndSearch\":0,\"stories\":0,\"other\":0,\"earlier\":0,\"reels\":6}",
            AethernetJsonContext.Default.PostViewsDto);

        Assert.NotNull(views);
        Assert.Equal(6, views.Reels);
    }
}
