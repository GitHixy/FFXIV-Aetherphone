using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Core.Shell;

internal sealed partial class DynamicIsland
{
    private const float PlaybackCardPadY = 14f;
    private const float PlaybackTrackGap = 14f;
    private const float PlaybackTrackThickness = 4f;
    private const float PlaybackTimeGap = 8f;
    private const float PlaybackTransportGap = 30f;
    private const float PlaybackTimeAlpha = 0.72f;
    private const float PlaybackDisabledAlpha = 0.4f;
    private const float ModeGlyphFraction = 0.42f;
    private const float ModeIdleAlpha = 0.7f;
    private const float ModeHoverAlpha = 0.2f;
    private const float ModeDotRadius = 1.6f;
    private const float ModeDotGap = 4f;
    private const string RemainingSign = "-";
    private const string RepeatOneLabel = "1";

    private bool playbackScrubbing;
    private float playbackScrubValue;

    private enum MediaCommand : byte
    {
        PlayPause,
        Previous,
        Next,
        Shuffle,
        Repeat,
    }

    private bool DrawPlaybackExpanded(ImDrawListPtr drawList, Rect bounds, float scale, Vector4 accent, float alpha,
        bool active)
    {
        var media = IslandMedia.ForPhone(playback.Title, playback.Subtitle, playback.Position, playback.Duration,
            playback.IsPlaying, playback.SongActive, playback.ShuffleEnabled, playback.RepeatMode);
        var artCenter = MediaArtCenter(bounds, scale);
        DrawPlaybackArt(drawList, artCenter, CardIconRadius * scale, alpha);
        return DrawMediaCard(drawList, bounds, artCenter, media, false, scale, accent, alpha, active);
    }

    private static Vector2 MediaArtCenter(Rect bounds, float scale)
    {
        var iconRadius = CardIconRadius * scale;
        return new Vector2(bounds.Min.X + CardPadX * scale + iconRadius,
            bounds.Min.Y + PlaybackCardPadY * scale + iconRadius);
    }

    private bool DrawMediaCard(ImDrawListPtr drawList, Rect bounds, Vector2 artCenter, in IslandMedia media, bool pc,
        float scale, Vector4 accent, float alpha, bool active)
    {
        var iconRadius = CardIconRadius * scale;
        var textLeft = artCenter.X + iconRadius + CardTextGap * scale;
        var textWidth = MathF.Max(1f, bounds.Max.X - CardPadX * scale - textLeft);
        DrawLines(drawList, media.Title, TextStyles.Headline, Ink, media.Subtitle, TextStyles.Subheadline, accent,
            textLeft, textWidth, artCenter.Y, scale, alpha, true);
        var artBottom = artCenter.Y + iconRadius;
        var overTimeline = false;
        float transportY;
        if (media.HasTimeline)
        {
            var trackY = artBottom + PlaybackTrackGap * scale;
            overTimeline = DrawPlaybackTimeline(drawList, bounds, trackY, media, pc, scale, accent, alpha, active);
            transportY = trackY + PlaybackTransportGap * scale;
        }
        else
        {
            playbackScrubbing = false;
            transportY = (artBottom + bounds.Max.Y - PlaybackCardPadY * scale) * 0.5f;
        }

        var center = new Vector2(bounds.Center.X, transportY);
        var overTransport = DrawPlaybackTransport(drawList, center, media, pc, scale, accent, alpha, active);
        var overModes = DrawPlaybackModes(drawList, center, media, pc, scale, accent, alpha, active);
        return overTimeline || overTransport || overModes;
    }

