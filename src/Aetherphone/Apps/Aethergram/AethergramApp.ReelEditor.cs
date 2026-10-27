using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Reels;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private enum ReelTrimDrag : byte
    {
        None,
        Start,
        End,
        Slide,
    }

    private const float ReelEditGap = 12f;
    private const float ReelPreviewRounding = 14f;
    private const float ReelFramingHeight = 34f;
    private const float ReelLabelRowHeight = 22f;
    private const float ReelStripHeight = 52f;
    private const float ReelStripRounding = 8f;
    private const float ReelHandleWidth = 14f;
    private const float ReelHandleHit = 26f;
    private const float ReelHandleGrip = 16f;
    private const float ReelWindowStroke = 3f;
    private const float ReelCropStroke = 2f;
    private const float ReelHintRowHeight = 20f;
    private const float ReelScrubberHeight = 6f;
    private const float ReelScrubberRowHeight = 28f;
    private const float ReelOverlayPad = 8f;
    private const float ReelOverlayInset = 10f;
    private const int SecondsKeyShift = 20;

    private readonly string[] reelFramingLabels = new string[2];
    private Action? reelEditBack;
    private Action? reelDetailsBack;
    private ReelTrimDrag reelTrimDrag;
    private bool reelScrubbing;
    private double reelSlideOffset;
    private CachedText reelLengthText;
    private CachedText reelRangeText;
    private CachedText reelCoverTime;
    private CachedText reelTrimHint;

    private Action ReelEditBack => reelEditBack ??= () =>
    {
        reelFrames.Close();
        reelPath = string.Empty;
        reelStage = ReelComposeStage.Pick;
    };

    private Action ReelDetailsBack => reelDetailsBack ??= () =>
    {
        composeTagMode = false;
        reelStage = ReelComposeStage.Edit;
    };

    private double ReelPreviewSeconds => reelTrimDrag switch
    {
        ReelTrimDrag.Start => reelEdit.StartSeconds,
        ReelTrimDrag.End => reelEdit.EndSeconds,
        _ => reelEdit.CoverSeconds,
    };

    private void DrawReelEdit(Rect area)
    {
        var scale = UiScale.Current;
        if (DrawComposeHeader(area, Loc.T(L.Aethergram.ReelEditTitle), false, ReelEditBack, Loc.T(L.Aethergram.Next),
                true))
        {
            reelTrimDrag = ReelTrimDrag.None;
            reelStage = ReelComposeStage.Details;
            captionFocus = true;
            return;
        }

        var margin = CellPadX * scale;
        var gap = ReelEditGap * scale;
        var left = area.Min.X + margin;
        var right = area.Max.X - margin;
        var cursor = area.Max.Y - margin;
        var scrubberRow = new Rect(new Vector2(left, cursor - ReelScrubberRowHeight * scale), new Vector2(right, cursor));
        cursor = scrubberRow.Min.Y;
        var coverLabel = new Rect(new Vector2(left, cursor - ReelLabelRowHeight * scale), new Vector2(right, cursor));
        cursor = coverLabel.Min.Y - gap;
        var hintRow = new Rect(new Vector2(left, cursor - ReelHintRowHeight * scale), new Vector2(right, cursor));
        cursor = hintRow.Min.Y;
        var strip = new Rect(new Vector2(left, cursor - ReelStripHeight * scale), new Vector2(right, cursor));
        cursor = strip.Min.Y - Metrics.Space.Xs * scale;
        var trimLabel = new Rect(new Vector2(left, cursor - ReelLabelRowHeight * scale), new Vector2(right, cursor));
        cursor = trimLabel.Min.Y - gap;
        if (reelEdit.CanFill)
        {
            var framingRow = new Rect(new Vector2(left, cursor - ReelFramingHeight * scale), new Vector2(right, cursor));
            DrawReelFraming(framingRow);
            cursor = framingRow.Min.Y - gap;
        }

        var previewRegion = new Rect(new Vector2(left, area.Min.Y + AppHeader.Height * scale + gap),
            new Vector2(right, cursor));
        DrawReelTrim(trimLabel, strip, hintRow, scale);
        DrawReelCover(coverLabel, scrubberRow, scale);
        reelFrames.Show(ReelPreviewSeconds, reelTrimDrag == ReelTrimDrag.None && !reelScrubbing);
        DrawReelPreview(previewRegion, scale);
    }

    private void DrawReelPreview(Rect region, float scale)
    {
        if (region.Width <= 0f || region.Height <= 0f || reelEdit.DisplayHeight <= 0)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var preview = ImageFit.CenteredRect(region, (float)reelEdit.DisplayWidth / reelEdit.DisplayHeight);
        var rounding = ReelPreviewRounding * scale;
        if (reelFrames.Preview is { } texture)
        {
            drawList.AddImageRounded(texture.Handle, preview.Min, preview.Max, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Ink.White), rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            Squircle.Fill(drawList, preview.Min, preview.Max, rounding, ImGui.GetColorU32(theme.SurfaceMuted));
            LoadingPulse.Spinner(preview.Center, ReelSpinnerRadius * scale, Accent);
        }

        if (reelEdit.Crop is not { } crop)
        {
            return;
        }

        var window = new Rect(preview.Min + new Vector2(crop.X * preview.Width, crop.Y * preview.Height),
            preview.Min + new Vector2((crop.X + crop.Width) * preview.Width, (crop.Y + crop.Height) * preview.Height));
        var scrim = ImGui.GetColorU32(Ink.Scrim);
        drawList.AddRectFilled(preview.Min, new Vector2(preview.Max.X, window.Min.Y), scrim);
        drawList.AddRectFilled(new Vector2(preview.Min.X, window.Max.Y), preview.Max, scrim);
        drawList.AddRectFilled(new Vector2(preview.Min.X, window.Min.Y), new Vector2(window.Min.X, window.Max.Y), scrim);
        drawList.AddRectFilled(new Vector2(window.Max.X, window.Min.Y), new Vector2(preview.Max.X, window.Max.Y), scrim);
        drawList.AddRect(window.Min, window.Max, ImGui.GetColorU32(Ink.White), 0f, ImDrawFlags.None,
            ReelCropStroke * scale);
        ImGui.SetCursorScreenPos(window.Min);
        ImGui.InvisibleButton("##aethergram.reel.crop", Vector2.Max(window.Size, Vector2.One));
        var active = ImGui.IsItemActive();
        if (active || ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(reelEdit.FillsHorizontally ? ImGuiMouseCursor.ResizeEw : ImGuiMouseCursor.ResizeNs);
        }

        if (active)
        {
            var delta = ImGui.GetIO().MouseDelta;
            reelEdit.DragFill(reelEdit.FillsHorizontally ? delta.X : delta.Y,
                reelEdit.FillsHorizontally ? preview.Width : preview.Height);
        }

        DrawReelOverlayCaption(drawList, new Vector2(window.Center.X, window.Max.Y - ReelOverlayInset * scale),
            Loc.T(L.Aethergram.ReelCropHint), window.Width, scale);
    }

    private void DrawReelOverlayCaption(ImDrawListPtr drawList, Vector2 bottomCenter, string text, float maxWidth,
        float scale)
    {
        var pad = ReelOverlayPad * scale;
        var fitted = Typography.FitText(text, MathF.Max(1f, maxWidth - pad * 4f), TextStyles.Caption1);
        var size = Typography.Measure(fitted, TextStyles.Caption1);
        var min = new Vector2(bottomCenter.X - size.X * 0.5f - pad, bottomCenter.Y - size.Y - pad);
        var max = new Vector2(bottomCenter.X + size.X * 0.5f + pad, bottomCenter.Y);
        Squircle.Fill(drawList, min, max, (max.Y - min.Y) * 0.5f, ImGui.GetColorU32(Ink.Scrim));
        Typography.Draw(drawList, new Vector2(min.X + pad, min.Y + pad * 0.5f), fitted, Ink.White, TextStyles.Caption1);
    }

    private void DrawReelFraming(Rect row)
    {
        reelFramingLabels[(int)ReelFraming.Original] = Loc.T(L.Aethergram.ReelFramingOriginal);
        reelFramingLabels[(int)ReelFraming.Fill] = Loc.T(L.Aethergram.ReelFramingFill);
        var current = (int)reelEdit.Framing;
        var picked = SegmentStrip.Draw("aethergram.reel.framing", row, reelFramingLabels, current, ui.Palette);
        if (picked != current)
        {
            reelEdit.SetFraming((ReelFraming)picked);
        }
    }

    private void DrawReelTrim(Rect label, Rect strip, Rect hintRow, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lengthKey = (long)Math.Round(reelEdit.LengthSeconds);
        var length = reelLengthText.IsCurrent(lengthKey)
            ? reelLengthText.Value
            : reelLengthText.Store(lengthKey,
                Loc.T(L.Aethergram.ReelLength, TimeText.MinutesSeconds((int)lengthKey)));
        var startWhole = (int)reelEdit.StartSeconds;
        var endWhole = (int)Math.Ceiling(reelEdit.EndSeconds);
        var rangeKey = ((long)startWhole << SecondsKeyShift) | (uint)endWhole;
        var range = reelRangeText.IsCurrent(rangeKey)
            ? reelRangeText.Value
            : reelRangeText.Store(rangeKey, Loc.T(L.Aethergram.ReelRange, TimeText.MinutesSeconds(startWhole),
                TimeText.MinutesSeconds(endWhole)));
        var lineHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        Typography.Draw(drawList, new Vector2(label.Min.X, label.Center.Y - lineHeight * 0.5f), length, Ink.TitleInk,
            TextStyles.SubheadlineEmphasized);
        var rangeSize = Typography.Measure(range, TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(label.Max.X - rangeSize.X, label.Center.Y - rangeSize.Y * 0.5f), range,
            Ink.MutedInk, TextStyles.Footnote);

        DrawReelStrip(drawList, strip, scale);
        var hint = reelTrimHint.IsCurrent(MaxReelSeconds)
            ? reelTrimHint.Value
            : reelTrimHint.Store(MaxReelSeconds, Loc.Plural(L.Aethergram.ReelTrimHint, MaxReelSeconds));
        Typography.DrawCentered(drawList, hintRow.Center,
            Typography.FitText(hint, hintRow.Width, TextStyles.Caption1), Ink.MutedInk, TextStyles.Caption1);
    }

    private void DrawReelStrip(ImDrawListPtr drawList, Rect strip, float scale)
    {
        var rounding = ReelStripRounding * scale;
        Squircle.Fill(drawList, strip.Min, strip.Max, rounding, ImGui.GetColorU32(theme.SurfaceMuted));
        var slotWidth = strip.Width / ReelEditorFrames.StripCount;
        for (var index = 0; index < ReelEditorFrames.StripCount; index++)
        {
            if (reelFrames.StripAt(index) is not { } thumbnail)
            {
                continue;
            }

            var slotMin = new Vector2(strip.Min.X + slotWidth * index, strip.Min.Y);
            var slotMax = new Vector2(slotMin.X + slotWidth, strip.Max.Y);
            var (uv0, uv1) = ImageFit.Cover(thumbnail.Width, thumbnail.Height, slotWidth, strip.Height);
            drawList.AddImage(thumbnail.Handle, slotMin, slotMax, uv0, uv1);
        }

        var duration = Math.Max(reelEdit.DurationSeconds, ReelEdit.MinimumLengthSeconds);
        var startX = strip.Min.X + (float)(reelEdit.StartSeconds / duration) * strip.Width;
        var endX = strip.Min.X + (float)(reelEdit.EndSeconds / duration) * strip.Width;
        var scrim = ImGui.GetColorU32(Ink.Scrim);
        drawList.AddRectFilled(strip.Min, new Vector2(startX, strip.Max.Y), scrim);
        drawList.AddRectFilled(new Vector2(endX, strip.Min.Y), strip.Max, scrim);
        var accent = ImGui.GetColorU32(Ink.Accent);
        drawList.AddRect(new Vector2(startX, strip.Min.Y), new Vector2(endX, strip.Max.Y), accent, rounding,
            ImDrawFlags.RoundCornersAll, ReelWindowStroke * scale);
        DrawReelHandle(drawList, startX, strip, scale, accent);
        DrawReelHandle(drawList, endX, strip, scale, accent);

        var hit = ReelHandleHit * scale;
        var middleMin = new Vector2(startX + hit * 0.5f, strip.Min.Y);
        var middleMax = new Vector2(MathF.Max(middleMin.X + 1f, endX - hit * 0.5f), strip.Max.Y);
        var mouseSeconds = SecondsAt(strip, ImGui.GetMousePos().X, duration);
        var dragging = ReelTrimDrag.None;
        if (ReelDragArea("##aethergram.reel.slide", middleMin, middleMax, ImGuiMouseCursor.Hand, out var slideStarted))
        {
            if (slideStarted)
            {
                reelSlideOffset = mouseSeconds - reelEdit.StartSeconds;
            }

            reelEdit.Slide(mouseSeconds - reelSlideOffset);
            dragging = ReelTrimDrag.Slide;
        }

        if (ReelDragArea("##aethergram.reel.start", new Vector2(startX - hit * 0.5f, strip.Min.Y),
                new Vector2(startX + hit * 0.5f, strip.Max.Y), ImGuiMouseCursor.ResizeEw, out _))
        {
            reelEdit.MoveStart(mouseSeconds);
            dragging = ReelTrimDrag.Start;
        }

        if (ReelDragArea("##aethergram.reel.end", new Vector2(endX - hit * 0.5f, strip.Min.Y),
                new Vector2(endX + hit * 0.5f, strip.Max.Y), ImGuiMouseCursor.ResizeEw, out _))
        {
            reelEdit.MoveEnd(mouseSeconds);
            dragging = ReelTrimDrag.End;
        }

        reelTrimDrag = dragging;
    }

    private static void DrawReelHandle(ImDrawListPtr drawList, float x, Rect strip, float scale, uint accent)
    {
        var half = ReelHandleWidth * scale * 0.5f;
        var min = new Vector2(x - half, strip.Min.Y);
        var max = new Vector2(x + half, strip.Max.Y);
        drawList.AddRectFilled(min, max, accent, ReelStripRounding * scale * 0.5f, ImDrawFlags.RoundCornersAll);
        var grip = ReelHandleGrip * scale * 0.5f;
        drawList.AddLine(new Vector2(x, strip.Center.Y - grip), new Vector2(x, strip.Center.Y + grip),
            ImGui.GetColorU32(Ink.White), ReelCropStroke * scale);
    }

    private static bool ReelDragArea(string id, Vector2 min, Vector2 max, ImGuiMouseCursor cursor, out bool started)
    {
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton(id, Vector2.Max(max - min, Vector2.One));
        started = ImGui.IsItemActivated();
        var active = ImGui.IsItemActive();
        if (active || ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(cursor);
        }

        return active;
    }

    private static double SecondsAt(Rect strip, float x, double duration) =>
        Math.Clamp((x - strip.Min.X) / MathF.Max(1f, strip.Width), 0f, 1f) * duration;

    private void DrawReelCover(Rect label, Rect scrubberRow, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        Typography.Draw(drawList, new Vector2(label.Min.X, label.Center.Y - lineHeight * 0.5f),
            Loc.T(L.Aethergram.ReelCover), Ink.TitleInk, TextStyles.SubheadlineEmphasized);
        var coverKey = (long)reelEdit.CoverSeconds;
        var time = reelCoverTime.IsCurrent(coverKey)
            ? reelCoverTime.Value
            : reelCoverTime.Store(coverKey, TimeText.MinutesSeconds((int)coverKey));
        var timeSize = Typography.Measure(time, TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(label.Max.X - timeSize.X, label.Center.Y - timeSize.Y * 0.5f), time,
            Ink.MutedInk, TextStyles.Footnote);
        var labelWidth = Typography.Measure(Loc.T(L.Aethergram.ReelCover), TextStyles.SubheadlineEmphasized).X;
        var hintLeft = label.Min.X + labelWidth + Metrics.Space.Sm * scale;
        var hintWidth = label.Max.X - timeSize.X - Metrics.Space.Sm * scale - hintLeft;
        if (hintWidth > 0f)
        {
            var hint = Typography.FitText(Loc.T(L.Aethergram.ReelCoverHint), hintWidth, TextStyles.Caption1);
            var hintHeight = Typography.LineHeight(TextStyles.Caption1);
            Typography.Draw(drawList, new Vector2(hintLeft, label.Center.Y - hintHeight * 0.5f), hint, Ink.MutedInk,
                TextStyles.Caption1);
        }

        var length = reelEdit.LengthSeconds;
        var value = length > 0d ? (float)((reelEdit.CoverSeconds - reelEdit.StartSeconds) / length) : 0f;
        var track = new Rect(new Vector2(scrubberRow.Min.X, scrubberRow.Center.Y - ReelScrubberHeight * scale * 0.5f),
            new Vector2(scrubberRow.Max.X, scrubberRow.Center.Y + ReelScrubberHeight * scale * 0.5f));
        reelScrubbing = ImGui.IsMouseDown(ImGuiMouseButton.Left) && Scrubber.IsHovered(track);
        var picked = Scrubber.Draw(track, value, Ink.Accent, Ink.MutedInk, 1f);
        if (MathF.Abs(picked - value) > float.Epsilon)
        {
            reelEdit.SetCover(reelEdit.StartSeconds + picked * length);
        }
    }
}
