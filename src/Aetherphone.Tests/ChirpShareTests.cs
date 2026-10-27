using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ChirpShareTests
{
    [Fact]
    public void ComposeRoundTripsThroughTryParse()
    {
        var token = ChirpShare.Compose("9c1d2e3f4a5b46c7d8e9f0a1b2c3d4e5");

        Assert.Equal("[aep.chirp.v1:9c1d2e3f4a5b46c7d8e9f0a1b2c3d4e5]", token);
        Assert.True(ChirpShare.TryParse(token, out var postId));
        Assert.Equal("9c1d2e3f4a5b46c7d8e9f0a1b2c3d4e5", postId);
    }

    [Fact]
    public void WhitespaceAroundTheTokenStillParses()
    {
        var token = "  " + ChirpShare.Compose("9c1d2e3f4a5b46c7d8e9f0a1b2c3d4e5") + "\n";

        Assert.True(ChirpShare.TryParse(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[aep.chirp.v1:]")]
    [InlineData("[aep.chirp.v1:abc")]
    [InlineData("aep.chirp.v1:abc]")]
    [InlineData("[aep.muster.v1:9c1d2e3f4a5b46c7]")]
    [InlineData("look at [aep.chirp.v1:9c1d2e3f4a5b46c7] this")]
    [InlineData("[aep.chirp.v1:../../etc/passwd]")]
    [InlineData("[aep.chirp.v1:abc def]")]
    public void InvalidBodiesDoNotParse(string? body)
    {
        Assert.False(ChirpShare.TryParse(body, out _));
    }

    [Fact]
    public void MixedTextContainingATokenStaysPlain()
    {
        var body = "read this " + ChirpShare.Compose("9c1d2e3f4a5b46c7d8e9f0a1b2c3d4e5");

        Assert.False(ChirpShare.IsToken(body));
    }

    [Fact]
    public void AnOverlongIdentifierIsRejected()
    {
        var token = ChirpShare.Compose(new string('a', 65));

        Assert.False(ChirpShare.TryParse(token, out _));
    }
}
