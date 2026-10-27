using System.Text.Json;
using Aetherphone.Apps.Chirper;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ChirperEditTests
{
    private const long Posted = 1_800_000_000L;
    private const string AuthorId = "author";

    private const string EditedPost =
        "{\"id\":\"c1\",\"authorId\":\"author\",\"authorName\":\"\",\"authorWorld\":\"\",\"authorDisplayName\":\"A\","
        + "\"authorHandle\":\"a\",\"text\":\"better words\",\"createdAtUnix\":100,\"reactionCounts\":[],"
        + "\"totalReactions\":0,\"myReaction\":-1,\"kind\":0,\"mediaUrl\":null,\"mediaWidth\":0,\"mediaHeight\":0,"
        + "\"authorAvatarUrl\":null,\"commentCount\":0,\"isFollowing\":false,\"editedAtUnix\":160,\"editCount\":2}";

    private const string History =
        "[{\"id\":\"e2\",\"text\":\"second\",\"postedAtUnix\":130,\"editedAtUnix\":160},"
        + "{\"id\":\"e1\",\"text\":\"first\",\"postedAtUnix\":100,\"editedAtUnix\":130}]";

    private static PostDto Chirp(int editCount = 0, string? repostOfId = null, PostPollDto? poll = null) =>
        new("c1", AuthorId, string.Empty, string.Empty, "Author", "author", "first words", Posted, Array.Empty<int>(), 0,
            -1, 0, null, 0, 0, null, 0, false, RepostOfId: repostOfId, Poll: poll, EditCount: editCount);

    [Fact]
    public void TheAuthorCanEditForAnHourAndUpToFiveTimes()
    {
        Assert.True(ChirperEdits.CanEdit(Chirp(), AuthorId, Posted + 60));
        Assert.True(ChirperEdits.CanEdit(Chirp(editCount: 4), AuthorId, Posted + ChirperEdits.WindowSeconds - 1));
        Assert.False(ChirperEdits.CanEdit(Chirp(), AuthorId, Posted + ChirperEdits.WindowSeconds));
        Assert.False(ChirperEdits.CanEdit(Chirp(editCount: ChirperEdits.MaxEdits), AuthorId, Posted + 60));
    }

    [Fact]
    public void NobodyElseAndNoRepostOrPollChirpOffersAnEdit()
    {
        Assert.False(ChirperEdits.CanEdit(Chirp(), "someone else", Posted + 60));
        Assert.False(ChirperEdits.CanEdit(Chirp(), null, Posted + 60));
        Assert.False(ChirperEdits.CanEdit(Chirp(repostOfId: "c0"), AuthorId, Posted + 60));
        Assert.False(ChirperEdits.CanEdit(Chirp(poll: new PostPollDto(new[] { "Ifrit", "Shiva" })), AuthorId, Posted + 60));
    }

    [Fact]
    public void SavingTheSameWordsWithSpacesAroundIsNoChange()
    {
        Assert.True(ChirperEdits.Unchanged("  first words \n", "first words"));
        Assert.False(ChirperEdits.Unchanged("first word", "first words"));
    }

    [Fact]
    public void AnEditedChirpCarriesItsCountAndTheHistoryParsesNewestFirst()
    {
        var post = JsonSerializer.Deserialize(EditedPost, AethernetJsonContext.Default.PostDto)!;
        var versions = JsonSerializer.Deserialize(History, AethernetJsonContext.Default.PostEditDtoArray)!;

        Assert.Equal(2, post.EditCount);
        Assert.True(ChirperEdits.IsEdited(post));
        Assert.Equal(3, ChirperEdits.EditsLeft(post));
        Assert.Equal(new[] { "second", "first" }, new[] { versions[0].Text, versions[1].Text });
        Assert.Equal(100L, versions[1].PostedAtUnix);
    }

    [Fact]
    public void AnEditLandsOnACopyWithoutTouchingItsOwnCounters()
    {
        var copy = Chirp() with { TotalReactions = 7, Saved = true };
        var edited = Chirp(editCount: 1) with { Text = "better words", EditedAtUnix = Posted + 30, Lang = "en" };

        var updated = ChirperEdits.WithEdit(copy, edited);

        Assert.Equal("better words", updated.Text);
        Assert.Equal(1, updated.EditCount);
        Assert.Equal(Posted + 30, updated.EditedAtUnix);
        Assert.Equal(7, updated.TotalReactions);
        Assert.True(updated.Saved);
    }
}
