using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const float MutedListLoadingTop = 60f;
    private const float MutedHintPadY = 10f;

    private readonly UserRowSubtitles mutedSubtitles = new();

    private void ToggleMute(string userId, bool muted, string displayName, string handle) =>
        SocialMute.Apply(store, toast, userId, !muted, displayName, handle);

    private void OpenMutedAccounts()
    {
        actions.Reset();
        store.RefreshMutedAccounts();
        router.Push(ChirperRoute.MutedAccounts);
    }

    private void DrawMutedAccounts(Rect area)
    {
        DrawScreenHeader(area, Loc.T(L.Social.MutedAccounts));
        var scale = UiScale.Current;
        var listRect = new Rect(new Vector2(area.Min.X, area.Min.Y + AppHeader.Height * scale), area.Max);
        var users = store.MutedAccounts;
        if (users.Length == 0)
        {
            DrawMutedAccountsEmpty(listRect);
            return;
        }

        var subtitles = mutedSubtitles.For(users, gameData);
        using (AppSurface.BeginEdgeToEdge(listRect))
        {
            DrawMutedHint();
            for (var index = 0; index < users.Length; index++)
            {
                if (SocialUserRow.SkipHidden(UserRowStyle))
                {
                    continue;
                }

                DrawMutedRow(users[index], subtitles[index]);
            }

            if (store.MutedAccountsLoadingMore)
            {
                InfiniteScroll.DrawLoadingRow(listRect.Center.X, ChirperInk.MutedInk);
            }
            else if (store.HasMoreMutedAccounts && InfiniteScroll.ReachedBottom())
            {
                store.LoadMoreMutedAccounts();
            }

            ImGui.Dummy(new Vector2(0f, 12f * scale));
        }
    }

    private void DrawMutedAccountsEmpty(Rect body)
    {
        if (store.MutedAccountsFailed)
        {
            if (EmptyState.Draw(body, ui, FontAwesomeIcon.VolumeMute, Loc.T(L.Common.LoadFailed),
                    Loc.T(L.Common.LoadFailedHint), Loc.T(L.Common.Retry)))
            {
                store.RefreshMutedAccounts();
            }

            return;
        }

        if (!store.MutedAccountsLoaded)
        {
            Typography.DrawCentered(new Vector2(body.Center.X, body.Min.Y + MutedListLoadingTop * UiScale.Current),
                Loc.T(L.Common.Loading), ChirperInk.MutedInk, MetaStyle);
            return;
        }

        EmptyState.Draw(body, ui, FontAwesomeIcon.VolumeMute, Loc.T(L.Social.MutedEmpty), Loc.T(L.Social.MutedHint));
    }

    private static void DrawMutedHint()
    {
        var scale = UiScale.Current;
        var origin = ImGui.GetCursorScreenPos();
        var width = ScrollLayout.StableContentWidth();
        var padX = CellPadX * scale;
        var height = Typography.DrawWrappedLeft(new Vector2(origin.X + padX, origin.Y + MutedHintPadY * scale),
            Loc.T(L.Social.MutedHint), ChirperInk.MutedInk, MetaStyle, width - padX * 2f);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height + MutedHintPadY * 2f * scale));
    }

    private void DrawMutedRow(UserDto user, string subtitle)
    {
        var scale = UiScale.Current;
        var muted = store.IsMuted(user);
        var label = SocialMute.PillLabel(muted);
        var row = SocialUserRow.Draw("chirper.muted.", user, subtitle, Button.WidthFor(label, ButtonSize.Small) / scale,
            UserRowStyle, ChirperInk.Shared, theme, images, lodestone);
        var pillHeight = Button.SmallHeight * scale;
        var pill = new Rect(new Vector2(row.Trailing.Min.X, row.Trailing.Center.Y - pillHeight * 0.5f),
            new Vector2(row.Trailing.Max.X, row.Trailing.Center.Y + pillHeight * 0.5f));
        bool clicked;
        using (UiScope.PushId(user.Id))
        {
            clicked = Button.Draw(ImGui.GetWindowDrawList(), pill, label, ui.Ink,
                muted ? ButtonStyle.Gray : ButtonStyle.Prominent);
        }

        if (clicked)
        {
            ToggleMute(user.Id, muted, user.DisplayName, user.Handle);
        }

        if (row.Tapped)
        {
            OpenProfile(user.Id);
        }
    }
}
