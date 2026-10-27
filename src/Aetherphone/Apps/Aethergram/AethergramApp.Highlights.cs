using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Core.Theme;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private enum HighlightNotice
    {
        None,
        Added,
        Saved,
        Failed,
        Full,
        Limit,
    }

    private const float PickerTitleHeight = 40f;
    private const float PickerBottomPad = 18f;
    private const int HighlightMenuItemCount = 2;

    private readonly HighlightStore highlights;
    private readonly CloseFriendsStore closeFriends;
    private readonly HighlightRow profileHighlightRow = new();
    private readonly HighlightRow pickerHighlightRow = new();
    private readonly Sheet highlightPicker = new();
    private readonly ActionSheet highlightMenu = new();
    private readonly ActionSheet.Item[] highlightMenuItems = new ActionSheet.Item[HighlightMenuItemCount];
    private StoryHighlightDto? highlightMenuTarget;
    private StoryDto? highlightPickerStory;
    private volatile HighlightNotice pendingHighlightNotice;
    private string highlightNoticeTitle = string.Empty;

    private void GateHighlightSheets()
    {
        if (highlightPicker.IsOpen)
        {
            UiInteract.BlockThisFrame();
        }

        highlightMenu.Gate();
    }

    private bool HighlightPickerCaptures => highlightPicker.CapturesPointer;

    private void ConsumeOpenedHighlight()
    {
        if (highlights.TryTakeOpened(out var highlight, out var items))
        {
            stories.OpenDetached(items, highlight.Title, highlight.CoverUrl);
        }
    }

    private void DrawStoryViewerExtras(Rect screen)
    {
        DrawHighlightPicker(screen);
        DrainHighlightNotice();
        toast.Draw(screen, ScreenToastStyle.From(ui));
    }

    private void DrawProfileHighlights(UserDto user)
    {
        var items = highlights.For(user.Id);
        if (items.Length == 0 && !user.IsMe)
        {
            return;
        }

        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var height = HighlightRow.Height * scale;
        ImGui.Dummy(new Vector2(width, height));
        var row = new Rect(origin, new Vector2(origin.X + width, origin.Y + height));
        var result = profileHighlightRow.Draw(ImGui.GetWindowDrawList(), row, items, user.IsMe,
            Loc.T(L.Story.HighlightNew), !highlights.Opening, false, theme, Ink, images, lodestone);
        if (result.NewTapped)
        {
            if (items.Length >= HighlightStore.MaxHighlights)
            {
                pendingHighlightNotice = HighlightNotice.Limit;
                return;
            }

            StartNewHighlight(null);
            return;
        }

        if (result.Opened >= 0)
        {
            highlights.Open(items[result.Opened]);
            return;
        }

        if (result.Menu >= 0 && user.IsMe)
        {
            OpenHighlightMenu(items[result.Menu]);
        }
    }

    private void OnHighlightStory(StoryDto story)
    {
        highlightPickerStory = story;
        highlights.EnsureMine();
        highlightPicker.Open();
    }

    private void DrawHighlightPicker(Rect screen)
    {
        if (!highlightPicker.CapturesPointer)
        {
            return;
        }

        var scale = UiScale.Current;
        var height = (SheetMetrics.GrabberZone + PickerTitleHeight + HighlightRow.Height + PickerBottomPad
                      + Metrics.Size.HomeIndicatorInset) * scale;
        var frame = highlightPicker.Begin(ImGui.GetForegroundDrawList(), screen, theme, SheetDetents.Fitted(height),
            SheetMetrics.AppVeil);
        if (!frame.Visible)
        {
            return;
        }

        var content = frame.Content;
        Typography.DrawCentered(frame.DrawList,
            new Vector2(content.Center.X, content.Min.Y + PickerTitleHeight * 0.5f * scale),
            Loc.T(L.Story.AddToHighlight), Palette.WithAlpha(frame.Ink, frame.Opacity), TextStyles.Headline);
        var rowTop = content.Min.Y + PickerTitleHeight * scale;
        var row = new Rect(new Vector2(content.Min.X, rowTop),
            new Vector2(content.Max.X, rowTop + HighlightRow.Height * scale));
        var mine = highlights.Mine;
        var result = HighlightRowResult.None;
        if (highlights.MineLoaded)
        {
            result = pickerHighlightRow.Draw(frame.DrawList, row, mine, true, Loc.T(L.Story.HighlightNew),
                frame.Interactive && !highlights.Saving, true, theme, Ink, images, lodestone);
        }
        else
        {
            Typography.DrawCentered(frame.DrawList, row.Center, Loc.T(L.Common.Loading),
                Palette.WithAlpha(Ink.MutedInk, frame.Opacity), TextStyles.Subheadline);
        }

        highlightPicker.End(in frame);
        if (highlightPickerStory is not { } story)
        {
            return;
        }

        if (result.NewTapped)
        {
            if (mine.Length >= HighlightStore.MaxHighlights)
            {
                pendingHighlightNotice = HighlightNotice.Limit;
                return;
            }

            highlightPicker.Close();
            stories.Close();
            StartNewHighlight(story.Id);
            return;
        }

        if (result.Opened >= 0)
        {
            AddToHighlight(mine[result.Opened], story);
        }
    }

    private void AddToHighlight(StoryHighlightDto highlight, StoryDto story)
    {
        if (highlight.ItemCount >= HighlightStore.MaxItems)
        {
            pendingHighlightNotice = HighlightNotice.Full;
            return;
        }

        highlightPicker.Close();
        highlightNoticeTitle = highlight.Title;
        highlights.AddStory(highlight.Id, story.Id,
            succeeded => pendingHighlightNotice = succeeded ? HighlightNotice.Added : HighlightNotice.Failed);
    }

    private void OpenHighlightMenu(StoryHighlightDto highlight)
    {
        highlightMenuTarget = highlight;
        highlightMenuItems[0] = new ActionSheet.Item(Loc.T(L.Story.EditHighlight), IconGlyph.Of(FontAwesomeIcon.Pen));
        highlightMenuItems[1] = new ActionSheet.Item(Loc.T(L.Story.DeleteHighlight),
            IconGlyph.Of(FontAwesomeIcon.Trash), true);
        highlightMenu.Open();
    }

    private void DrawHighlightMenu(Rect screen)
    {
        DrainHighlightNotice();
        if (!highlightMenu.CapturesPointer)
        {
            return;
        }

        var target = highlightMenuTarget;
        var picked = highlightMenu.Draw(screen, ActionSheetStyle.From(ui), highlightMenuItems, Loc.T(L.Common.Cancel),
            false, target?.Title ?? string.Empty);
        if (picked < 0 || target is null)
        {
            return;
        }

        if (picked == 0)
        {
            OpenHighlightEditor(target);
            return;
        }

        AskDeleteHighlight(target.Id);
    }

    private void AskDeleteHighlight(string highlightId)
    {
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(L.Story.DeleteHighlightMessage),
            ConfirmLabel = Loc.T(L.Common.Delete),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            BusyLabel = Loc.T(L.Aethergram.Saving),
            FailedMessage = Loc.T(L.Story.DeleteHighlightFailed),
            ConfirmAsync = done => highlights.Delete(highlightId, succeeded =>
            {
                if (succeeded)
                {
                    editorOutcome = EditorClosed;
                }

                done(succeeded);
            }),
        });
    }

    private void DrainHighlightNotice()
    {
        var notice = pendingHighlightNotice;
        if (notice == HighlightNotice.None)
        {
            return;
        }

        pendingHighlightNotice = HighlightNotice.None;
        toast.Show(notice switch
        {
            HighlightNotice.Added => Loc.T(L.Story.AddedToHighlight, highlightNoticeTitle),
            HighlightNotice.Saved => Loc.T(L.Story.HighlightSaved),
            HighlightNotice.Full => Loc.T(L.Story.HighlightFull, HighlightStore.MaxItems),
            HighlightNotice.Limit => Loc.T(L.Story.HighlightLimit, HighlightStore.MaxHighlights),
            _ => Loc.T(L.Story.HighlightFailed),
        });
    }
}