    private bool DrawPlaybackTimeline(ImDrawListPtr drawList, Rect bounds, float centerY, in IslandMedia media,
        bool pc, float scale, Vector4 accent, float alpha, bool active)
    {
        var padX = CardPadX * scale;
        var duration = media.Duration;
        var fraction = playbackScrubbing ? playbackScrubValue : Math.Clamp(media.Position / duration, 0f, 1f);
        var shownSeconds = fraction * duration;
        var elapsed = TimeText.Duration((int)shownSeconds);
        var remaining = TimeText.Duration(IslandActivities.RemainingSeconds(shownSeconds, duration));
        var style = TextStyles.Caption1;
        var elapsedWidth = WidgetText.TabularWidth(elapsed, style);
        var signWidth = Typography.Measure(RemainingSign, style).X;
        var remainingWidth = WidgetText.TabularWidth(remaining, style) + signWidth;
        var textTop = centerY - Typography.LineHeight(style) * 0.5f;
        var color = Palette.WithAlpha(Ink, PlaybackTimeAlpha * alpha);
        WidgetText.Tabular(drawList, new Vector2(bounds.Min.X + padX, textTop), elapsed, color, style);
        var remainingLeft = bounds.Max.X - padX - remainingWidth;
        Typography.Draw(drawList, new Vector2(remainingLeft, textTop), RemainingSign, color, style);
        WidgetText.Tabular(drawList, new Vector2(remainingLeft + signWidth, textTop), remaining, color, style);
        var gap = PlaybackTimeGap * scale;
        var halfThickness = PlaybackTrackThickness * scale * 0.5f;
        var track = new Rect(new Vector2(bounds.Min.X + padX + elapsedWidth + gap, centerY - halfThickness),
            new Vector2(remainingLeft - gap, centerY + halfThickness));
        if (track.Width <= 0f)
        {
            return false;
        }

        var rail = Palette.WithAlpha(Ink, ControlFillAlpha);
        if (!active || !media.Has(IslandMediaControls.Seek))
        {
            playbackScrubbing = false;
            DrawPlaybackRail(drawList, track, fraction, accent, rail, alpha);
            return false;
        }

        var hovered = Scrubber.IsHovered(track);
        var updated = Scrubber.Draw(track, fraction, accent, rail, alpha);
        var wasScrubbing = playbackScrubbing;
        if ((wasScrubbing || hovered) && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            playbackScrubbing = true;
            playbackScrubValue = updated;
            UiInteract.CancelPendingTap();
        }
        else if (wasScrubbing)
        {
            playbackScrubbing = false;
            SeekMedia(pc, playbackScrubValue * duration);
            UiInteract.CancelPendingTap();
        }

        return hovered || wasScrubbing;
    }

    private static void DrawPlaybackRail(ImDrawListPtr drawList, Rect track, float fraction, Vector4 accent,
        Vector4 rail, float alpha)
    {
        var rounding = track.Height * 0.5f;
        drawList.AddRectFilled(track.Min, track.Max, ImGui.GetColorU32(Palette.WithAlpha(rail, alpha)), rounding);
        var fillMax = new Vector2(track.Min.X + track.Width * fraction, track.Max.Y);
        drawList.AddRectFilled(track.Min, fillMax, ImGui.GetColorU32(Palette.WithAlpha(accent, alpha)), rounding);
    }

    private bool DrawPlaybackTransport(ImDrawListPtr drawList, Vector2 center, in IslandMedia media, bool pc,
        float scale, Vector4 accent, float alpha, bool active)
    {
        var sideRadius = IslandActivities.SideControlRadiusUnits * scale;
        var playRadius = CardControlRadius * scale;
        var stride = IslandActivities.TransportStrideUnits * scale;
        var previousCenter = center - new Vector2(stride, 0f);
        var nextCenter = center + new Vector2(stride, 0f);
        var extent = new Vector2(playRadius, playRadius);
        var overControl = active && UiInteract.Hover(previousCenter - extent, nextCenter + extent);
        if (media.Has(IslandMediaControls.Previous) && TransportButton.Draw(previousCenter, sideRadius,
                TransportAction.Previous, accent, Ink, alpha, active, drawList))
        {
            RunMedia(MediaCommand.Previous, pc);
        }

        drawList.AddCircleFilled(center, playRadius,
            ImGui.GetColorU32(Palette.WithAlpha(Ink, ControlFillAlpha * alpha)), 32);
        var canToggle = media.Has(IslandMediaControls.PlayPause);
        var playAlpha = canToggle ? alpha : alpha * PlaybackDisabledAlpha;
        var playAction = media.Has(IslandMediaControls.Playing) ? TransportAction.Pause : TransportAction.Play;
        if (TransportButton.Draw(center, playRadius, playAction, accent, Ink, playAlpha, active && canToggle,
                drawList))
        {
            RunMedia(MediaCommand.PlayPause, pc);
        }

        if (media.Has(IslandMediaControls.Next) && TransportButton.Draw(nextCenter, sideRadius, TransportAction.Next,
                accent, Ink, alpha, active, drawList))
        {
            RunMedia(MediaCommand.Next, pc);
        }

        return overControl;
    }

