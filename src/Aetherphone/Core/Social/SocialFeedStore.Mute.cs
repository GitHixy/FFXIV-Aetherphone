using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private static readonly Dictionary<string, bool> NoMuteChanges = new(StringComparer.Ordinal);

    private volatile Dictionary<string, bool> muteChanges = NoMuteChanges;
    private volatile UserDto[] mutedAccounts = Array.Empty<UserDto>();
    private volatile string? mutedAccountsCursor;
    private volatile bool mutedAccountsLoading;
    private volatile bool mutedAccountsLoadingMore;
    private volatile bool mutedAccountsLoaded;
    private volatile bool mutedAccountsFailed;

    public UserDto[] MutedAccounts => mutedAccounts;
    public bool MutedAccountsLoading => mutedAccountsLoading;
    public bool MutedAccountsLoadingMore => mutedAccountsLoadingMore;
    public bool MutedAccountsLoaded => mutedAccountsLoaded;
    public bool MutedAccountsFailed => mutedAccountsFailed;
    public bool HasMoreMutedAccounts => mutedAccountsCursor is not null;

    public bool IsMuted(UserDto user) =>
        muteChanges.TryGetValue(user.Id, out var muted) ? muted : user.IsMuted;

    public bool IsMuted(string userId)
    {
        if (muteChanges.TryGetValue(userId, out var muted))
        {
            return muted;
        }

        return profileUser is { } shown && shown.Id == userId && shown.IsMuted;
    }

    public void SetMuted(string userId, bool muted, Action<bool> onComplete)
    {
        RecordMuteChange(userId, muted);
        if (muted)
        {
            HideMutedAuthor(userId);
        }

        work.Run(muted ? "mute" : "unmute", async token => muted
            ? await client.MuteAsync(userId, token).ConfigureAwait(false)
            : await client.UnmuteAsync(userId, token).ConfigureAwait(false), saved =>
        {
            if (!saved)
            {
                AepLog.Warning($"{(muted ? "Mute" : "Unmute")} of {userId} failed; restoring the previous state");
                RecordMuteChange(userId, !muted);
                if (muted)
                {
                    RefreshLoadedFeeds();
                }
            }

            onComplete(saved);
        });
    }

    public void RefreshMutedAccounts()
    {
        if (!session.IsSignedIn || mutedAccountsLoading)
        {
            return;
        }

        mutedAccountsLoading = true;
        work.Run("muted accounts", async token =>
        {
            var page = await client.MutedUsersAsync(null, token).ConfigureAwait(false);
            if (page is null)
            {
                mutedAccountsFailed = true;
                return;
            }

            mutedAccounts = page.Items;
            mutedAccountsCursor = page.NextCursor;
            mutedAccountsFailed = false;
            mutedAccountsLoaded = true;
        }, () => mutedAccountsLoading = false);
    }

    public void LoadMoreMutedAccounts()
    {
        var cursor = mutedAccountsCursor;
        if (!session.IsSignedIn || cursor is null || mutedAccountsLoadingMore || mutedAccountsLoading)
        {
            return;
        }

        mutedAccountsLoadingMore = true;
        work.Run("muted accounts more", async token =>
        {
            var page = await client.MutedUsersAsync(cursor, token).ConfigureAwait(false);
            if (page is null)
            {
                return;
            }

            mutedAccounts = CopyOnWrite.AppendPageById(mutedAccounts, page.Items);
            mutedAccountsCursor = page.NextCursor;
        }, () => mutedAccountsLoadingMore = false);
    }

    private void RecordMuteChange(string userId, bool muted)
    {
        muteChanges = new Dictionary<string, bool>(muteChanges, StringComparer.Ordinal) { [userId] = muted };
    }

    // Only the feeds lose the author: their profile, the post being read, saved and liked posts stay,
    // because a mute narrows what is pushed at the viewer and nothing the viewer opens on purpose.
    private void HideMutedAuthor(string userId)
    {
        HideMutedAuthor(forYouLane, userId);
        HideMutedAuthor(latestLane, userId);
        HideMutedAuthor(followingLane, userId);
        HideMutedAuthor(hashtagLane, userId);
        pendingForYouPage = null;
        DismissSuggestion(userId);
    }

    private static void HideMutedAuthor(FeedLane<PostDto> lane, string userId)
    {
        lane.Items = CopyOnWrite.RemoveWhere(lane.Items, post => BlockedContent.Hides(post, userId));
        lane.DiscardPending();
    }

    private void RefreshLoadedFeeds()
    {
        RefreshLoadedFeed(SocialFeedScope.ForYou);
        RefreshLoadedFeed(SocialFeedScope.Latest);
        RefreshLoadedFeed(SocialFeedScope.Following);
    }

    private void RefreshLoadedFeed(SocialFeedScope scope)
    {
        if (!Lane(scope).NeverLoaded)
        {
            RefreshFeed(scope);
        }
    }

    private void ResetMutes()
    {
        muteChanges = NoMuteChanges;
        mutedAccounts = Array.Empty<UserDto>();
        mutedAccountsCursor = null;
        mutedAccountsLoaded = false;
        mutedAccountsFailed = false;
    }
}
