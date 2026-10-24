using System.Numerics;
using Aetherphone.Apps.Games.Framework;
using Aetherphone.Core;
using Aetherphone.Core.Games;
using Aetherphone.Core.Localization;
using Xunit;

namespace Aetherphone.Tests;

public sealed class GameGuideTests
{
    private static readonly LocString Text = new("test.guide", "Guide");

    [Fact]
    public void AGameWithoutAGuideDeclaresNone()
    {
        var spec = new GameSpec("plain", Text, GameGenre.Arcade);
        Assert.Null(spec.Guide);
    }

    [Fact]
    public void AGuideIsSeenOnceMarkedAndOnlyForItsGame()
    {
        var configuration = new FakeStatsConfiguration();
        var stats = new GameStatsStore(configuration);
        Assert.False(stats.GuideSeen("claim"));
        stats.MarkGuideSeen("claim");
        Assert.True(stats.GuideSeen("claim"));
        Assert.False(stats.GuideSeen("fuse"));
    }

    [Fact]
    public void MarkingASeenGuideAgainSavesNothing()
    {
        var configuration = new FakeStatsConfiguration();
        var stats = new GameStatsStore(configuration);
        stats.MarkGuideSeen("claim");
        var saves = configuration.Saves;
        stats.MarkGuideSeen("claim");
        stats.MarkGuideSeen(string.Empty);
        Assert.Equal(saves, configuration.Saves);
        Assert.Single(configuration.GameGuidesSeen);
    }

    [Theory]
    [InlineData(0f, 2f, 0f)]
    [InlineData(1f, 2f, 0.5f)]
    [InlineData(3f, 2f, 0.5f)]
    [InlineData(1f, 0f, 0f)]
    public void LoopWrapsSecondsIntoAPhase(float seconds, float period, float expected)
    {
        Assert.Equal(expected, GuideKit.Loop(seconds, period), 3);
    }

    [Theory]
    [InlineData(0.1f, 0.2f, 0.4f, 0f)]
    [InlineData(0.3f, 0.2f, 0.4f, 0.5f)]
    [InlineData(0.9f, 0.2f, 0.4f, 1f)]
    public void WindowRampsBetweenItsEdges(float phase, float start, float end, float expected)
    {
        Assert.Equal(expected, GuideKit.Window(phase, start, end), 3);
    }

    [Theory]
    [InlineData(200f, 100f, 1f, 100f, 100f)]
    [InlineData(100f, 200f, 1f, 100f, 100f)]
    [InlineData(300f, 100f, 2f, 200f, 100f)]
    [InlineData(100f, 300f, 0.5f, 100f, 200f)]
    public void FitCentersTheLargestRectOfTheAspect(float width, float height, float aspect, float fitWidth,
        float fitHeight)
    {
        var art = new Rect(new Vector2(10f, 20f), new Vector2(10f + width, 20f + height));
        var fitted = GuideKit.Fit(art, aspect);
        Assert.Equal(fitWidth, fitted.Width, 3);
        Assert.Equal(fitHeight, fitted.Height, 3);
        Assert.Equal(art.Center.X, fitted.Center.X, 3);
        Assert.Equal(art.Center.Y, fitted.Center.Y, 3);
    }
}
