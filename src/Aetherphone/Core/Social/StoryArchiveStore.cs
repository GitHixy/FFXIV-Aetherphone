using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Clients;
using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

internal sealed class StoryArchiveStore : IDisposable
{
    private readonly AethernetSession session;
    private readonly GramClient client;
    private readonly StoreWork work = new("Aethergram story archive");
    private volatile StoryDto[] items = Array.Empty<StoryDto>();
    private volatile string? cursor;
    private volatile bool loading;
    private volatile bool loadingMore;
    private volatile bool loaded;
    private int generation;
    private string? lastAccountId;

    public StoryArchiveStore(AethernetSession session, GramClient client)
    {
        this.session = session;
        this.client = client;
        session.Changed += OnSessionChanged;
    }

    public StoryDto[] Items => items;
    public bool Loading => loading;
    public bool Loaded => loaded;
    public bool LoadingMore => loadingMore;
    public bool HasMore => cursor is not null;

    private void OnSessionChanged()
    {
        var accountId = session.CurrentUser?.Id;
        if (string.Equals(accountId, lastAccountId, StringComparison.Ordinal))
        {
            return;
        }

        lastAccountId = accountId;
        Interlocked.Increment(ref generation);
        items = Array.Empty<StoryDto>();
        cursor = null;
        loaded = false;
    }

    public void Refresh()
    {
        if (!session.IsSignedIn || loading)
        {
            return;
        }

        loading = true;
        var requested = Interlocked.Increment(ref generation);
        work.Run("story archive", async token =>
        {
            var page = await client.StoryArchiveAsync(null, token).ConfigureAwait(false);
            if (page is null || requested != Volatile.Read(ref generation))
            {
                return;
            }

            items = page.Items;
            cursor = page.NextCursor;
            loaded = true;
        }, () => loading = false);
    }

    public void LoadMore()
    {
        var next = cursor;
        if (next is null || loading || loadingMore)
        {
            return;
        }

        loadingMore = true;
        var requested = Volatile.Read(ref generation);
        work.Run("story archive more", async token =>
        {
            var page = await client.StoryArchiveAsync(next, token).ConfigureAwait(false);
            if (page is null || requested != Volatile.Read(ref generation))
            {
                return;
            }

            items = Append(items, page.Items);
            cursor = page.NextCursor;
        }, () => loadingMore = false);
    }

    public void Delete(string storyId, Action<bool> onComplete)
    {
        work.Run("delete archived story", async token =>
        {
            if (!await client.DeleteStoryAsync(storyId, token).ConfigureAwait(false))
            {
                return false;
            }

            items = Without(items, storyId);
            return true;
        }, onComplete);
    }

    public static StoryDto[] Without(StoryDto[] source, string storyId)
    {
        var at = -1;
        for (var index = 0; index < source.Length; index++)
        {
            if (string.Equals(source[index].Id, storyId, StringComparison.Ordinal))
            {
                at = index;
                break;
            }
        }

        if (at < 0)
        {
            return source;
        }

        var kept = new StoryDto[source.Length - 1];
        Array.Copy(source, 0, kept, 0, at);
        Array.Copy(source, at + 1, kept, at, source.Length - at - 1);
        return kept;
    }

    private static StoryDto[] Append(StoryDto[] source, StoryDto[] incoming)
    {
        if (incoming.Length == 0)
        {
            return source;
        }

        var known = new HashSet<string>(source.Length, StringComparer.Ordinal);
        for (var index = 0; index < source.Length; index++)
        {
            known.Add(source[index].Id);
        }

        var merged = new List<StoryDto>(source.Length + incoming.Length);
        merged.AddRange(source);
        for (var index = 0; index < incoming.Length; index++)
        {
            if (known.Add(incoming[index].Id))
            {
                merged.Add(incoming[index]);
            }
        }

        return merged.Count == source.Length ? source : merged.ToArray();
    }

    public void Dispose()
    {
        session.Changed -= OnSessionChanged;
        work.Dispose();
    }
}
