using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Aetherphone.Windows.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const ImGuiWindowFlags EditHistoryFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                                                      ImGuiWindowFlags.NoBackground;

    private const float EditHistoryHeaderHeight = 40f;
    private const float EditHistoryMutedAlpha = 0.62f;
    private const float EditHistoryHairlineAlpha = 0.12f;

    private static readonly TextStyle EditHistoryTitleStyle = TextStyles.Headline;
    private static readonly TextStyle EditHistoryTimeStyle = TextStyles.FootnoteEmphasized;
    private static readonly TextStyle EditHistoryTextStyle = TextStyles.Body;

    private readonly Sheet chirpEditHistorySheet = new();
    private CachedText[] chirpEditTimeLabels = Array.Empty<CachedText>();
    private CachedText lastEditedLabel;
    private volatile PostEditDto[]? chirpEditVersions;
    private volatile bool chirpEditVersionsLoading;
    private volatile string? chirpEditHistoryPostId;

    private bool ChirpEditHistoryCaptures => chirpEditHistorySheet.CapturesPointer;

    private void OpenChirpEditHistory(string postId)
    {
        chirpEditHistoryPostId = postId;
        chirpEditVersions = null;
        chirpEditVersionsLoading = true;
        chirpEditHistorySheet.Open();
        store.LoadChirpEdits(postId, loaded =>
        {
            if (chirpEditHistoryPostId != postId)
            {
                return;
            }

            chirpEditVersions = loaded;
            chirpEditVersionsLoading = false;
        });
    }

    private float DrawLastEditedLine(ImDrawListPtr drawList, PostDto post, float left, float width, float top)
    {
        if (!ChirperEdits.IsEdited(post))
        {
            return top;
        }

        var scale = UiScale.Current;
        var editedAt = post.EditedAtUnix!.Value;
        var label = lastEditedLabel.IsCurrent(editedAt)
            ? lastEditedLabel.Value
            : lastEditedLabel.Store(editedAt, Loc.T(L.Chirper.LastEdited, FullTimestamp(editedAt)));
        var lineTop = top + 6f * scale;
        var fitted = Typography.FitText(label, width, HeadHandleStyle);
        var size = Typography.Measure(fitted, HeadHandleStyle);
        var min = new Vector2(left, lineTop);
        var max = new Vector2(left + size.X, lineTop + Typography.LineHeight(HeadHandleStyle));
        var hovered = UiInteract.Hover(min, max);
        Typography.Draw(drawList, min, fitted, hovered ? ChirperInk.TitleInk : ChirperInk.AccentLink, HeadHandleStyle);
        if (hovered)
        {
            drawList.AddLine(new Vector2(min.X, min.Y + size.Y), new Vector2(min.X + size.X, min.Y + size.Y),
                ImGui.GetColorU32(ChirperInk.TitleInk), 1f);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (UiInteract.Click(min, max, hovered))
        {
            OpenChirpEditHistory(post.Id);
        }

        return max.Y;
    }

    private void DrawChirpEditHistory(Rect screen)
    {
        if (!chirpEditHistorySheet.CapturesPointer)
        {
            return;
        }

        ImGui.SetCursorScreenPos(screen.Min);
        using (ImRaii.Child("##chirperEditHistory", screen.Size, false, EditHistoryFlags))
        {
            var frame = chirpEditHistorySheet.Begin(ImGui.GetWindowDrawList(), screen, theme,
                SheetDetents.Standard(screen.Height), SheetMetrics.VeilFor(true));
            if (!frame.Visible)
            {
                return;
            }

            DrawChirpEditHistoryContent(in frame);
            chirpEditHistorySheet.End(in frame);
        }
    }

    private void DrawChirpEditHistoryContent(in SheetFrame frame)
    {
        var scale = UiScale.Current;
        var content = frame.Content;
        var ink = Palette.WithAlpha(frame.Ink, frame.Ink.W * frame.Opacity);
        var muted = Palette.WithAlpha(ink, ink.W * EditHistoryMutedAlpha);
        var headerHeight = EditHistoryHeaderHeight * scale;
        Typography.DrawCentered(frame.DrawList, new Vector2(content.Center.X, content.Min.Y + headerHeight * 0.5f),
            Loc.T(L.Chirper.EditHistory), ink, EditHistoryTitleStyle);
        var rows = new Rect(new Vector2(content.Min.X, content.Min.Y + headerHeight),
            new Vector2(content.Max.X, content.Max.Y - Metrics.Size.HomeIndicatorInset * scale));
        var versions = chirpEditVersions;
        if (versions is null)
        {
            Typography.DrawCentered(frame.DrawList, new Vector2(rows.Center.X, rows.Min.Y + 40f * scale),
                Loc.T(chirpEditVersionsLoading ? L.Common.Loading : L.Account.CannotReach), muted, TextStyles.Subheadline);
            return;
        }

        if (chirpEditTimeLabels.Length != versions.Length)
        {
            chirpEditTimeLabels = new CachedText[versions.Length];
        }

        using (AppSurface.BeginEdgeToEdge(rows))
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var width = ScrollLayout.StableContentWidth();
            var padX = Metrics.Space.Lg * scale;
            var left = origin.X + padX;
            var textWidth = MathF.Max(1f, width - padX * 2f);
            var cursorY = origin.Y;
            for (var index = 0; index < versions.Length; index++)
            {
                var version = versions[index];
                ref var timeLabel = ref chirpEditTimeLabels[index];
                var time = timeLabel.IsCurrent(version.PostedAtUnix)
                    ? timeLabel.Value
                    : timeLabel.Store(version.PostedAtUnix, FullTimestamp(version.PostedAtUnix));
                Typography.Draw(drawList, new Vector2(left, cursorY), Typography.FitText(time, textWidth, EditHistoryTimeStyle),
                    muted, EditHistoryTimeStyle);
                cursorY += Typography.LineHeight(EditHistoryTimeStyle) + Metrics.Space.Xxs * scale;
                cursorY += Typography.DrawWrappedLeft(new Vector2(left, cursorY), version.Text, ink, EditHistoryTextStyle,
                    textWidth);
                cursorY += Metrics.Space.Md * scale;
                if (index < versions.Length - 1)
                {
                    drawList.AddLine(new Vector2(left, cursorY), new Vector2(left + textWidth, cursorY),
                        ImGui.GetColorU32(Palette.WithAlpha(ink, ink.W * EditHistoryHairlineAlpha)), 1f);
                    cursorY += Metrics.Space.Md * scale;
                }
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, MathF.Max(1f, cursorY - origin.Y)));
        }
    }
}
