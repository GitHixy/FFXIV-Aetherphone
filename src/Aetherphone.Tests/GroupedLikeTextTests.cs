using System.Text.Json;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class GroupedLikeTextTests
{
    [Fact]
    public void ASingleLikeReadsAsBefore()
    {
        Assert.Equal("liked your chirp", SocialActivity.Body(Like(SocialActivity.ChirperApp, 1, null)));
    }

    [Fact]
    public void TwoLikersNameTheSecond()
    {
        Assert.Equal("and Bea liked your chirp", SocialActivity.Body(Like(SocialActivity.ChirperApp, 2, "Bea")));
        Assert.Equal("and Bea liked your photo", SocialActivity.Body(Like(SocialActivity.AethergramApp, 2, "Bea")));
    }

    [Fact]
    public void ACrowdIsCounted()
    {
        Assert.Equal("and 22 others liked your photo", SocialActivity.Body(Like(SocialActivity.AethergramApp, 23, "Bea")));
        Assert.Equal("and 1 other liked your chirp", SocialActivity.Body(Like(SocialActivity.ChirperApp, 2, null)));
    }

    [Fact]
    public void AGroupedCommentLikeKeepsItsQuote()
    {
        var item = Like(SocialActivity.ChirperApp, 3, "Bea") with { Type = SocialActivity.TypeCommentLike, Preview = "nice" };
        Assert.Equal("and 2 others liked your comment: “nice”", SocialActivity.Body(item));
    }

    [Fact]
    public void TheServerShapeReadsBack()
    {
        const string json = """{"id":"n1","type":0,"app":"aethergram","postId":"p1","actorId":"a1","actorName":"","actorDisplayName":"Ann","actorHandle":"ann","actorAvatarUrl":null,"preview":null,"createdAtUnix":1,"actorCount":4,"otherActorDisplayName":"Bea"}""";
        var item = JsonSerializer.Deserialize(json, AethernetJsonContext.Default.NotificationDto)!;

        Assert.Equal(4, item.ActorCount);
        Assert.Equal("Bea", item.OtherActorDisplayName);
        Assert.Equal("and 3 others liked your photo", SocialActivity.Body(item));
    }

    private static NotificationDto Like(string app, int count, string? other)
    {
        return new NotificationDto("n", SocialActivity.TypeLike, app, "p", "a", string.Empty, "Ann", "ann", null, null, 1,
            ActorCount: count, OtherActorDisplayName: other);
    }
}
