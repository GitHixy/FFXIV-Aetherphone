using Aetherphone.Core.Message;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ReadWatermarkTests
{
    private static readonly ReadThrough ReadAtEight = new(LastMessageAt: 1_000, Unread: 8);

    [Fact]
    public void HoldsZeroWhileTheServerRepeatsTheCountItHadWhenRead()
    {
        Assert.Equal(ReadWatermarkVerdict.Hold, ReadWatermark.Judge(ReadAtEight, 1_000, 8));
    }

    [Fact]
    public void HoldsZeroWhileTheServerStillReportsSomeOfThatCount()
    {
        Assert.Equal(ReadWatermarkVerdict.Hold, ReadWatermark.Judge(ReadAtEight, 1_000, 3));
    }

    [Fact]
    public void SettlesOnceTheServerAgrees()
    {
        Assert.Equal(ReadWatermarkVerdict.Settled, ReadWatermark.Judge(ReadAtEight, 1_000, 0));
    }

    [Fact]
    public void ANewerMessageSupersedesTheMark()
    {
        Assert.Equal(ReadWatermarkVerdict.Superseded, ReadWatermark.Judge(ReadAtEight, 1_001, 1));
    }

    [Fact]
    public void AnExtraMessageInTheSameSecondSupersedesTheMark()
    {
        Assert.Equal(ReadWatermarkVerdict.Superseded, ReadWatermark.Judge(ReadAtEight, 1_000, 9));
    }
}
