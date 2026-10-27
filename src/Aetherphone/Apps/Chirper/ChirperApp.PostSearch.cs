using Aetherphone.Core;
using Aetherphone.Core.Animation;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;
using Aetherphone.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private enum DiscoverTab : byte
    {
        People,
        Chirps,
    }

    private readonly FeedVirtualizer postSearchVirtualizer = new(400f);
    private DiscoverTab discoverTab;
    private Spring discoverTabSlide;
    private bool postSearchStale;

    private static bool SearchesPosts(string draft, bool searchingTags) =>
        !searchingTags && !draft.AsSpan().Trim().IsEmpty;

    private Rect DrawDiscoverTabs(Rect listRect)
    {
        var scale = UiScale.Current;
        var row = new Rect(listRect.Min, new Vector2(listRect.Max.X, listRect.Min.Y + FeedTabRowHeight * scale));
        var picked = UnderlineTabs.Draw(row, Loc.T(L.Chirper.SuggestedPeople), Loc.T(L.Chirper.SearchChirpsTab),
            discoverTab == DiscoverTab.Chirps, ref discoverTabSlide, ChirperInk.Shared, FeedTabsStyle);
        if (picked >= 0 && (DiscoverTab)picked != discoverTab)
        {
            discoverTab = (DiscoverTab)picked;
            if (discoverTab == DiscoverTab.Chirps && postSearchStale)
            {
                SyncPostSearch();
            }
        }

        return new Rect(new Vector2(listRect.Min.X, row.Max.Y), listRect.Max);
    }

    // Chirp search only runs while its tab is showing, so typing a name on the People tab costs no extra request.
    private void SyncPostSearch()
    {
        var query = searchDraft.Trim();
        if (query.Length < SocialFeedStore.PostSearchMinLength || query.StartsWith('#'))
        {
            postSearchStale = false;
            store.ClearPostSearch();
            return;
        }

        if (discoverTab != DiscoverTab.Chirps)
        {
            postSearchStale = true;
            return;
        }

        postSearchStale = false;
        store.SearchPosts(query);
    }

    private void DrawPostSearchResults(Rect body)
    {
        var scale = UiScale.Current;
        using (AppSurface.BeginEdgeToEdge(body))
        {
            var posts = store.PostSearchResults;
            if (posts.Length == 0)
            {
                Typography.DrawCentered(new Vector2(body.Center.X, body.Min.Y + 60f * scale), PostSearchEmptyMessage(),
                    ChirperInk.MutedInk, MetaStyle);
                return;
            }

            ImGui.Dummy(new Vector2(0f, FeedTopPadding * scale));
            postSearchVirtualizer.BeginFrame(stepping: configuration);
            for (var index = 0; index < posts.Length; index++)
            {
                var post = posts[index];
                if (HiddenByMediaPreference(post))
                {
                    continue;
                }

                if (postSearchVirtualizer.Skip(post.Id))
                {
                    continue;
                }

                DrawPost(post);
                postSearchVirtualizer.Record(post.Id);
            }

            if (store.PostSearchLoadingMore)
            {
                InfiniteScroll.DrawLoadingRow(body.Center.X, ChirperInk.MutedInk);
            }

            ImGui.Dummy(new Vector2(0f, 24f * scale));
            if (InfiniteScroll.ReachedBottom() && store.HasMorePostSearchResults && !store.PostSearchLoadingMore)
            {
                store.LoadMorePostSearch();
            }
        }
    }

    private string PostSearchEmptyMessage()
    {
        if (searchDraft.AsSpan().Trim().Length < SocialFeedStore.PostSearchMinLength)
        {
            return Loc.T(L.Chirper.SearchChirpsKeepTyping);
        }

        if (store.PostSearchLoading || postSearchStale || searchDirtyAt >= 0d)
        {
            return Loc.T(L.Common.Searching);
        }

        return store.PostSearchFailed ? Loc.T(L.Account.CannotReach) : Loc.T(L.Chirper.SearchChirpsEmpty);
    }
}
