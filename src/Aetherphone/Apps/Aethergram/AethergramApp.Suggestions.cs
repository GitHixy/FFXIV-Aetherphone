using Aetherphone.Core;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Aethergram;

internal sealed partial class AethergramApp
{
    private const int FewFollows = 5;
    private const int PeopleRailAfterPosts = 3;
    private const float EmptyFollowingListGap = 44f;
    private const float BannerFollowGap = 10f;
    private const float CollapsedCellHeight = 48f;
    private const int CollapsedRevision = 2;
    private const string PeopleRailRowId = "aethergram.people";
    private const string PeopleAnchor = "aethergram.people";
    private static readonly TextStyle BannerFollowStyle = TextStyles.SubheadlineEmphasized;

    private readonly SuggestedPeopleRail peopleRail = new();
    private readonly HashSet<string> followedFromFeed = new(StringComparer.Ordinal);
    private bool drawingForYou;

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

        var result = peopleRail.Draw(store.SuggestedPeople, configuration.DismissedFollowSuggestions,
            Ink, SectionLabelStyle, new SuggestedPeopleLabels(Loc.T(L.Aethergram.Follow), Loc.T(L.Aethergram.Following)),
            PeopleAnchor, theme, images, lodestone);
        feedVirtualizer.Record(PeopleRailRowId);
        if (SuggestedPeopleRail.OpensProfile(result, store, configuration))
        {
            OpenProfile(result.User!.Id);
        }
    }

    private void DrawNotInterestedCell(string postId)
    {
        var scale = UiScale.Current;
        var drawList = ImGui.GetWindowDrawList();
        var cell = FeedCell.Begin(drawList, CollapsedCellHeight * scale, Ink.HoverTint, false);
        var padX = CellPadX * scale;
        var undo = Loc.T(L.Social.Undo);
        var undoSize = Typography.Measure(undo, BannerFollowStyle);
        var centerY = cell.Bounds.Min.Y + cell.Bounds.Height * 0.5f;
        var undoMin = new Vector2(cell.Bounds.Max.X - padX - undoSize.X, centerY - undoSize.Y * 0.5f);
        var undoMax = undoMin + undoSize;
        var text = Typography.FitText(Loc.T(L.Social.NotInterestedDone),
            MathF.Max(1f, undoMin.X - BannerFollowGap * scale - cell.Bounds.Min.X - padX), CardMetaStyle);
        var size = Typography.Measure(text, CardMetaStyle);
        Typography.Draw(drawList, new Vector2(cell.Bounds.Min.X + padX, centerY - size.Y * 0.5f), text, Ink.MutedInk,
            CardMetaStyle);
        var hovered = UiInteract.Hover(undoMin, undoMax);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        Typography.Draw(drawList, undoMin, undo, hovered ? Ink.TitleInk : Ink.AccentLink, BannerFollowStyle);
        if (UiInteract.Click(undoMin, undoMax, hovered))
        {
            store.UndoNotInterested(postId);
        }

        FeedCell.End(drawList, cell, Ink.Hairline);
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
        SocialChrome.DrawSectionLabel(Loc.T(L.Social.PeopleToFollow), Ink, SectionLabelStyle);
        for (var index = 0; index < people.Length; index++)
        {
            if (dismissed.Contains(people[index].User.Id))
            {
                continue;
            }

            DrawUserRowWithFollow(people[index].User, SuggestedPeopleRail.ReasonLine(people[index]));
        }
    }
}
