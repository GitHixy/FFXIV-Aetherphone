using System.Numerics;
using Aetherphone.Apps.Casino.Tables;
using Aetherphone.Core;
using Xunit;

namespace Aetherphone.Tests;

public sealed class LedgerEntryLayoutTests
{
    private const float PageWidth = 328f;
    private const float Epsilon = 0.01f;

    public static TheoryData<float> Scales => new() { 0.75f, 1f, 1.25f, 1.5f };

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheActionSitsUnderTheStateLineAtTheRightEdge(float scale)
    {
        var width = PageWidth * scale;
        var title = 17f * scale;
        var parties = 15f * scale;
        var state = 15f * scale;
        var card = new Rect(Vector2.Zero,
            new Vector2(width, LedgerEntryLayout.Height(title, parties, state, true, scale)));
        var button = LedgerEntryLayout.Button(card, 110f * scale, scale);
        var stateBottom = LedgerEntryLayout.StateTop(card.Min.Y, title, parties, scale) + state;

        Assert.True(button.Min.Y >= stateBottom - Epsilon);
        Assert.True(button.Max.Y <= card.Max.Y - LedgerEntryLayout.Pad * scale + Epsilon);
        Assert.Equal(card.Max.X - LedgerEntryLayout.Pad * scale, button.Max.X, 3);
        Assert.True(button.Height >= LedgerEntryLayout.Touch * scale - Epsilon);
        Assert.Equal(110f * scale, button.Width, 3);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void AnOverlongActionIsClampedInsideTheCard(float scale)
    {
        var width = PageWidth * scale;
        var card = new Rect(Vector2.Zero,
            new Vector2(width, LedgerEntryLayout.Height(17f * scale, 15f * scale, 15f * scale, true, scale)));
        var button = LedgerEntryLayout.Button(card, width * 2f, scale);

        Assert.True(button.Min.X >= card.Min.X + LedgerEntryLayout.Pad * scale - Epsilon);
        Assert.True(button.Max.X <= card.Max.X - LedgerEntryLayout.Pad * scale + Epsilon);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void AnEntryWithoutAnActionIsShorterByTheButtonRow(float scale)
    {
        var withAction = LedgerEntryLayout.Height(17f * scale, 15f * scale, 15f * scale, true, scale);
        var withoutAction = LedgerEntryLayout.Height(17f * scale, 15f * scale, 15f * scale, false, scale);

        Assert.Equal((LedgerEntryLayout.ButtonGap + LedgerEntryLayout.Touch) * scale, withAction - withoutAction, 3);
    }
}
