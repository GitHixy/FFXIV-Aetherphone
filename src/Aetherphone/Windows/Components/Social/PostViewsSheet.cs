using System.Globalization;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Notifications;
using Aetherphone.Core.Social;
using Aetherphone.Core.Theme;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Windows.Components;

internal sealed class PostViewsSheet
{
    private const int GroupCount = 8;
    private const int ReelsRow = 1;
    private const int EarlierRow = GroupCount - 1;
    private const float MaxDim = 0.45f;
    private const float Rounding = 24f;
    private const float PadX = 18f;
    private const float GrabberWidth = 38f;
    private const float GrabberHeight = 4.5f;
    private const float SplitBarHeight = 8f;
    private const float RowHeight = 46f;
    private const float RowBarHeight = 4f;
    private const float LegendDot = 8f;
    private const float StatusHeight = 120f;
    private const float DoneHeight = Button.LargeHeight;
    private const string DoneKey = "##postviews.done";
    private const float BottomPad = 30f;
    private const float PanelLift = 0.08f;
    private const float PanelAlpha = 0.96f;
    private const float TrackAlpha = 0.10f;
    private const float NonFollowerAlpha = 0.38f;
    private const float LineSpacing = 1.3f;

    private static readonly TextStyle TitleStyle = new(1.13f, FontWeight.Bold);
    private static readonly Vector4 GrabberFill = new(1f, 1f, 1f, 0.22f);
    private static readonly Vector4 PanelStroke = new(1f, 1f, 1f, 0.10f);

    private static readonly LocString[] GroupLabels =
    {
        L.Social.ViewsSourceHome,
        L.Aethergram.Reels,
        L.Social.ViewsSourceExplore,
        L.Social.ViewsSourceProfile,
        L.Social.ViewsSourceHashtags,
        L.Social.ViewsSourceStories,
        L.Social.ViewsSourceOther,
        L.Social.ViewsSourceEarlier,
    };

    private readonly int[] groupCounts = new int[GroupCount];
    private readonly int[] groupPercents = new int[GroupCount];
    private readonly string[] groupCountTexts = new string[GroupCount];
    private readonly string[] groupPercentTexts = new string[GroupCount];
    private readonly int[] splitPercents = new int[2];
    private PostViewsDto? formattedViews;
    private CultureInfo? formattedCulture;
    private string totalText = string.Empty;
    private string followersText = string.Empty;
    private string nonFollowersText = string.Empty;
    private int attributed;
    private Spring reveal;
    private bool open;
    private bool includeReels;
    private int openedFrame;

    public bool IsOpen => open;

    public bool CapturesPointer => open || !reveal.IsResting(0f, 0.001f, 0.005f);

    public void Open()
    {
        if (!open)
        {
            openedFrame = ImGui.GetFrameCount();
            UiFeedback.Play(UiSound.SheetPresent);
        }

        open = true;
    }

    public void Close()
    {
        if (!open)
        {
            return;
        }

        open = false;
        UiFeedback.Play(UiSound.SheetDismiss);
    }

    public void Gate()
    {
        if (open)
        {
            UiInteract.BlockThisFrame();
        }
    }