    private bool DrawPlaybackModes(ImDrawListPtr drawList, Vector2 center, in IslandMedia media, bool pc,
        float scale, Vector4 accent, float alpha, bool active)
    {
        var radius = IslandActivities.ModeRadiusUnits * scale;
        var offset = new Vector2(IslandActivities.ModeStrideUnits * scale, 0f);
        var over = false;
        if (media.Has(IslandMediaControls.Shuffle))
        {
            if (DrawPlaybackMode(drawList, center - offset, radius, false,
                    media.Has(IslandMediaControls.ShuffleActive), false, scale, accent, alpha, active,
                    out var shuffleHovered))
            {
                RunMedia(MediaCommand.Shuffle, pc);
            }

            over |= shuffleHovered;
        }

        if (media.Has(IslandMediaControls.Repeat))
        {
            if (DrawPlaybackMode(drawList, center + offset, radius, true, media.Has(IslandMediaControls.RepeatActive),
                    media.Has(IslandMediaControls.RepeatOne), scale, accent, alpha, active, out var repeatHovered))
            {
                RunMedia(MediaCommand.Repeat, pc);
            }

            over |= repeatHovered;
        }

        return over;
    }

    private static bool DrawPlaybackMode(ImDrawListPtr drawList, Vector2 center, float radius, bool repeat, bool on,
        bool one, float scale, Vector4 accent, float alpha, bool active, out bool hovered)
    {
        var reach = new Vector2(radius, radius);
        hovered = active && UiInteract.Hover(center - reach, center + reach);
        if (hovered)
        {
            drawList.AddCircleFilled(center, radius,
                ImGui.GetColorU32(Palette.WithAlpha(accent, ModeHoverAlpha * alpha)), 32);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var tint = on || hovered ? accent : Palette.WithAlpha(Ink, ModeIdleAlpha);
        var ink = Palette.WithAlpha(tint, tint.W * alpha);
        var color = ImGui.GetColorU32(ink);
        var size = radius * ModeGlyphFraction;
        if (repeat)
        {
            MediaGlyph.Repeat(drawList, center, size, color);
        }
        else
        {
            MediaGlyph.Shuffle(drawList, center, size, color);
        }

        if (one)
        {
            Typography.DrawCentered(drawList, center, RepeatOneLabel, ink, TextStyles.Caption2);
        }

        if (on)
        {
            drawList.AddCircleFilled(new Vector2(center.X, center.Y + size + ModeDotGap * scale),
                ModeDotRadius * scale, color, 12);
        }

        return UiInteract.Click(center - reach, center + reach, hovered);
    }

    private void SeekMedia(bool pc, float seconds)
    {
        if (pc)
        {
            pcMedia?.Seek(TimeSpan.FromSeconds(seconds));
            return;
        }

        playback.Seek(seconds);
    }

    private void RunMedia(MediaCommand command, bool pc)
    {
        if (pc)
        {
            RunPcMedia(command);
            return;
        }

        switch (command)
        {
            case MediaCommand.PlayPause:
                playback.TogglePlayPause();
                break;
            case MediaCommand.Previous:
                playback.Previous();
                break;
            case MediaCommand.Next:
                playback.Next();
                break;
            case MediaCommand.Shuffle:
                playback.ToggleShuffle();
                break;
            case MediaCommand.Repeat:
                playback.ToggleRepeat();
                break;
        }
    }

    private void RunPcMedia(MediaCommand command)
    {
        if (pcMedia is null)
        {
            return;
        }

        switch (command)
        {
            case MediaCommand.PlayPause:
                pcMedia.TogglePlayPause(pcSnapshot);
                break;
            case MediaCommand.Previous:
                pcMedia.Previous();
                break;
            case MediaCommand.Next:
                pcMedia.Next();
                break;
            case MediaCommand.Shuffle:
                pcMedia.ToggleShuffle(pcSnapshot);
                break;
            case MediaCommand.Repeat:
                pcMedia.CycleRepeat(pcSnapshot);
                break;
        }
    }
}
