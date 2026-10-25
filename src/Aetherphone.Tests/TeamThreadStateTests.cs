using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;
using Aetherphone.Core.Team;
using Xunit;

namespace Aetherphone.Tests;

public sealed class TeamThreadStateTests
{
    private const int PageSize = 3;

    private static TeamMessageDto Team(long id, bool read = false) =>
        new(id, true, TeamMessageKinds.Text, "team " + id, string.Empty, "Xeldar", DateTime.UtcNow, read);

    private static TeamMessageDto Player(long id) =>
        new(id, false, TeamMessageKinds.Text, "player " + id, string.Empty, string.Empty, DateTime.UtcNow, false);

    private static TeamMessagePage Page(int unread, bool canReply, params TeamMessageDto[] newestFirst) =>
        new(newestFirst, unread, canReply);

    private static long[] Ids(TeamThreadState state)
    {
        var messages = state.Messages;
        var ids = new long[messages.Length];
        for (var index = 0; index < messages.Length; index++)
        {
            ids[index] = messages[index].Id;
        }

        return ids;
    }

    [Fact]
    public void TheFirstPageLandsOldestFirstWithTheServerCounts()
    {
        var state = new TeamThreadState();

        state.ApplyLatest(Page(2, true, Team(30), Player(20), Team(10)), PageSize);

        Assert.Equal(new long[] { 10, 20, 30 }, Ids(state));
        Assert.Equal(2, state.Unread);
        Assert.True(state.CanReply);
        Assert.True(state.HasMoreOlder);
        Assert.True(state.Loaded);
        Assert.True(state.HasThread);
    }

    [Fact]
    public void AShortFirstPageHasNothingOlder()
    {
        var state = new TeamThreadState();

        state.ApplyLatest(Page(0, true, Team(5)), PageSize);

        Assert.False(state.HasMoreOlder);
    }

    [Fact]
    public void OlderPagesMergeBelowWithoutDuplicates()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, true, Team(30), Team(20), Team(10)), PageSize);

        state.ApplyOlder(Page(0, true, Team(10), Team(5)), PageSize);

        Assert.Equal(new long[] { 5, 10, 20, 30 }, Ids(state));
        Assert.False(state.HasMoreOlder);
    }

    [Fact]
    public void ARefreshKeepsLoadedOlderMessagesAndTakesTheFresherCopy()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(1, true, Team(30), Team(20), Team(10)), PageSize);
        state.ApplyOlder(Page(1, true, Team(5)), PageSize);

        state.ApplyLatest(Page(0, true, Team(40, true), Team(30, true), Team(20, true)), PageSize);

        Assert.Equal(new long[] { 5, 10, 20, 30, 40 }, Ids(state));
        Assert.True(state.Messages[3].Read);
        Assert.Equal(0, state.Unread);
    }

    [Fact]
    public void ARefreshThatSkipsPastEverythingKnownStartsOver()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, true, Team(3), Team(2), Team(1)), PageSize);

        state.ApplyLatest(Page(3, true, Team(90), Team(80), Team(70)), PageSize);

        Assert.Equal(new long[] { 70, 80, 90 }, Ids(state));
        Assert.True(state.HasMoreOlder);
    }

    [Fact]
    public void ALiveMessageArrivesOnceAndCountsAsUnread()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, true, Team(10)), PageSize);

        Assert.True(state.ApplyIncoming(Team(11)));
        Assert.False(state.ApplyIncoming(Team(11)));

        Assert.Equal(new long[] { 10, 11 }, Ids(state));
        Assert.Equal(1, state.Unread);
    }

    [Fact]
    public void YourOwnReplyIsNeverUnread()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, true, Team(10)), PageSize);

        Assert.True(state.ApplyIncoming(Player(11)));

        Assert.Equal(0, state.Unread);
        Assert.Equal(11, state.NewestId);
    }

    [Fact]
    public void ATeamMessageOpensReplies()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, false), PageSize);
        Assert.False(state.CanReply);
        Assert.False(state.HasThread);

        state.ApplyIncoming(Team(1));

        Assert.True(state.CanReply);
        Assert.True(state.HasThread);
    }

    [Fact]
    public void MarkingReadClearsTheCountAndTheFlagsOnce()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(2, true, Team(20), Team(10)), PageSize);

        Assert.True(state.MarkAllRead());
        Assert.False(state.MarkAllRead());

        Assert.Equal(0, state.Unread);
        Assert.All(state.Messages, message => Assert.True(message.Read));
    }

    [Fact]
    public void AClosedThreadStopsReplies()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(0, true, Team(1)), PageSize);

        state.CloseReplies();

        Assert.False(state.CanReply);
    }

    [Fact]
    public void ClearingForASignOutForgetsEverything()
    {
        var state = new TeamThreadState();
        state.ApplyLatest(Page(1, true, Team(30), Team(20), Team(10)), PageSize);
        var before = state.Version;

        state.Clear();

        Assert.Empty(state.Messages);
        Assert.Equal(0, state.Unread);
        Assert.False(state.CanReply);
        Assert.False(state.HasMoreOlder);
        Assert.False(state.Loaded);
        Assert.False(state.HasThread);
        Assert.NotEqual(before, state.Version);
    }

    [Fact]
    public void ANewAccountResetsButTheSameOneDoesNot()
    {
        Assert.False(TeamInboxStore.ShouldReset("user-1", "user-1"));
        Assert.True(TeamInboxStore.ShouldReset("user-1", "user-2"));
        Assert.True(TeamInboxStore.ShouldReset("user-1", null));
        Assert.True(TeamInboxStore.ShouldReset(null, "user-1"));
    }

    [Fact]
    public void AClosedThreadAnswerIsRecognised()
    {
        var byCode = new AepFailure(AepFailureKind.Server, 403, TeamInboxStore.ThreadClosedCode, null, null, null);
        var byStatus = AepFailure.FromStatus(403, null);
        var other = AepFailure.FromStatus(500, null);

        Assert.True(TeamInboxStore.IsThreadClosed(byCode));
        Assert.True(TeamInboxStore.IsThreadClosed(byStatus));
        Assert.False(TeamInboxStore.IsThreadClosed(other));
    }

    [Fact]
    public void ServerTimesWithoutAZoneReadAsUtc()
    {
        var unspecified = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        var fromUnspecified = new TeamMessageDto(1, CreatedAt: unspecified).CreatedAtUnix;
        var fromUtc = new TeamMessageDto(2, CreatedAt: utc).CreatedAtUnix;

        Assert.Equal(new DateTimeOffset(utc).ToUnixTimeSeconds(), fromUtc);
        Assert.Equal(fromUtc, fromUnspecified);
        Assert.Equal(0, new TeamMessageDto(3).CreatedAtUnix);
    }
}