    public void Draw(Rect screen, SocialInk ink, PostViewsDto? views, bool loading, bool reel = false)
    {
        includeReels = reel;
        var delta = MathF.Min(ImGui.GetIO().DeltaTime, TransitionTiming.MaxFrameSeconds);
        reveal.Step(open ? 1f : 0f, Motion.Sheet, delta);
        if (!open && reveal.IsResting(0f, 0.001f, 0.005f))
        {
            reveal.SnapTo(0f);
            return;
        }

        if (views is not null)
        {
            Format(views);
        }

        var scale = UiScale.Current;
        var opacity = Math.Clamp(reveal.Value, 0f, 1f);
        var drawList = ImGui.GetForegroundDrawList();
        drawList.PushClipRect(screen.Min, screen.Max, false);
        drawList.AddRectFilled(screen.Min, screen.Max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, MaxDim * opacity)));

        var padX = PadX * scale;
        var titleHeight = Typography.LineHeight(TitleStyle);
        var bodyHeight = views is null ? StatusHeight * scale : ContentHeight(screen.Width - padX * 2f, scale);
        var panelHeight = (8f + GrabberHeight + 12f) * scale + titleHeight + Metrics.Space.Md * scale + bodyHeight
            + Metrics.Space.Lg * scale + DoneHeight * scale + BottomPad * scale;
        panelHeight = MathF.Min(panelHeight, screen.Height * 0.92f);
        var panelTop = screen.Max.Y - panelHeight + panelHeight * (1f - opacity);
        var panelMin = new Vector2(screen.Min.X, panelTop);
        var panelMax = new Vector2(screen.Max.X, screen.Max.Y + Rounding * scale);
        var rounding = Rounding * scale;
        var panelFill = Palette.WithAlpha(Palette.Lighten(ink.BackdropTop, PanelLift), PanelAlpha * opacity);
        Squircle.Fill(drawList, panelMin, panelMax, rounding, ImGui.GetColorU32(panelFill));
        Squircle.Stroke(drawList, panelMin, panelMax, rounding,
            ImGui.GetColorU32(Palette.WithAlpha(PanelStroke, PanelStroke.W * opacity)), 1f);
        var interactive = open && reveal.Arrive(1f);

        var grabberMin = new Vector2(screen.Center.X - GrabberWidth * scale * 0.5f, panelTop + 8f * scale);
        drawList.AddRectFilled(grabberMin, grabberMin + new Vector2(GrabberWidth, GrabberHeight) * scale,
            ImGui.GetColorU32(Palette.WithAlpha(GrabberFill, GrabberFill.W * opacity)), GrabberHeight * scale * 0.5f);
        var left = panelMin.X + padX;
        var right = panelMax.X - padX;
        var cursorY = grabberMin.Y + (GrabberHeight + 12f) * scale;
        Typography.DrawCentered(drawList, new Vector2(screen.Center.X, cursorY + titleHeight * 0.5f),
            Typography.FitText(Loc.T(L.Social.Views), right - left, TitleStyle),
            Palette.WithAlpha(ink.TitleInk, opacity), TitleStyle);
        cursorY += titleHeight + Metrics.Space.Md * scale;

        var doneTop = screen.Max.Y - BottomPad * scale - DoneHeight * scale;
        drawList.PushClipRect(new Vector2(left, cursorY), new Vector2(right, doneTop - Metrics.Space.Sm * scale), true);
        if (views is null)
        {
            var status = loading ? Loc.T(L.Common.Loading) : Loc.T(L.Social.ViewsLoadFailed);
            Typography.DrawWrappedCentered(drawList, status, TextStyles.Callout,
                Palette.WithAlpha(ink.MutedInk, ink.MutedInk.W * opacity),
                new Vector2(screen.Center.X, cursorY + StatusHeight * scale * 0.35f), right - left, LineSpacing);
        }
        else
        {
            DrawContent(drawList, ink, left, right, cursorY, opacity, scale);
        }

        drawList.PopClipRect();

        var doneMin = new Vector2(left, doneTop);
        var doneMax = new Vector2(right, doneTop + DoneHeight * scale);
        var doneHovered = interactive && UiInteract.HoverWindowOnly(doneMin, doneMax, false);
        var doneFace = Button.Surface(drawList, new Rect(doneMin, doneMax), ink.Control, ButtonStyle.Prominent,
            ButtonRole.Normal, true, doneHovered, ImGui.GetID(DoneKey), opacity);
        Button.DrawLabel(drawList, doneFace, Loc.T(L.Common.Close));
        if (doneHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(doneMin, doneMax, doneHovered))
        {
            Close();
        }

        drawList.PopClipRect();
        if (interactive && ImGui.GetFrameCount() != openedFrame && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && !UiInteract.HoverWindowOnly(panelMin, panelMax, false))
        {
            Close();
        }
    }

    private float ContentHeight(float width, float scale)
    {
        var height = Typography.LineHeight(TextStyles.Hero) + Metrics.Space.Xxs * scale
            + Typography.LineHeight(TextStyles.Footnote) + Metrics.Space.Xl * scale;
        if (attributed > 0)
        {
            height += SplitBarHeight * scale + Metrics.Space.Sm * scale + Typography.LineHeight(TextStyles.Footnote)
                + Metrics.Space.Xl * scale;
        }

        height += ListSection.OverlineHeight + Metrics.Space.Xs * scale + RowHeight * scale * VisibleRows();
        if (groupCounts[EarlierRow] > 0)
        {
            height += Metrics.Space.Sm * scale
                + Typography.MeasureWrappedBlock(Loc.T(L.Social.ViewsEarlierHint), TextStyles.Footnote, width).Y
                * LineSpacing;
        }

        return height;
    }

    private void DrawContent(ImDrawListPtr drawList, SocialInk ink, float left, float right, float top, float opacity,
        float scale)
    {
        var width = right - left;
        var centerX = (left + right) * 0.5f;
        var titleInk = Palette.WithAlpha(ink.TitleInk, opacity);
        var mutedInk = Palette.WithAlpha(ink.MutedInk, ink.MutedInk.W * opacity);
        var accent = Palette.WithAlpha(ink.Accent, opacity);
        var cursorY = top;
        var heroHeight = Typography.LineHeight(TextStyles.Hero);
        Typography.DrawCentered(drawList, new Vector2(centerX, cursorY + heroHeight * 0.5f),
            Typography.FitText(totalText, width, TextStyles.Hero), titleInk, TextStyles.Hero);
        cursorY += heroHeight + Metrics.Space.Xxs * scale;
        var footnoteHeight = Typography.LineHeight(TextStyles.Footnote);
        Typography.DrawCentered(drawList, new Vector2(centerX, cursorY + footnoteHeight * 0.5f),
            Typography.FitText(Loc.T(L.Social.ViewsSheetCaption), width, TextStyles.Footnote), mutedInk,
            TextStyles.Footnote);
        cursorY += footnoteHeight + Metrics.Space.Xl * scale;

        if (attributed > 0)
        {
            cursorY = DrawFollowerSplit(drawList, ink, left, right, cursorY, opacity, scale);
        }

        ListSection.PaintOverline(drawList, new Vector2(left, cursorY), Loc.T(L.Social.ViewsSources), mutedInk, width);
        cursorY += ListSection.OverlineHeight + Metrics.Space.Xs * scale;
        var rowHeight = RowHeight * scale;
        var track = Palette.WithAlpha(ink.TitleInk, TrackAlpha * opacity);
        for (var groupIndex = 0; groupIndex < GroupCount; groupIndex++)
        {
            if (!RowVisible(groupIndex))
            {
                continue;
            }

            DrawRow(drawList, groupIndex, left, right, cursorY, rowHeight, titleInk, mutedInk, accent, track, scale);
            cursorY += rowHeight;
        }

        if (groupCounts[EarlierRow] == 0)
        {
            return;
        }

        cursorY += Metrics.Space.Sm * scale;
        Typography.DrawWrappedCentered(drawList, Loc.T(L.Social.ViewsEarlierHint), TextStyles.Footnote, mutedInk,
            new Vector2(centerX, cursorY), width, LineSpacing);
    }

    private float DrawFollowerSplit(ImDrawListPtr drawList, SocialInk ink, float left, float right, float top,
        float opacity, float scale)
    {
        var barHeight = SplitBarHeight * scale;
        var followerInk = Palette.WithAlpha(ink.Accent, opacity);
        var nonFollowerInk = Palette.WithAlpha(ink.Accent, NonFollowerAlpha * opacity);
        drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, top + barHeight),
            ImGui.GetColorU32(nonFollowerInk), barHeight * 0.5f);
        var split = left + (right - left) * splitPercents[0] / 100f;
        if (splitPercents[0] > 0)
        {
            drawList.AddRectFilled(new Vector2(left, top), new Vector2(split, top + barHeight),
                ImGui.GetColorU32(followerInk), barHeight * 0.5f);
        }

        var legendY = top + barHeight + Metrics.Space.Sm * scale;
        var footnoteHeight = Typography.LineHeight(TextStyles.Footnote);
        var half = (right - left) * 0.5f;
        DrawLegend(drawList, left, legendY, footnoteHeight, half, followersText, followerInk, ink, opacity, scale);
        DrawLegend(drawList, left + half, legendY, footnoteHeight, half, nonFollowersText, nonFollowerInk, ink, opacity,
            scale);
        return legendY + footnoteHeight + Metrics.Space.Xl * scale;
    }

    private static void DrawLegend(ImDrawListPtr drawList, float left, float top, float height, float width, string text,
        Vector4 dotInk, SocialInk ink, float opacity, float scale)
    {
        var dot = LegendDot * scale;
        var centerY = top + height * 0.5f;
        drawList.AddCircleFilled(new Vector2(left + dot * 0.5f, centerY), dot * 0.5f, ImGui.GetColorU32(dotInk), 12);
        var textLeft = left + dot + Metrics.Space.Xs * scale;
        Typography.Draw(drawList, new Vector2(textLeft, top),
            Typography.FitText(text, MathF.Max(1f, width - (textLeft - left) - Metrics.Space.Sm * scale),
                TextStyles.Footnote),
            Palette.WithAlpha(ink.BodyInk, ink.BodyInk.W * opacity), TextStyles.Footnote);
    }

    private void DrawRow(ImDrawListPtr drawList, int groupIndex, float left, float right, float top, float rowHeight,
        Vector4 titleInk, Vector4 mutedInk, Vector4 accent, Vector4 track, float scale)
    {
        var lineHeight = Typography.LineHeight(TextStyles.Body);
        var textTop = top + (rowHeight - lineHeight - Metrics.Space.Xs * scale - RowBarHeight * scale) * 0.5f;
        var percentText = groupPercentTexts[groupIndex];
        var percentWidth = Typography.Measure(percentText, TextStyles.Footnote).X;
        var countText = groupCountTexts[groupIndex];
        var countWidth = Typography.Measure(countText, TextStyles.BodyEmphasized).X;
        var gap = Metrics.Space.Sm * scale;
        var percentLeft = right - percentWidth;
        var countLeft = percentLeft - gap - countWidth;
        var labelWidth = MathF.Max(1f, countLeft - gap - left);
        Typography.Draw(drawList, new Vector2(left, textTop),
            Typography.FitText(Loc.T(GroupLabels[groupIndex]), labelWidth, TextStyles.Body), titleInk, TextStyles.Body);
        Typography.Draw(drawList, new Vector2(countLeft, textTop), countText, titleInk, TextStyles.BodyEmphasized);
        var footnoteHeight = Typography.LineHeight(TextStyles.Footnote);
        Typography.Draw(drawList, new Vector2(percentLeft, textTop + (lineHeight - footnoteHeight) * 0.5f), percentText,
            mutedInk, TextStyles.Footnote);
        var barTop = textTop + lineHeight + Metrics.Space.Xs * scale;
        var barHeight = RowBarHeight * scale;
        drawList.AddRectFilled(new Vector2(left, barTop), new Vector2(right, barTop + barHeight),
            ImGui.GetColorU32(track), barHeight * 0.5f);
        if (groupPercents[groupIndex] <= 0)
        {
            return;
        }

        var fillRight = left + (right - left) * groupPercents[groupIndex] / 100f;
        drawList.AddRectFilled(new Vector2(left, barTop), new Vector2(fillRight, barTop + barHeight),
            ImGui.GetColorU32(accent), barHeight * 0.5f);
    }

    // Earlier shows only when old viewers exist, and Reels on a reel or once the Reels feed has shown the post.
    private bool RowVisible(int groupIndex) => groupIndex switch
    {
        EarlierRow => groupCounts[EarlierRow] > 0,
        ReelsRow => includeReels || groupCounts[ReelsRow] > 0,
        _ => true,
    };

    private int VisibleRows()
    {
        var rows = 0;
        for (var groupIndex = 0; groupIndex < GroupCount; groupIndex++)
        {
            if (RowVisible(groupIndex))
            {
                rows++;
            }
        }

        return rows;
    }

    private void Format(PostViewsDto views)
    {
        if (ReferenceEquals(views, formattedViews) && ReferenceEquals(Loc.Culture, formattedCulture))
        {
            return;
        }

        formattedViews = views;
        formattedCulture = Loc.Culture;
        groupCounts[0] = views.Home;
        groupCounts[ReelsRow] = views.Reels;
        groupCounts[2] = views.Explore;
        groupCounts[3] = views.Profile;
        groupCounts[4] = views.HashtagsAndSearch;
        groupCounts[5] = views.Stories;
        groupCounts[6] = views.Other;
        groupCounts[EarlierRow] = views.Earlier;
        VoteShares.Percentages(groupCounts, groupPercents);
        for (var groupIndex = 0; groupIndex < GroupCount; groupIndex++)
        {
            groupCountTexts[groupIndex] = CountText.Exact(groupCounts[groupIndex]);
            groupPercentTexts[groupIndex] = Loc.T(L.Social.ViewsPercent, groupPercents[groupIndex]);
        }

        totalText = CountText.Exact(views.Total);
        attributed = views.Followers + views.NonFollowers;
        Span<int> split = stackalloc int[2] { views.Followers, views.NonFollowers };
        VoteShares.Percentages(split, splitPercents);
        followersText = Loc.T(L.Social.ViewsFollowers) + " " + Loc.T(L.Social.ViewsPercent, splitPercents[0]);
        nonFollowersText = Loc.T(L.Social.ViewsNonFollowers) + " " + Loc.T(L.Social.ViewsPercent, splitPercents[1]);
    }
}
