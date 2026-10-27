using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const int CloseFriendsSearchMaxLength = 40;
    private const double CloseFriendsSearchDelaySeconds = 0.35d;
    private const float StoryShareGap = 8f;

    private string closeFriendsDraft = string.Empty;
    private string closeFriendsTyped = string.Empty;
    private double closeFriendsTypedAt;

    private void OpenCloseFriends()
    {
        closeFriendsDraft = string.Empty;
        closeFriendsTyped = string.Empty;
        closeFriends.Open();
        router.Push(AethergramRoute.CloseFriends);
    }

    private void DrawCloseFriends(Rect area)
    {
        DrawScreenHeader(area, Loc.T(L.Story.CloseFriends));
        var scale = UiScale.Current;
        var top = area.Min.Y + AppHeader.Height * scale;
        var searchRect = new Rect(new Vector2(area.Min.X + CellPadX * scale, top),
            new Vector2(area.Max.X - CellPadX * scale, top + InboxSearchHeight * scale));
        SearchField.Draw(searchRect, "##aethergramCloseFriendsSearch", Loc.T(L.Story.CloseFriendsSearch),
            ref closeFriendsDraft, AethergramInk.CurrentPalette, CloseFriendsSearchMaxLength);
        DebounceCloseFriendsSearch();
        var listRect = new Rect(new Vector2(area.Min.X, searchRect.Max.Y + 4f * scale), area.Max);
        var members = closeFriends.Members;
        var candidates = closeFriends.Candidates;
        using (AppSurface.BeginEdgeToEdge(listRect))
        {
            DrawSettingsHint(Loc.T(L.Story.CloseFriendsHint));
            if (members.Length > 0)
            {
                SocialChrome.DrawSectionLabel(Loc.T(L.Story.CloseFriendsOnList), Ink, SettingsSectionStyle);
                for (var index = 0; index < members.Length; index++)
                {
                    if (!SocialUserRow.SkipHidden(UserRowStyle))
                    {
                        DrawCloseFriendRow(members[index], true);
                    }
                }
            }

            if (closeFriends.HasMoreMembers)
            {
                DrawCloseFriendsLoadingRow(listRect, true);
                return;
            }

            SocialChrome.DrawSectionLabel(Loc.T(L.Story.CloseFriendsFollowers), Ink, SettingsSectionStyle);
            if (candidates.Length == 0)
            {
                var message = closeFriends.Loading ? L.Common.Loading
                    : closeFriends.Failed ? L.Story.CloseFriendsFailed
                    : L.Story.CloseFriendsEmpty;
                DrawSettingsHint(Loc.T(message));
                return;
            }

            for (var index = 0; index < candidates.Length; index++)
            {
                if (!SocialUserRow.SkipHidden(UserRowStyle))
                {
                    DrawCloseFriendRow(candidates[index], false);
                }
            }

            if (closeFriends.HasMoreCandidates)
            {
                DrawCloseFriendsLoadingRow(listRect, false);
            }

            ImGui.Dummy(new Vector2(0f, 12f * scale));
        }
    }

    private void DrawStoryShareButtons(Rect shareRect, bool busy)
    {
        var gap = StoryShareGap * UiScale.Current;
        var half = (shareRect.Width - gap) * 0.5f;
        var everyone = new Rect(shareRect.Min, new Vector2(shareRect.Min.X + half, shareRect.Max.Y));
        var closeOnly = new Rect(new Vector2(everyone.Max.X + gap, shareRect.Min.Y), shareRect.Max);
        if (DrawAccentPill(everyone, busy ? Loc.T(L.Aethergram.Sharing) : Loc.T(L.Story.YourStory), !busy))
        {
            CommitStory(StoryAudiences.Everyone);
        }

        var greenInk = ui.Ink with { Accent = StoryRingArt.CloseFriendsGreen };
        if (Button.Draw(SnapButton(closeOnly), Loc.T(L.Story.CloseFriends), greenInk, ButtonStyle.Prominent,
                enabled: !busy))
        {
            CommitStory(StoryAudiences.CloseFriends);
        }
    }

    private void DebounceCloseFriendsSearch()
    {
        var now = ImGui.GetTime();
        if (!string.Equals(closeFriendsDraft, closeFriendsTyped, StringComparison.Ordinal))
        {
            closeFriendsTyped = closeFriendsDraft;
            closeFriendsTypedAt = now;
            return;
        }

        if (now - closeFriendsTypedAt >= CloseFriendsSearchDelaySeconds)
        {
            closeFriends.Search(closeFriendsTyped);
        }
    }

    private void DrawCloseFriendsLoadingRow(Rect listRect, bool members)
    {
        InfiniteScroll.DrawLoadingRow(listRect.Center.X, Ink.MutedInk);
        if (!InfiniteScroll.ReachedBottom())
        {
            return;
        }

        if (members)
        {
            closeFriends.LoadMoreMembers();
            return;
        }

        closeFriends.LoadMoreCandidates();
    }

    private void DrawCloseFriendRow(UserDto user, bool member)
    {
        var row = DrawUserRow(user, FollowPillWidth);
        bool clicked;
        using (UiScope.PushId(user.Id))
        {
            clicked = member
                ? DrawGrayPill(row.Trailing, Loc.T(L.Story.CloseFriendsRemove))
                : DrawAccentPill(row.Trailing, Loc.T(L.Story.Add));
        }

        if (clicked)
        {
            if (member)
            {
                closeFriends.Remove(user);
            }
            else
            {
                closeFriends.Add(user);
            }

            return;
        }

        if (row.Tapped)
        {
            OpenProfile(user.Id);
        }
    }
}
