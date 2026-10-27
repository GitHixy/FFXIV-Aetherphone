using System.Text.Json;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class SocialMuteLedgerTests
{
    private const string PostFields =
        "\"id\":\"q1\",\"authorId\":\"a\",\"authorName\":\"\",\"authorWorld\":\"\",\"authorDisplayName\":\"A\","
        + "\"authorHandle\":\"a\",\"text\":\"Hello\",\"createdAtUnix\":100,\"reactionCounts\":[],"
        + "\"totalReactions\":0,\"myReaction\":-1,\"kind\":0,\"mediaUrl\":null,\"mediaWidth\":0,\"mediaHeight\":0,"
        + "\"authorAvatarUrl\":null,\"commentCount\":0,\"isFollowing\":false";

    [Fact]
    public void AMuteRecordedByOneAppIsSeenAndAnnouncedToEveryListener()
    {
        var ledger = new SocialMuteLedger();
        var heard = new List<MuteChange>();
        ledger.Changed += heard.Add;
        ledger.Changed += heard.Add;

        ledger.Record("u1", true);

        Assert.True(ledger.TryGet("u1", out var muted));
        Assert.True(muted);
        Assert.False(ledger.TryGet("u2", out _));
        Assert.Equal(2, heard.Count);
        Assert.All(heard, change => Assert.Equal(new MuteChange("u1", true, false), change));
    }

    [Fact]
    public void ARestoredMuteIsMarkedSoListenersCanReloadTheirFeeds()
    {
        var ledger = new SocialMuteLedger();
        MuteChange? last = null;
        ledger.Changed += change => last = change;

        ledger.Record("u1", true);
        ledger.Restore("u1", false);

        Assert.True(ledger.TryGet("u1", out var muted));
        Assert.False(muted);
        Assert.Equal(new MuteChange("u1", false, true), last);
    }

    [Fact]
    public void ResettingForgetsEveryMuteMadeThisSession()
    {
        var ledger = new SocialMuteLedger();
        ledger.Record("u1", true);

        ledger.Reset();

        Assert.False(ledger.TryGet("u1", out _));
    }

    [Fact]
    public void AQuotedChirpReadsTheViewersMuteFlagAndDefaultsToUnmuted()
    {
        var flagged = JsonSerializer.Deserialize("{" + PostFields + ",\"authorMuted\":true}",
            AethernetJsonContext.Default.PostDto)!;
        var older = JsonSerializer.Deserialize("{" + PostFields + "}", AethernetJsonContext.Default.PostDto)!;

        Assert.True(flagged.AuthorMuted);
        Assert.False(older.AuthorMuted);
    }
}
