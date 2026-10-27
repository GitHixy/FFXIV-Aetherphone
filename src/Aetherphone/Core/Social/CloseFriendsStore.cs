using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Clients;
using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal sealed class CloseFriendsStore : IDisposable
{
    private readonly GramClient client;
    private readonly StoreWork work = new("Aethergram close friends");
    private volatile UserDto[] members = Array.Empty<UserDto>();
    private volatile UserDto[] candidates = Array.Empty<UserDto>();
    private volatile string? membersCursor;
    private volatile string? candidatesCursor;
    private volatile bool membersLoading;
    private volatile bool candidatesLoading;
    private volatile bool failed;
    private int generation;
    private string query = string.Empty;

    public CloseFriendsStore(GramClient client)
    {
        this.client = client;
    }

    public UserDto[] Members => members;
    public UserDto[] Candidates => candidates;
    public bool Loading => membersLoading || candidatesLoading;
    public bool Failed => failed;
    public bool HasMoreMembers => membersCursor is not null;
    public bool HasMoreCandidates => candidatesCursor is not null;

    public void Open()
    {
        query = string.Empty;
        Reload();
    }

    public void Search(string text)
    {
        var trimmed = text.Trim();
        if (string.Equals(trimmed, query, StringComparison.Ordinal))
        {
            return;
        }

        query = trimmed;
        Reload();
    }

    public void LoadMoreMembers()
    {
        var cursor = membersCursor;
        if (cursor is null || membersLoading)
        {
            return;
        }

        membersLoading = true;
        var expected = Volatile.Read(ref generation);
        work.Run("close friends more", async token =>
        {
            var page = await client.CloseFriendsAsync(string.Empty, cursor, token).ConfigureAwait(false);
            if (page is null || expected != Volatile.Read(ref generation))
            {
                return;
            }

            members = Append(members, page.Items);
            membersCursor = page.NextCursor;
        }, () => membersLoading = false);
    }

    public void LoadMoreCandidates()
    {
        var cursor = candidatesCursor;
        if (cursor is null || candidatesLoading)
        {
            return;
        }

        candidatesLoading = true;
        var expected = Volatile.Read(ref generation);
        work.Run("close friend candidates more", async token =>
        {
            var page = await client.CloseFriendCandidatesAsync(string.Empty, cursor, token).ConfigureAwait(false);
            if (page is null || expected != Volatile.Read(ref generation))
            {
                return;
            }

            candidates = Append(candidates, page.Items);
            candidatesCursor = page.NextCursor;
        }, () => candidatesLoading = false);
    }

    public void Add(UserDto user)
    {
        candidates = Without(candidates, user.Id);
        members = Prepend(members, user);
        work.Run("close friend add", async token =>
        {
            if (!await client.AddCloseFriendAsync(user.Id, token).ConfigureAwait(false))
            {
                Reload();
            }
        });
    }

    public void Remove(UserDto user)
    {
        members = Without(members, user.Id);
        if (user.FollowsYou)
        {
            candidates = Prepend(candidates, user);
        }

        work.Run("close friend remove", async token =>
        {
            if (!await client.RemoveCloseFriendAsync(user.Id, token).ConfigureAwait(false))
            {
                Reload();
            }
        });
    }

    private void Reload()
    {
        var expected = Interlocked.Increment(ref generation);
        var text = query;
        failed = false;
        membersLoading = true;
        candidatesLoading = true;
        work.Run("close friends", async token =>
        {
            var page = await client.CloseFriendsAsync(text, null, token).ConfigureAwait(false);
            if (expected != Volatile.Read(ref generation))
            {
                return;
            }

            failed |= page is null;
            members = page?.Items ?? Array.Empty<UserDto>();
            membersCursor = page?.NextCursor;
        }, () => membersLoading = false);
        work.Run("close friend candidates", async token =>
        {
            var page = await client.CloseFriendCandidatesAsync(text, null, token).ConfigureAwait(false);
            if (expected != Volatile.Read(ref generation))
            {
                return;
            }

            failed |= page is null;
            candidates = page?.Items ?? Array.Empty<UserDto>();
            candidatesCursor = page?.NextCursor;
        }, () => candidatesLoading = false);
    }

    private static UserDto[] Append(UserDto[] source, UserDto[] incoming)
    {
        var result = new List<UserDto>(source.Length + incoming.Length);
        result.AddRange(source);
        for (var index = 0; index < incoming.Length; index++)
        {
            if (IndexOf(source, incoming[index].Id) < 0)
            {
                result.Add(incoming[index]);
            }
        }

        return result.ToArray();
    }

    private static UserDto[] Prepend(UserDto[] source, UserDto user)
    {
        if (IndexOf(source, user.Id) >= 0)
        {
            return source;
        }

        var result = new UserDto[source.Length + 1];
        result[0] = user;
        Array.Copy(source, 0, result, 1, source.Length);
        return result;
    }

    private static UserDto[] Without(UserDto[] source, string userId)
    {
        var position = IndexOf(source, userId);
        if (position < 0)
        {
            return source;
        }

        var result = new UserDto[source.Length - 1];
        Array.Copy(source, 0, result, 0, position);
        Array.Copy(source, position + 1, result, position, source.Length - position - 1);
        return result;
    }

    private static int IndexOf(UserDto[] source, string userId)
    {
        for (var index = 0; index < source.Length; index++)
        {
            if (string.Equals(source[index].Id, userId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public void Dispose()
    {
        work.Dispose();
    }
}
