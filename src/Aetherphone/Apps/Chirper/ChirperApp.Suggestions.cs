using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const int FewFollows = 5;
    private const int PeopleRailAfterPosts = 5;
    private const float EmptyFollowingListTop = 130f;
    private const float BannerFollowGap = 10f;
    private const string PeopleRailRowId = "chirper.people";
    private const string PeopleAnchor = "chirper.people";

    private readonly SuggestedPeopleRail peopleRail = new();
    private readonly HashSet<string> followedFromFeed = new(StringComparer.Ordinal);

    private int PeopleRailSlot(SocialFeedScope scope, int postCount)
    {
        if (scope != SocialFeedScope.ForYou)
        {
            return -1;
        }

        store.EnsureSuggestedPeople();
        if (!SuggestedPeopleRail.HasAny(store.SuggestedPeople, configuration.DismissedFollowSuggestions))
        {
            return -1;
        }

        var follows = store.Me?.Following ?? 0;
        return Math.Min(postCount, follows < FewFollows ? 0 : PeopleRailAfterPosts);
    }

    private void DrawPeopleRailRow()
    {
        if (feedVirtualizer.Skip(PeopleRailRowId))
        {
            return;
        }

        var result = peopleRail.Draw("chirper.people.", store.SuggestedPeople, configuration.DismissedFollowSuggestions,
            ChirperInk.Shared, SectionLabelStyle, new SuggestedPeopleLabels(Loc.T(L.Chirper.Follow), Loc.T(L.Chirper.Following)),
            PeopleAnchor, theme, images, lodestone);
        feedVirtualizer.Record(PeopleRailRowId);
        if (SuggestedPeopleRail.OpensProfile(result, store, configuration))
        {
            OpenProfile(result.User!.Id);
        }
    }

    private void DrawSuggestedPeopleList(float top)
    {
        store.EnsureSuggestedPeople();
        var people = store.SuggestedPeople;
        var dismissed = configuration.DismissedFollowSuggestions;
        if (!SuggestedPeopleRail.HasAny(people, dismissed))
        {
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, top));
        DrawSectionLabel(Loc.T(L.Social.PeopleToFollow));
        for (var index = 0; index < people.Length; index++)
        {
            if (dismissed.Contains(people[index].User.Id))
            {
                continue;
            }

            DrawUserRow(people[index].User, SuggestedPeopleRail.ReasonLine(people[index]));
        }
    }
}
