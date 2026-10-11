using Aetherphone.Apps.Casino.Cabinets;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ScratchStatusLayoutTests
{
    public static TheoryData<float> Scales => new() { 0.75f, 1f, 1.25f, 1.5f };

    [Theory]
    [MemberData(nameof(Scales))]
    public void AWrappedHintAndTheRunButtonBothFitTheReservedArea(float scale)
    {
        var line = 22f * scale;
        var button = 44f * scale;
        var gap = 5f * scale;
        var twoLines = line * 2f;

        var reserve = ScratchCabinet.StatusReserve(twoLines, true, line, button, gap);

        Assert.True(reserve >= twoLines + gap + button - 0.01f);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ATallNoticeIsNeverClippedByTheRestingReserve(float scale)
    {
        var notice = 74f * scale;

        var reserve = ScratchCabinet.StatusReserve(notice, false, 22f * scale, 44f * scale, 5f * scale);

        Assert.True(reserve >= notice - 0.01f);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheReserveNeverDropsBelowOneLineAndAButton(float scale)
    {
        var line = 22f * scale;
        var button = 44f * scale;
        var gap = 5f * scale;

        Assert.Equal(line + gap + button, ScratchCabinet.StatusReserve(0f, false, line, button, gap), 3);
        Assert.Equal(line + gap + button, ScratchCabinet.StatusReserve(button, false, line, button, gap), 3);
    }

    [Fact]
    public void TheRunButtonOnlyShowsBesideAnIdleOrLosingTicket()
    {
        Assert.True(ScratchCabinet.ShowsRun(ScratchStatusLine.None, true));
        Assert.True(ScratchCabinet.ShowsRun(ScratchStatusLine.NoWin, true));
        Assert.False(ScratchCabinet.ShowsRun(ScratchStatusLine.Hint, true));
        Assert.False(ScratchCabinet.ShowsRun(ScratchStatusLine.Reason, true));
        Assert.False(ScratchCabinet.ShowsRun(ScratchStatusLine.None, false));
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheKioskLightsStayAboveTheStatusArea(float scale)
    {
        var kiosk = 12f * scale;
        var gap = 10f * scale;
        var bottom = 520f * scale;
        var safe = new Aetherphone.Core.Rect(new System.Numerics.Vector2(0f, 60f * scale),
            new System.Numerics.Vector2(330f * scale, 700f * scale));
        var ticket = ScratchCabinet.TicketRect(safe, bottom, kiosk, 360f * scale, 0.25f);
        Assert.True(ticket.Width > 0f);
        Assert.True(ticket.Min.Y - kiosk >= safe.Min.Y - 0.001f);
        Assert.True(ticket.Max.Y + kiosk <= bottom + 0.001f);
        Assert.True(ticket.Min.X - kiosk >= safe.Min.X - 0.001f);
        Assert.True(ticket.Max.X + kiosk <= safe.Max.X + 0.001f);
        Assert.True(ScratchCabinet.StatusTop(ticket, kiosk, gap) >= ticket.Max.Y + kiosk + gap - 0.001f);
    }
}
