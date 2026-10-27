using Aetherphone.Core.Songs;
using Xunit;

namespace Aetherphone.Tests;

public sealed class SongResolverErrorTests
{
    [Theory]
    [InlineData("ERROR: [youtube] 1Wsar3HGnrE: Video unavailable")]
    [InlineData("ERROR: [youtube] syNK6A6Xxtl: This video is unavailable")]
    [InlineData("ERROR: [youtube] abcdefghijk: Private video. Sign in if you've been granted access to this video")]
    [InlineData("ERROR: [youtube] abcdefghijk: Video unavailable. This video is no longer available because the YouTube account associated with this video has been terminated.")]
    [InlineData("ERROR: [youtube] abcdefghijk: Video unavailable. The uploader has not made this video available in your country")]
    public void ReadsDeadVideosAsUnavailable(string errorText)
    {
        Assert.True(SongLinkResolver.ReportsUnavailable(errorText));
    }

    [Theory]
    [InlineData("ERROR: [youtube] abcdefghijk: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies for the authentication.")]
    [InlineData("ERROR: [youtube] abcdefghijk: Sign in to confirm your age. This video may be inappropriate for some users.")]
    [InlineData("ERROR: [youtube] abcdefghijk: Requested format is not available. Use --list-formats for a list of available formats")]
    [InlineData("ERROR: Unable to download API page: HTTP Error 503: Service Unavailable")]
    [InlineData("")]
    public void KeepsRecoverableErrorsRetryable(string errorText)
    {
        Assert.False(SongLinkResolver.ReportsUnavailable(errorText));
    }
}
