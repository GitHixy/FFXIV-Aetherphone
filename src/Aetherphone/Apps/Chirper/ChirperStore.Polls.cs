using Aetherphone.Core;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Net;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperStore
{
    private readonly object pollGate = new();
    private readonly HashSet<string> pollVotesInFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> pollRefreshes = new(StringComparer.Ordinal);
    private AepFailureBox? pollVoteFailure;

    public void ComposePoll(string text, PostPollInput poll, Action<bool> onComplete,
        Action<AepFailure>? onFailure = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || posting)
        {
            return;
        }

        posting = true;
        work.Run("compose poll", async token =>
        {
            var created = await client.CreatePollPostAsync(trimmed, poll, token, onFailure).ConfigureAwait(false);
            if (created is null)
            {
                return false;
            }

            AcceptCreatedPost(created);
            return true;
        }, onComplete, () => posting = false);
    }

    public bool PollVotePending(string postId)
    {
        lock (pollGate)
        {
            return pollVotesInFlight.Contains(postId);
        }
    }

    public bool TakePollVoteFailure(out AepFailure failure)
    {
        var box = Interlocked.Exchange(ref pollVoteFailure, null);
        failure = box?.Failure ?? AepFailure.None;
        return box is not null;
    }

    public void VotePoll(PostDto post, int optionIndex)
    {
        if (post.Poll is not { } previous)
        {
            return;
        }

        var isAuthor = Me is { } me && me.Id == post.AuthorId;
        if (!ChirperPolls.CanVote(previous, isAuthor, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
        {
            return;
        }

        var optimistic = ChirperPolls.ApplyLocalVote(previous, optionIndex);
        if (ReferenceEquals(optimistic, previous))
        {
            return;
        }

        lock (pollGate)
        {
            if (!pollVotesInFlight.Add(post.Id))
            {
                return;
            }
        }

        var postId = post.Id;
        MapPollEverywhere(postId, previous, optimistic);
        work.Run("poll vote", async token =>
        {
            var reported = AepFailure.None;
            var counted = await client.VotePostPollAsync(postId, optionIndex, token, failure => reported = failure)
                .ConfigureAwait(false);
            if (counted is not null)
            {
                MapPollEverywhere(postId, optimistic, counted);
                return;
            }

            MapPollEverywhere(postId, optimistic, previous);
            Interlocked.Exchange(ref pollVoteFailure, new AepFailureBox(reported.Failed
                ? reported
                : AepFailure.Transport(AepFailureKind.Offline)));
            AepLog.Warning($"Chirper poll vote failed: {reported.Describe()}");
            if (reported.Code is FailureCodes.PollAlreadyVoted or FailureCodes.PollClosed)
            {
                await RefetchPollPostAsync(postId, token).ConfigureAwait(false);
            }
        }, () =>
        {
            lock (pollGate)
            {
                pollVotesInFlight.Remove(postId);
            }
        });
    }

    public void RequestPollRefresh(string postId)
    {
        lock (pollGate)
        {
            if (!pollRefreshes.Add(postId))
            {
                return;
            }
        }

        work.Run("poll refresh", token => RefetchPollPostAsync(postId, token));
    }

    private async Task RefetchPollPostAsync(string postId, CancellationToken token)
    {
        var fresh = await client.PostAsync(postId, token).ConfigureAwait(false);
        if (fresh?.Poll is not { } poll)
        {
            return;
        }

        ReplacePost(fresh);
        MapReferencedEverywhere(postId, host => ChirperPolls.WithPoll(host, poll));
    }

    private void MapPollEverywhere(string postId, PostPollDto expected, PostPollDto next)
    {
        PostDto Swap(PostDto host) => host.Poll is { } current
            ? ChirperPolls.WithPoll(host, ChirperPolls.Swap(current, expected, next))
            : host;
        MapPostEverywhere(postId, Swap);
        MapReferencedEverywhere(postId, Swap);
    }
}
