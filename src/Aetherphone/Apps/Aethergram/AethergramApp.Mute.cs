using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private readonly UserRowSubtitles mutedSubtitles = new();

    private void ToggleMute(string userId, bool muted, string displayName, string handle) =>
        SocialMute.Apply(store, toast, userId, !muted, displayName, handle);

    private void OpenMutedAccounts()
    {
        store.RefreshMutedAccounts();
        router.Push(AethergramRoute.MutedAccounts);
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
            DrawSettingsHint(Loc.T(L.Social.MutedHint));
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
                InfiniteScroll.DrawLoadingRow(listRect.Center.X, Ink.MutedInk);
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
            Typography.DrawCentered(new Vector2(body.Center.X, body.Min.Y + PeopleListEmptyTop * UiScale.Current),
                Loc.T(L.Common.Loading), Ink.MutedInk);
            return;
        }

        EmptyState.Draw(body, ui, FontAwesomeIcon.VolumeMute, Loc.T(L.Social.MutedEmpty), Loc.T(L.Social.MutedHint));
    }

    private void DrawMutedRow(UserDto user, string subtitle)
    {
        var muted = store.IsMuted(user);
        var label = SocialMute.PillLabel(muted);
        var pillWidth = MathF.Max(FollowPillWidth, Button.WidthFor(label, ButtonSize.Regular) / UiScale.Current);
        var row = DrawUserRow(user, pillWidth, subtitle);
        bool clicked;
        using (UiScope.PushId(user.Id))
        {
            clicked = muted ? DrawGrayPill(row.Trailing, label) : DrawAccentPill(row.Trailing, label);
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
