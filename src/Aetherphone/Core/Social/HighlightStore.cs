using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Clients;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Net;

namespace Aetherphone.Core.Social;

internal readonly record struct HighlightEdit(
    string HighlightId,
    string? Title,
    string? CoverStoryId,
    string[] Added,
    string[] Removed);

internal sealed class HighlightStore : IDisposable
{
    public const int TitleMaxLength = 15;
    public const int MaxItems = 100;
    public const int MaxHighlights = 50;

    private const long ProfileRefreshMilliseconds = 60_000;

    private readonly AethernetSession session;
    private readonly GramClient client;
    private readonly StoreWork work = new("Aethergram highlights");
    private readonly Action<AepFailure> recordFailure;
    private volatile StoryHighlightDto[] profileHighlights = Array.Empty<StoryHighlightDto>();
    private volatile string? profileOwnerId;
    private volatile bool profileLoading;
    private long profileLoadedAt;
    private volatile StoryHighlightDto[] mine = Array.Empty<StoryHighlightDto>();
    private volatile bool mineLoaded;
    private volatile bool mineLoading;
    private string? mineOwnerId;
    private volatile string? openingId;
    private volatile StoryHighlightDto? openedHighlight;
    private volatile StoryDto[]? openedStories;
    private volatile StoryDto[] editorStories = Array.Empty<StoryDto>();
    private volatile string[] editorMembers = Array.Empty<string>();
    private volatile bool editorLoading;
    private volatile int editorVersion;
    private volatile bool saving;
    private volatile AepFailureBox? lastFailure;
    private volatile string[] holdingIds = Array.Empty<string>();
    private volatile string? holdingStoryId;
    private string? lastAccountId;

    public HighlightStore(AethernetSession session, GramClient client)
    {
        this.session = session;
        this.client = client;
        recordFailure = failure => lastFailure = new AepFailureBox(failure);
        session.Changed += OnSessionChanged;
    }

    public StoryHighlightDto[] Mine => mine;
    public bool MineLoaded => mineLoaded;
    public bool Opening => openingId is not null;
    public bool Saving => saving;
    public StoryDto[] EditorStories => editorStories;
    public string[] EditorMembers => editorMembers;
    public bool EditorLoading => editorLoading;
    public int EditorVersion => editorVersion;
    public AepFailure LastFailure => lastFailure?.Failure ?? AepFailure.None;

    public string[] HoldingFor(string storyId) =>
        string.Equals(holdingStoryId, storyId, StringComparison.Ordinal) ? holdingIds : Array.Empty<string>();

    private void OnSessionChanged()
    {
        var accountId = session.CurrentUser?.Id;
        if (string.Equals(accountId, lastAccountId, StringComparison.Ordinal))
        {
            return;
        }

        lastAccountId = accountId;
        profileOwnerId = null;
        profileHighlights = Array.Empty<StoryHighlightDto>();
        mine = Array.Empty<StoryHighlightDto>();
        mineLoaded = false;
        mineOwnerId = null;
        openingId = null;
        openedHighlight = null;
        openedStories = null;
        holdingStoryId = null;
        holdingIds = Array.Empty<string>();
    }

    public StoryHighlightDto[] For(string ownerId)
    {
        if (!string.Equals(profileOwnerId, ownerId, StringComparison.Ordinal))
        {
            profileOwnerId = ownerId;
            profileHighlights = Array.Empty<StoryHighlightDto>();
            LoadProfile(ownerId);
        }
        else if (!profileLoading
                 && Environment.TickCount64 - Interlocked.Read(ref profileLoadedAt) >= ProfileRefreshMilliseconds)
        {
            LoadProfile(ownerId);
        }

        return profileHighlights;
    }

    public void EnsureMine(string ownerId)
    {
        mineOwnerId = session.CurrentUser?.Id ?? ownerId;
        if (mineLoaded || mineLoading)
        {
            return;
        }

        RefreshMine();
    }

    public void LoadHolding(string storyId)
    {
        holdingStoryId = storyId;
        holdingIds = Array.Empty<string>();
        work.Run("highlights holding story", async token =>
        {
            var membership = await client.HighlightsHoldingAsync(storyId, token).ConfigureAwait(false);
            if (membership is not null && string.Equals(holdingStoryId, storyId, StringComparison.Ordinal))
            {
                holdingIds = membership.HighlightIds;
            }
        });
    }

    public void Open(StoryHighlightDto highlight)
    {
        if (openingId is not null)
        {
            return;
        }

        openingId = highlight.Id;
        work.Run("open highlight", async token =>
        {
            var group = await client.HighlightStoriesAsync(highlight.Id, token).ConfigureAwait(false);
            if (group is not null && group.Items.Length > 0 && openingId == highlight.Id)
            {
                openedStories = group.Items;
                openedHighlight = highlight;
            }
        }, () => openingId = null);
    }

    public bool TryTakeOpened(out StoryHighlightDto highlight, out StoryDto[] stories)
    {
        var taken = openedHighlight;
        var items = openedStories;
        if (taken is null || items is null)
        {
            highlight = null!;
            stories = Array.Empty<StoryDto>();
            return false;
        }

        openedHighlight = null;
        openedStories = null;
        highlight = taken;
        stories = items;
        return true;
    }

