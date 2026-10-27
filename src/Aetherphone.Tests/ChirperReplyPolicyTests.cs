using System.Text.Json;
using Aetherphone.Apps.Chirper;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ChirperReplyPolicyTests
{
    private const string PostFields =
        "\"id\":\"c1\",\"authorId\":\"a\",\"authorName\":\"\",\"authorWorld\":\"\",\"authorDisplayName\":\"A\","
        + "\"authorHandle\":\"a\",\"text\":\"Hello\",\"createdAtUnix\":100,\"reactionCounts\":[],"
        + "\"totalReactions\":0,\"myReaction\":-1,\"kind\":0,\"mediaUrl\":null,\"mediaWidth\":0,\"mediaHeight\":0,"
        + "\"authorAvatarUrl\":null,\"commentCount\":0,\"isFollowing\":false";

    [Fact]
    public void ANewChirpSendsWhoCanReply()
    {
        var json = JsonSerializer.Serialize(new CreatePostRequest("Hello", ReplyPolicy: ChirperReplyPolicy.Mentioned),
            AethernetJsonContext.Default.CreatePostRequest);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(ChirperReplyPolicy.Mentioned, document.RootElement.GetProperty("replyPolicy").GetInt32());
    }

    [Fact]
    public void ChangingWhoCanReplySendsOnlyThePolicy()
    {
        var json = JsonSerializer.Serialize(new SetReplyPolicyRequest(ChirperReplyPolicy.Following),
            AethernetJsonContext.Default.SetReplyPolicyRequest);

        Assert.Equal("{\"replyPolicy\":1}", json);
    }

    [Fact]
    public void AChirpFromAServerWithoutReplySettingsStaysOpenToReplies()
    {
        var post = JsonSerializer.Deserialize("{" + PostFields + "}", AethernetJsonContext.Default.PostDto)!;

        Assert.Equal(ChirperReplyPolicy.Everyone, post.ReplyPolicy);
        Assert.True(post.CanReply);
    }

    [Fact]
    public void ALimitedChirpCarriesTheViewersAnswer()
    {
        var post = JsonSerializer.Deserialize("{" + PostFields + ",\"replyPolicy\":2,\"canReply\":false}",
            AethernetJsonContext.Default.PostDto)!;

        Assert.Equal(ChirperReplyPolicy.Mentioned, post.ReplyPolicy);
        Assert.False(post.CanReply);
    }
}
