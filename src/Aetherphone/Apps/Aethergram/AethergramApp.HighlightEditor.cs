using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float EditorCoverRadius = 40f;
    private const float EditorFieldWidth = 230f;
    private const float EditorGap = 12f;
    private const float EditorTileGap = 3f;
    private const float EditorTileAspect = 1.6f;
    private const float EditorCheckRadius = 10f;
    private const float EditorCheckInset = 8f;
    private const float EditorCheckGlyph = 13f;
    private const float EditorCoverGlyph = 14f;
    private const float EditorEmptyTop = 40f;
    private const int EditorColumns = 3;
    private const int EditorSaved = 1;
    private const int EditorClosed = 2;

    private readonly HashSet<string> editorSelected = new(StringComparer.Ordinal);
    private string[] editorOriginal = Array.Empty<string>();
    private string? editorHighlightId;
    private string editorTitle = string.Empty;
    private string editorOriginalTitle = string.Empty;
    private string? editorCoverId;
    private string? editorOriginalCover;
    private string? editorPreselect;
    private bool editorPickingCover;
    private int editorSyncedVersion;
    private volatile int editorOutcome;
    private int editorCountFor = -1;
    private string editorCountLabel = string.Empty;

    private void StartNewHighlight(string? preselectStoryId) => OpenHighlightEditor(null, preselectStoryId);

    private void OpenHighlightEditor(StoryHighlightDto? highlight, string? preselectStoryId = null)
    {
        editorHighlightId = highlight?.Id;
        editorTitle = highlight?.Title ?? string.Empty;
        editorOriginalTitle = editorTitle;
        editorCoverId = highlight?.CoverStoryId;
        editorOriginalCover = editorCoverId;
        editorPreselect = preselectStoryId;
        editorPickingCover = false;
        editorSelected.Clear();
        editorOriginal = Array.Empty<string>();
        editorOutcome = 0;
        editorCountFor = -1;
        editorSyncedVersion = highlights.EditorVersion;
        highlights.LoadEditor(highlight?.Id);
        router.Push(AethergramRoute.HighlightEditor);
    }

    private void DrawHighlightEditor(Rect area, bool onTop)
    {
        if (onTop && editorOutcome != 0)
        {
            editorOutcome = 0;
            router.Pop();
            return;
        }

        var isNew = editorHighlightId is null;
        DrawScreenHeader(area, Loc.T(isNew ? L.Story.NewHighlight : L.Story.EditHighlight), isNew ? 0 : 1);
        if (editorHighlightId is { } highlightId && DrawHeaderIcon(ImGui.GetWindowDrawList(),
                SocialChrome.HeaderSlot(area, 0), PhoneIcons.Trash, Loc.T(L.Story.DeleteHighlight)))
        {
            AskDeleteHighlight(highlightId);
        }

        SyncEditor();
        var scale = UiScale.Current;
        var margin = CellPadX * scale;
        var saveRect = new Rect(new Vector2(area.Min.X + margin, area.Max.Y - margin - PillHeight * scale),
            new Vector2(area.Max.X - margin, area.Max.Y - margin));
        var listRect = new Rect(new Vector2(area.Min.X, area.Min.Y + AppHeader.Height * scale),
            new Vector2(area.Max.X, saveRect.Min.Y - EditorGap * scale));
        var candidates = highlights.EditorStories;
        using (AppSurface.BeginEdgeToEdge(listRect))
        {
            DrawEditorHead(candidates);
            DrawEditorGrid(candidates);
        }

        var saving = highlights.Saving;
        var label = saving ? Loc.T(L.Aethergram.Saving) : Loc.T(isNew ? L.Story.Add : L.Story.HighlightDone);
        if (DrawAccentPill(saveRect, label, editorSelected.Count > 0 && !saving && !highlights.EditorLoading))
        {
            SaveHighlight();
        }
    }

    private void SyncEditor()
    {
        if (highlights.EditorLoading || highlights.EditorVersion == editorSyncedVersion)
        {
            return;
        }

        editorSyncedVersion = highlights.EditorVersion;
        editorOriginal = highlights.EditorMembers;
        editorSelected.Clear();
        for (var index = 0; index < editorOriginal.Length; index++)
        {
            editorSelected.Add(editorOriginal[index]);
        }

        if (editorPreselect is { } preselect && IndexOfStory(highlights.EditorStories, preselect) >= 0)
        {
            editorSelected.Add(preselect);
        }
    }

    private void DrawEditorHead(StoryDto[] candidates)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var radius = EditorCoverRadius * scale;
        var center = new Vector2(origin.X + width * 0.5f, origin.Y + EditorGap * scale + radius);
        var cover = EditorCover(candidates);
        drawList.AddCircle(center, radius + 3.5f * scale, ImGui.GetColorU32(Ink.Hairline), 48, 1.3f * scale);
        AvatarView.DrawRemote(drawList, center, radius, theme, editorTitle, string.Empty, cover?.MediaUrl, images,
            lodestone, 0.8f, 48);
        var cursorY = center.Y + radius + 10f * scale;
        var lineHeight = Typography.LineHeight(TextStyles.SubheadlineEmphasized);
        if (editorSelected.Count > 0)
        {
            var link = Loc.T(editorPickingCover ? L.Story.PickCoverHint : L.Story.EditCover);
            var linkSize = Typography.Measure(link, TextStyles.SubheadlineEmphasized);
            var linkMin = new Vector2(center.X - linkSize.X * 0.5f, cursorY);
            var linkMax = linkMin + linkSize;
            Typography.Draw(drawList, linkMin, link, editorPickingCover ? Ink.MutedInk : Ink.AccentLink,
                TextStyles.SubheadlineEmphasized);
            if (UiInteract.HoverClick(linkMin, linkMax))
            {
                editorPickingCover = !editorPickingCover;
            }
        }

        cursorY += lineHeight + EditorGap * scale;
        var fieldWidth = MathF.Min(EditorFieldWidth * scale, width - CellPadX * 2f * scale);
        var field = new Rect(new Vector2(center.X - fieldWidth * 0.5f, cursorY),
            new Vector2(center.X + fieldWidth * 0.5f, cursorY + GlassField.HeightUnits * scale));
        Squircle.Fill(drawList, field.Min, field.Max, GlassField.Radius(field), ImGui.GetColorU32(Ink.FieldFill));
        GlassField.Text(field, "##aethergramHighlightTitle", Loc.T(L.Story.HighlightTitleHint), ref editorTitle,
            Ink.TitleInk, Ink.MutedInk, 16f * scale, HighlightStore.TitleMaxLength);
        cursorY = field.Max.Y + EditorGap * scale;
        var hint = editorSelected.Count > 0 ? EditorCountLabel() : Loc.T(L.Story.PickStoriesHint);
        var hintBottom = Typography.DrawWrappedCentered(drawList, hint, TextStyles.Footnote, Ink.MutedInk,
            new Vector2(center.X, cursorY), width - CellPadX * 2f * scale);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, hintBottom + EditorGap * scale - origin.Y));
    }

    private string EditorCountLabel()
    {
        if (editorCountFor != editorSelected.Count)
        {
            editorCountFor = editorSelected.Count;
            editorCountLabel = Loc.Plural(L.Story.StoriesSelected, editorCountFor);
        }

        return editorCountLabel;
    }

    private void DrawEditorGrid(StoryDto[] candidates)
    {
        var scale = UiScale.Current;
        var width = ScrollLayout.StableContentWidth();
        if (candidates.Length == 0)
        {
            var message = highlights.EditorLoading ? Loc.T(L.Common.Loading) : Loc.T(L.Story.NoStoriesToHighlight);
            Typography.DrawWrappedCentered(ImGui.GetWindowDrawList(), message, TextStyles.Subheadline, Ink.MutedInk,
                new Vector2(ImGui.GetCursorScreenPos().X + width * 0.5f, ImGui.GetCursorScreenPos().Y + EditorEmptyTop * scale),
                width - CellPadX * 2f * scale);
            return;
        }

        var gap = EditorTileGap * scale;
        var cellWidth = (width - gap * (EditorColumns - 1)) / EditorColumns;
        var cellHeight = cellWidth * EditorTileAspect;
        var coverId = EditorCover(candidates)?.Id;
        using (UiScope.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                ImGui.Dummy(new Vector2(cellWidth, cellHeight));
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                if (index % EditorColumns != EditorColumns - 1 && index < candidates.Length - 1)
                {
                    ImGui.SameLine();
                }

                DrawEditorTile(candidates[index], min, max, candidates[index].Id == coverId);
            }
        }

        ImGui.Dummy(new Vector2(0f, EditorGap * scale));
    }

    private void DrawEditorTile(StoryDto story, Vector2 min, Vector2 max, bool isCover)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var texture = images.Sized(story.MediaUrl, MathF.Max(max.X - min.X, max.Y - min.Y) * GridThumbnailOversample);
        if (texture is null)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Ink.ThumbFill));
        }
        else
        {
            var (uv0, uv1) = ImageFit.Cover(texture.Size.X, texture.Size.Y, max.X - min.X, max.Y - min.Y);
            drawList.AddImage(texture.Handle, min, max, uv0, uv1);
        }

        var selected = editorSelected.Contains(story.Id);
        if (editorPickingCover && !selected)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Ink.Scrim));
        }

        var checkRadius = EditorCheckRadius * scale;
        var checkCenter = new Vector2(max.X - (EditorCheckInset * scale + checkRadius),
            min.Y + EditorCheckInset * scale + checkRadius);
        if (selected)
        {
            drawList.AddCircleFilled(checkCenter, checkRadius, ImGui.GetColorU32(Ink.Accent), 24);
            PhoneIcon.Draw(drawList, checkCenter, PhoneIcons.Check, Ink.White, EditorCheckGlyph * scale);
        }
        else
        {
            drawList.AddCircleFilled(checkCenter, checkRadius, ImGui.GetColorU32(Ink.Scrim), 24);
            drawList.AddCircle(checkCenter, checkRadius, ImGui.GetColorU32(Ink.White), 24, 1.5f * scale);
        }

        if (isCover && selected)
        {
            var badgeCenter = new Vector2(min.X + (EditorCheckInset * scale + checkRadius), checkCenter.Y);
            drawList.AddCircleFilled(badgeCenter, checkRadius, ImGui.GetColorU32(Ink.Scrim), 24);
            PhoneIcon.Draw(drawList, badgeCenter, PhoneIcons.StarFilled, Ink.White, EditorCoverGlyph * scale);
        }

        var hovered = UiInteract.Hover(min, max);
        if (hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Ink.HoverTint));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (!UiInteract.Click(min, max, hovered))
        {
            return;
        }

        if (editorPickingCover)
        {
            if (selected)
            {
                editorCoverId = story.Id;
                editorPickingCover = false;
            }

            return;
        }

        if (selected)
        {
            editorSelected.Remove(story.Id);
            return;
        }

        if (editorSelected.Count >= HighlightStore.MaxItems)
        {
            pendingHighlightNotice = HighlightNotice.Full;
            return;
        }

        editorSelected.Add(story.Id);
    }

    private StoryDto? EditorCover(StoryDto[] candidates)
    {
        StoryDto? newest = null;
        for (var index = 0; index < candidates.Length; index++)
        {
            var story = candidates[index];
            if (!editorSelected.Contains(story.Id))
            {
                continue;
            }

            if (story.Id == editorCoverId)
            {
                return story;
            }

            if (newest is null || story.CreatedAtUnix > newest.CreatedAtUnix)
            {
                newest = story;
            }
        }

        return newest;
    }

    private void SaveHighlight()
    {
        var title = editorTitle.Trim();
        if (title.Length == 0)
        {
            title = Loc.T(L.Story.HighlightDefaultTitle);
        }

        var selected = new string[editorSelected.Count];
        editorSelected.CopyTo(selected);
        var cover = editorCoverId is not null && editorSelected.Contains(editorCoverId) ? editorCoverId : null;
        if (editorHighlightId is not { } highlightId)
        {
            highlights.Create(title, selected, cover, OnHighlightSaved);
            return;
        }

        var added = new List<string>(selected.Length);
        for (var index = 0; index < selected.Length; index++)
        {
            if (Array.IndexOf(editorOriginal, selected[index]) < 0)
            {
                added.Add(selected[index]);
            }
        }

        var removed = new List<string>(editorOriginal.Length);
        for (var index = 0; index < editorOriginal.Length; index++)
        {
            if (!editorSelected.Contains(editorOriginal[index]))
            {
                removed.Add(editorOriginal[index]);
            }
        }

        var titleChange = string.Equals(title, editorOriginalTitle, StringComparison.Ordinal) ? null : title;
        var coverChange = string.Equals(cover, editorOriginalCover, StringComparison.Ordinal) ? null : cover;
        highlights.Save(new HighlightEdit(highlightId, titleChange, coverChange, added.ToArray(), removed.ToArray()),
            OnHighlightSaved);
    }

    private void OnHighlightSaved(bool succeeded)
    {
        pendingHighlightNotice = succeeded ? HighlightNotice.Saved : HighlightNotice.Failed;
        if (succeeded)
        {
            editorOutcome = EditorSaved;
        }
    }

    private static int IndexOfStory(StoryDto[] stories, string storyId)
    {
        for (var index = 0; index < stories.Length; index++)
        {
            if (stories[index].Id == storyId)
            {
                return index;
            }
        }

        return -1;
    }
}