    public void LoadEditor(string? highlightId)
    {
        var me = session.CurrentUser?.Id;
        if (me is null)
        {
            return;
        }

        editorStories = Array.Empty<StoryDto>();
        editorMembers = Array.Empty<string>();
        editorLoading = true;
        work.Run("highlight editor", async token =>
        {
            var held = highlightId is null
                ? null
                : await client.HighlightStoriesAsync(highlightId, token).ConfigureAwait(false);
            var active = await client.UserStoriesAsync(me, token).ConfigureAwait(false);
            var heldItems = held?.Items ?? Array.Empty<StoryDto>();
            var activeItems = active?.Items ?? Array.Empty<StoryDto>();
            var merged = new List<StoryDto>(heldItems.Length + activeItems.Length);
            var memberIds = new string[heldItems.Length];
            for (var index = 0; index < heldItems.Length; index++)
            {
                merged.Add(heldItems[index]);
                memberIds[index] = heldItems[index].Id;
            }

            for (var index = 0; index < activeItems.Length; index++)
            {
                if (Array.IndexOf(memberIds, activeItems[index].Id) < 0)
                {
                    merged.Add(activeItems[index]);
                }
            }

            editorStories = merged.ToArray();
            editorMembers = memberIds;
            editorVersion++;
        }, () => editorLoading = false);
    }

    public void Create(string title, string[] storyIds, string? coverStoryId, Action<bool> onComplete)
    {
        Write("create highlight", async token =>
            await client.CreateHighlightAsync(title, storyIds, coverStoryId, token, recordFailure).ConfigureAwait(false)
                is not null,
            onComplete);
    }

    public void AddStory(string highlightId, string storyId, Action<bool> onComplete)
    {
        Write("add to highlight", async token =>
            await client.AddToHighlightAsync(highlightId, storyId, token, recordFailure).ConfigureAwait(false) is not null,
            onComplete);
    }

    public void RemoveStory(string highlightId, string storyId, Action<bool> onComplete)
    {
        Write("remove from highlight",
            token => client.RemoveFromHighlightAsync(highlightId, storyId, token, recordFailure), onComplete);
    }

    public void Delete(string highlightId, Action<bool> onComplete)
    {
        Write("delete highlight", token => client.DeleteHighlightAsync(highlightId, token, recordFailure), onComplete);
    }

    public void Save(HighlightEdit edit, Action<bool> onComplete)
    {
        Write("edit highlight", async token =>
        {
            for (var index = 0; index < edit.Added.Length; index++)
            {
                if (await client.AddToHighlightAsync(edit.HighlightId, edit.Added[index], token, recordFailure)
                        .ConfigureAwait(false) is null)
                {
                    return false;
                }
            }

            if ((edit.Title is not null || edit.CoverStoryId is not null)
                && await client.UpdateHighlightAsync(edit.HighlightId, edit.Title, edit.CoverStoryId, token,
                    recordFailure).ConfigureAwait(false) is null)
            {
                return false;
            }

            for (var index = 0; index < edit.Removed.Length; index++)
            {
                if (!await client.RemoveFromHighlightAsync(edit.HighlightId, edit.Removed[index], token,
                        recordFailure).ConfigureAwait(false))
                {
                    return false;
                }
            }

            return true;
        }, onComplete);
    }

    private void Write(string operation, Func<CancellationToken, Task<bool>> action, Action<bool> onComplete)
    {
        if (saving)
        {
            onComplete(false);
            return;
        }

        saving = true;
        lastFailure = null;
        work.Run(operation, action, succeeded =>
        {
            RefreshMine();
            var me = session.CurrentUser?.Id;
            if (me is not null && string.Equals(profileOwnerId, me, StringComparison.Ordinal))
            {
                LoadProfile(me);
            }

            onComplete(succeeded);
        }, () => saving = false);
    }

    private void RefreshMine()
    {
        var me = session.CurrentUser?.Id ?? mineOwnerId;
        if (me is null)
        {
            return;
        }

        mineLoading = true;
        work.Run("own highlights", async token =>
        {
            var list = await client.HighlightsAsync(me, token).ConfigureAwait(false);
            if (list is not null)
            {
                mine = list.Items;
                mineLoaded = true;
            }
        }, () => mineLoading = false);
    }

    private void LoadProfile(string ownerId)
    {
        profileLoading = true;
        Interlocked.Exchange(ref profileLoadedAt, Environment.TickCount64);
        work.Run("profile highlights", async token =>
        {
            var list = await client.HighlightsAsync(ownerId, token).ConfigureAwait(false);
            if (list is not null && string.Equals(profileOwnerId, ownerId, StringComparison.Ordinal))
            {
                profileHighlights = list.Items;
            }
        }, () => profileLoading = false);
    }

    public void Dispose()
    {
        session.Changed -= OnSessionChanged;
        work.Dispose();
    }
}
