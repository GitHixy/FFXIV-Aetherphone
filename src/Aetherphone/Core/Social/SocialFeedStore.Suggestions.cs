using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal abstract partial class SocialFeedStore
{
    private const int SuggestedPeopleLimit = 20;
    private const long SuggestedPeopleRefreshMilliseconds = 600_000;

    private volatile SuggestedUserDto[] suggestedPeople = Array.Empty<SuggestedUserDto>();
    private volatile bool suggestedPeopleLoading;
    private long suggestedPeopleAttemptTick;

    public SuggestedUserDto[] SuggestedPeople => suggestedPeople;

    public void EnsureSuggestedPeople()
    {
        if (!session.IsSignedIn || suggestedPeopleLoading)
        {
            return;
        }

        var now = Environment.TickCount64;
        var lastAttempt = Interlocked.Read(ref suggestedPeopleAttemptTick);
        if (lastAttempt != 0 && now - lastAttempt < SuggestedPeopleRefreshMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref suggestedPeopleAttemptTick, now);
        suggestedPeopleLoading = true;
        work.Run("suggested people", async token =>
        {
            var page = await client.SuggestedUsersAsync(SuggestedPeopleLimit, token).ConfigureAwait(false);
            if (page is not null)
            {
                suggestedPeople = page.Items;
            }
        }, () => suggestedPeopleLoading = false);
    }

    public void DismissSuggestion(string userId)
    {
        suggestedPeople = CopyOnWrite.RemoveWhere(suggestedPeople, suggestion => suggestion.User.Id == userId);
    }

    private void ResetSuggestedPeople()
    {
        suggestedPeople = Array.Empty<SuggestedUserDto>();
        Interlocked.Exchange(ref suggestedPeopleAttemptTick, 0);
    }

    private void MapSuggestedFollow(string userId, bool following, bool requested)
    {
        suggestedPeople = CopyOnWrite.Map(suggestedPeople,
            suggestion => suggestion.User.Id == userId
                && (suggestion.User.IsFollowing != following || suggestion.User.FollowRequested != requested),
            suggestion => suggestion with
            {
                User = suggestion.User with { IsFollowing = following, FollowRequested = requested },
            });
    }
}
