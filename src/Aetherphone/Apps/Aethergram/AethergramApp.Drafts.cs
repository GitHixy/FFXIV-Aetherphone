using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Confirm;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Media;
using Aetherphone.Core.Onboarding;
using Aetherphone.Core.Social;
using Aetherphone.Core.Wallpapers;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const float DraftsEntryHeight = 40f;
    private const float DraftsChevronSize = 16f;

    private static readonly TextStyle DraftsEntryStyle = TextStyles.SubheadlineEmphasized;

    private readonly DraftExitSheet draftExitSheet = new();
    private readonly DraftRowCache draftRows = new();
    private bool composeDraftsOpen;
    private string? composeDraftId;
    private int draftsCountShown = -1;
    private string draftsCountText = string.Empty;
    private Action? closeCompose;
    private Action? closeComposeDrafts;

    private Action CloseCompose => closeCompose ??= RequestComposeClose;

    private Action CloseComposeDrafts => closeComposeDrafts ??= () => composeDraftsOpen = false;

    private List<ComposerDraft>? ComposeDrafts =>
        ComposerDrafts.For(configuration.AethergramDrafts, store.AccountId);

    private bool ComposeKeepsDrafts => !composeAvatarMode && !composeStoryMode;

    private void ResetComposeDraft()
    {
        composeDraftId = null;
        composeDraftsOpen = false;
        draftExitSheet.Close();
    }

    private void RequestComposeClose()
    {
        if (!ComposeKeepsDrafts || store.Posting
            || (!composeSession.HasSelection && string.IsNullOrWhiteSpace(caption)))
        {
            CloseComposeNow();
            return;
        }

        draftExitSheet.Open();
    }

    private void DrawDraftExitSheet(Rect screen)
    {
        switch (draftExitSheet.Draw(screen, ActionSheetStyle.From(ui)))
        {
            case DraftExitChoice.Save:
                SaveComposeDraft();
                CloseComposeNow();
                break;
            case DraftExitChoice.Discard:
                CloseComposeNow();
                break;
        }
    }

    private void CloseComposeNow()
    {
        caption = string.Empty;
        composeSensitive = false;
        composeTags.Clear();
        ResetComposeDraft();
        back();
    }

    private void ForgetComposeDraft()
    {
        if (ComposerDrafts.Remove(configuration.AethergramDrafts, store.AccountId, composeDraftId))
        {
            configuration.Save();
        }

        ResetComposeDraft();
    }

    private void SaveComposeDraft()
    {
        if (store.AccountId is not { } accountId)
        {
            return;
        }

        var saved = new ComposerDraft
        {
            Id = composeDraftId ?? ComposerDrafts.NewId(),
            Text = caption,
            Sensitive = composeSensitive,
            Aspect = (int)composeSession.Aspect,
        };
        var paths = composeSession.SelectedArray();
        saved.PhotoPaths.AddRange(paths);
        SaveDraftFrames(saved, paths);
        for (var index = 0; index < composeCollaborators.Count; index++)
        {
            var collaborator = composeCollaborators[index];
            saved.Collaborators.Add(new ComposerDraftPerson
            {
                UserId = collaborator.UserId,
                Handle = collaborator.Handle,
                DisplayName = collaborator.DisplayName,
                AvatarUrl = collaborator.AvatarUrl ?? string.Empty,
            });
        }

        for (var index = 0; index < composeTags.Count; index++)
        {
            var tag = composeTags[index];
            saved.PhotoTags.Add(new ComposerDraftTag
            {
                UserId = tag.UserId,
                Handle = tag.Handle,
                DisplayName = tag.DisplayName,
                PhotoIndex = tag.PhotoIndex,
                X = tag.X,
                Y = tag.Y,
            });
        }

        ComposerDrafts.Put(configuration.AethergramDrafts, accountId, saved);
        configuration.Save();
        toast.Show(Loc.T(L.Social.DraftSaved));
    }

    private void RestoreComposeDraft(ComposerDraft saved)
    {
        PrepareCompose(false, false);
        composeDraftId = saved.Id;
        caption = saved.Text;
        composeSession.Aspect = DraftAspect(saved.Aspect);
        var photoSlots = new int[saved.PhotoPaths.Count];
        var missing = 0;
        for (var index = 0; index < saved.PhotoPaths.Count; index++)
        {
            var path = saved.PhotoPaths[index];
            if (!File.Exists(path))
            {
                photoSlots[index] = -1;
                missing++;
                continue;
            }

            var before = composeSession.SelectedCount;
            composeSession.TakeImported(path);
            photoSlots[index] = composeSession.SelectedCount > before ? before : -1;
            RestoreDraftPhoto(saved, index, photoSlots[index]);
        }

        for (var index = 0; index < saved.PhotoTags.Count; index++)
        {
            var tag = saved.PhotoTags[index];
            if (tag.PhotoIndex < 0 || tag.PhotoIndex >= photoSlots.Length || photoSlots[tag.PhotoIndex] < 0)
            {
                continue;
            }

            composeTags.Add(new PhotoTagDto(string.Empty, tag.UserId, tag.Handle, tag.DisplayName,
                photoSlots[tag.PhotoIndex], tag.X, tag.Y, 1));
        }

        composeSensitive = saved.Sensitive;
        for (var index = 0; index < saved.Collaborators.Count; index++)
        {
            var person = saved.Collaborators[index];
            AddComposeCollaborator(new MentionSuggestDto(person.UserId, person.Handle, person.DisplayName,
                person.AvatarUrl.Length > 0 ? person.AvatarUrl : null));
        }

        if (missing > 0)
        {
            toast.Show(Loc.Plural(L.Social.DraftPhotosMissing, missing));
        }
    }

    private void SaveDraftFrames(ComposerDraft saved, string[] paths)
    {
        var crops = composeSession.CropsArray();
        var edits = composeSession.EditsArray();
        for (var index = 0; index < paths.Length; index++)
        {
            saved.AltTexts.Add(PostAltTexts.For(composeAltTexts, paths[index]));
            var crop = index < crops.Length ? crops[index] : WallpaperCrop.Cover;
            var edit = index < edits.Length ? edits[index] : PhotoEdit.None;
            saved.Frames.Add(new ComposerDraftFrame
            {
                Zoom = crop.Zoom,
                CenterX = crop.CenterX,
                CenterY = crop.CenterY,
                QuarterTurns = edit.QuarterTurns,
                Mirrored = edit.Mirrored,
                Straighten = edit.Straighten,
                Brightness = edit.Brightness,
                Contrast = edit.Contrast,
                Saturation = edit.Saturation,
                Warmth = edit.Warmth,
                Vignette = edit.Vignette,
                Look = (int)edit.Look,
                LookStrength = edit.LookStrength,
            });
        }
    }

    private void RestoreDraftPhoto(ComposerDraft saved, int savedIndex, int slot)
    {
        if (slot < 0)
        {
            return;
        }

        var path = saved.PhotoPaths[savedIndex];
        if (savedIndex < saved.AltTexts.Count)
        {
            PostAltTexts.Set(composeAltTexts, path, saved.AltTexts[savedIndex]);
        }

        if (savedIndex >= saved.Frames.Count || composeSession.GifSelected)
        {
            return;
        }

        var frame = saved.Frames[savedIndex];
        var look = frame.Look is >= (int)PhotoLook.None and <= (int)PhotoLook.Noir
            ? (PhotoLook)frame.Look
            : PhotoLook.None;
        var edit = new PhotoEdit(frame.QuarterTurns, frame.Mirrored, frame.Straighten, frame.Brightness, frame.Contrast,
            frame.Saturation, frame.Warmth, frame.Vignette, look, frame.LookStrength);
        var crop = new WallpaperCrop(Math.Clamp(frame.Zoom, 0.01f, WallpaperCrop.MaxZoom),
            Math.Clamp(frame.CenterX, 0f, 1f), Math.Clamp(frame.CenterY, 0f, 1f));
        composeSession.RestoreFraming(slot, crop, edit, PostAspects.Ratio(composeSession.Aspect));
    }

    private static PostAspect DraftAspect(int saved) =>
        saved >= 0 && saved < PostAspects.All.Length ? PostAspects.All[saved] : PostAspect.Square;

    private float DrawComposeDraftsEntry(Rect area, float top)
    {
        var count = ComposerDrafts.Count(configuration.AethergramDrafts, store.AccountId);
        if (!ComposeKeepsDrafts || count == 0)
        {
            return top;
        }

        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var row = new Rect(new Vector2(area.Min.X, top), new Vector2(area.Max.X, top + DraftsEntryHeight * scale));
        UiAnchors.Report("aethergram.compose.drafts", row);
        var hovered = UiInteract.Hover(row.Min, row.Max);
        if (hovered)
        {
            drawList.AddRectFilled(row.Min, row.Max, ImGui.GetColorU32(Ink.HoverTint));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (draftsCountShown != count)
        {
            draftsCountShown = count;
            draftsCountText = count.ToString(Loc.Culture);
        }

        var padX = CellPadX * scale;
        var chevronCenter = new Vector2(row.Max.X - padX - DraftsChevronSize * 0.5f * scale, row.Center.Y);
        PhoneIcon.Draw(drawList, chevronCenter, PhoneIcons.ChevronRight, Ink.MutedInk, DraftsChevronSize * scale);
        var countSize = Typography.Measure(draftsCountText, TextStyles.Subheadline);
        var countLeft = chevronCenter.X - DraftsChevronSize * scale - countSize.X;
        Typography.Draw(drawList, new Vector2(countLeft, row.Center.Y - countSize.Y * 0.5f), draftsCountText,
            Ink.MutedInk, TextStyles.Subheadline);
        var labelHeight = Typography.LineHeight(DraftsEntryStyle);
        Typography.Draw(drawList, new Vector2(row.Min.X + padX, row.Center.Y - labelHeight * 0.5f),
            Typography.FitText(Loc.T(L.Social.Drafts), MathF.Max(1f, countLeft - row.Min.X - padX * 2f),
                DraftsEntryStyle), Ink.TitleInk, DraftsEntryStyle);
        if (UiInteract.Click(row.Min, row.Max, hovered))
        {
            composeDraftsOpen = true;
        }

        return row.Max.Y;
    }

    private void DrawComposeDraftsPanel(Rect area)
    {
        var scale = UiScale.Current;
        SocialChrome.DrawScreenHeader(area, Loc.T(L.Social.Drafts), Ink, CloseComposeDrafts, ScreenTitleStyle);
        var body = new Rect(new Vector2(area.Min.X, area.Min.Y + AppHeader.Height * scale), area.Max);
        var drafts = ComposeDrafts;
        var rows = draftRows.Rows(drafts);
        if (rows.Length == 0)
        {
            EmptyState.Draw(body, ui, PhoneIcons.FileText, Loc.T(L.Social.DraftsEmpty), Loc.T(L.Social.DraftsEmptyHint));
            return;
        }

        var tapped = ComposerDraftList.Draw(body, ui, Ink, rows, Loc.T(L.Social.DraftDelete), true, out var deleted);
        if (deleted >= 0)
        {
            AskDeleteDraft(drafts![deleted].Id);
            return;
        }

        if (tapped >= 0)
        {
            RestoreComposeDraft(drafts![tapped]);
        }
    }

    private void AskDeleteDraft(string draftId)
    {
        confirm.Ask(new ConfirmRequest
        {
            Message = Loc.T(L.Social.DraftDeleteConfirm),
            ConfirmLabel = Loc.T(L.Common.Delete),
            CancelLabel = Loc.T(L.Common.Cancel),
            Sheet = true,
            Confirm = () => DeleteDraft(draftId),
        });
    }

    private void DeleteDraft(string draftId)
    {
        if (!ComposerDrafts.Remove(configuration.AethergramDrafts, store.AccountId, draftId))
        {
            return;
        }

        if (composeDraftId == draftId)
        {
            composeDraftId = null;
        }

        configuration.Save();
    }
}
