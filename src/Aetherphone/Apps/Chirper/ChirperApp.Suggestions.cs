using System.Globalization;
using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private const int FewFollows = 5;
    private const float EmptyFollowingListTop = 130f;
    private const float BannerFollowGap = 10f;
    private const float CollapsedCellHeight = 48f;
    private const int ViewsLabelCap = 512;
    private const string PeopleRailRowId = "chirper.people";
    private const string PeopleAnchor = "chirper.people";

    private readonly SuggestedPeopleRail peopleRail = new();
    private readonly HashSet<string> followedFromFeed = new(StringComparer.Ordinal);
    private bool drawingForYou;
    private readonly Dictionary<string, ViewsLabel> viewsLabels = new(StringComparer.Ordinal);

    private readonly record struct ViewsLabel(int Views, CultureInfo Culture, string Text);

    private string ViewsText(string postId, int views)
    {
        if (viewsLabels.TryGetValue(postId, out var label) && label.Views == views && ReferenceEquals(label.Culture, Loc.Culture))
        {
            return label.Text;
        }

        if (viewsLabels.Count >= ViewsLabelCap)
        {
            viewsLabels.Clear();
        }

        var text = views > 0 ? CountText.Compact(views) : string.Empty;
        viewsLabels[postId] = new ViewsLabel(views, Loc.Culture, text);
        return text;
    }

    private bool ShowsPeopleRail(SocialFeedScope scope)
    {
        if (scope != SocialFeedScope.ForYou || (store.Me?.Following ?? 0) >= FewFollows)
        {
            return false;
        }

        store.EnsureSuggestedPeople();
        return SuggestedPeopleRail.HasAny(store.SuggestedPeople, configuration.DismissedFollowSuggestions);
    }

    private void DrawPeopleRailRow()
    {
        if (feedVirtualizer.Skip(PeopleRailRowId))
        {
            return;
        }

        var result = peopleRail.Draw(store.SuggestedPeople, configuration.DismissedFollowSuggestions,
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

        ImGui.Dummy(new Vector2(0f, ComposeFab.Clearance(FabRadius, true)));
    }
}
