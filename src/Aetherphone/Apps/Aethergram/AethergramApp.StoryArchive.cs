using System.Globalization;
using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const int ArchivePostsTab = 0;
    private const int ArchiveStoriesTab = 1;
    private const int ArchiveColumns = 3;
    private const float ArchiveTileAspect = 16f / 9f;
    private const float ArchiveDateInset = 6f;
    private const float ArchiveDatePadX = 7f;
    private const float ArchiveDatePadY = 3f;

    private static readonly TextStyle ArchiveDateStyle = TextStyles.Caption2;

    private readonly StoryArchiveStore storyArchive;
    private int archiveTab = ArchiveStoriesTab;
    private Spring archiveTabSlide = new(ArchiveStoriesTab);
    private StoryDto[] archiveLabelsFor = Array.Empty<StoryDto>();
    private CultureInfo? archiveLabelsCulture;
    private string[] archiveDateLabels = Array.Empty<string>();
    private volatile string? archiveDroppedStoryId;

    private void OpenArchive()
    {
        store.RefreshArchived();
        storyArchive.Refresh();
        router.Push(AethergramRoute.Archive);
    }

    private void DrawArchive(Rect area)
    {
        var scale = UiScale.Current;
        DrawScreenHeader(area, Loc.T(L.Social.ArchiveTitle));
        var top = area.Min.Y + AppHeader.Height * scale;
        var tabRow = new Rect(new Vector2(area.Min.X, top), new Vector2(area.Max.X, top + FeedTabRowHeight * scale));
        var picked = UnderlineTabs.Draw(tabRow, Loc.T(L.PhotoTag.PostsTab), Loc.T(L.Story.ArchiveStoriesTab),
            archiveTab == ArchiveStoriesTab, ref archiveTabSlide, Ink, FeedTabsStyle);
        if (picked >= 0)
        {
            archiveTab = picked;
        }

        var body = new Rect(new Vector2(area.Min.X, tabRow.Max.Y), area.Max);
        if (archiveTab == ArchivePostsTab)
        {
            DrawPostGridBody(body, PostSource.Archive, L.Social.ArchiveEmpty, L.Social.ArchiveEmptyHint);
            return;
        }

        DrawStoryArchive(body);
    }

    private void DrawStoryArchive(Rect body)
    {
        var scale = UiScale.Current;
        var items = storyArchive.Items;
        using (AppSurface.BeginEdgeToEdge(body))
        {
            if (items.Length == 0)
            {
                var waiting = storyArchive.Loading || !storyArchive.Loaded;
                DrawEmptyState(body, Loc.T(waiting ? L.Common.Loading : L.Story.ArchiveEmpty),
                    waiting ? string.Empty : Loc.T(L.Story.ArchiveEmptyHint));
                return;
            }

            ImGui.Dummy(new Vector2(0f, 2f * scale));
            var labels = ArchiveDateLabels(items);
            var width = ScrollLayout.StableContentWidth();
            var gap = GridGap * scale;
            var cellWidth = (width - gap * (ArchiveColumns - 1)) / ArchiveColumns;
            var cellHeight = cellWidth * ArchiveTileAspect;
            var tapped = -1;
            using (UiScope.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap)))
            {
                for (var index = 0; index < items.Length; index++)
                {
                    ImGui.Dummy(new Vector2(cellWidth, cellHeight));
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    if (ImGui.IsRectVisible(min, max) && DrawArchiveTile(items[index], labels[index], min, max))
                    {
                        tapped = index;
                    }

                    if (index % ArchiveColumns != ArchiveColumns - 1)
                    {
                        ImGui.SameLine();
                    }
                }
            }

            ImGui.NewLine();
            if (storyArchive.LoadingMore)
            {
                InfiniteScroll.DrawLoadingRow(body.Center.X, Ink.MutedInk);
            }
            else if (storyArchive.HasMore && InfiniteScroll.ReachedBottom())
            {
                storyArchive.LoadMore();
            }

            ImGui.Dummy(new Vector2(0f, 24f * scale));
            if (tapped >= 0)
            {
                OpenArchivedStory(items, tapped);
            }
        }
    }

    private bool DrawArchiveTile(StoryDto story, string dateLabel, Vector2 min, Vector2 max)
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

        var inset = ArchiveDateInset * scale;
        var padX = ArchiveDatePadX * scale;
        var padY = ArchiveDatePadY * scale;
        var fitted = Typography.FitText(dateLabel, MathF.Max(1f, max.X - min.X - inset * 2f - padX * 2f),
            ArchiveDateStyle);
        var size = Typography.Measure(fitted, ArchiveDateStyle);
        var pillMin = new Vector2(min.X + inset, min.Y + inset);
        var pillMax = new Vector2(pillMin.X + size.X + padX * 2f, pillMin.Y + size.Y + padY * 2f);
        Squircle.Fill(drawList, pillMin, pillMax, (pillMax.Y - pillMin.Y) * 0.5f, ImGui.GetColorU32(Ink.Scrim));
        Typography.Draw(drawList, new Vector2(pillMin.X + padX, pillMin.Y + padY), fitted, Ink.White,
            ArchiveDateStyle);
        var hovered = UiInteract.Hover(min, max);
        if (hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Ink.HoverTint));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return UiInteract.Click(min, max, hovered);
    }

    private string[] ArchiveDateLabels(StoryDto[] items)
    {
        if (ReferenceEquals(items, archiveLabelsFor) && ReferenceEquals(Loc.Culture, archiveLabelsCulture))
        {
            return archiveDateLabels;
        }

        var labels = new string[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            labels[index] = TimeText.MonthDay(items[index].CreatedAtUnix);
        }

        archiveLabelsFor = items;
        archiveLabelsCulture = Loc.Culture;
        archiveDateLabels = labels;
        return labels;
    }

    private void OpenArchivedStory(StoryDto[] newestFirst, int tappedIndex)
    {
        var last = newestFirst.Length - 1;
        var chronological = new StoryDto[newestFirst.Length];
        for (var index = 0; index <= last; index++)
        {
            chronological[index] = newestFirst[last - index];
        }

        stories.OpenArchived(chronological, last - tappedIndex, Loc.T(L.Story.YourStory), store.Me?.AvatarUrl,
            AskDeleteArchivedStory);
    }

    private void AskDeleteArchivedStory(StoryDto story)
    {
        var live = story.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(live ? L.Story.DeleteMessage : L.Story.DeleteArchivedMessage),
            ConfirmLabel = Loc.T(L.Aethergram.DeleteConfirm),
            CancelLabel = Loc.T(L.Aethergram.DeleteCancel),
            Sheet = true,
            BusyLabel = Loc.T(L.Aethergram.Saving),
            FailedMessage = Loc.T(L.Story.DeleteFailed),
            ConfirmAsync = done => storyArchive.Delete(story.Id, succeeded =>
            {
                if (succeeded)
                {
                    archiveDroppedStoryId = story.Id;
                    if (live)
                    {
                        stories.RefreshTray();
                    }
                }

                done(succeeded);
            }),
        });
    }

    private void DrainArchivedDeletion()
    {
        var dropped = archiveDroppedStoryId;
        if (dropped is null)
        {
            return;
        }

        archiveDroppedStoryId = null;
        stories.DropDetached(dropped);
    }
}
