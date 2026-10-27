using Aetherphone.Core.Localization;

namespace Aetherphone.Core.Social;

internal static class ComposerDrafts
{
    public const int MaxPerAccount = 20;

    private const string MetaSeparator = " · ";

    public static int Version { get; private set; }

    public static List<ComposerDraft>? For(Dictionary<string, List<ComposerDraft>> drafts, string? accountId)
    {
        if (accountId is null)
        {
            return null;
        }

        return drafts.TryGetValue(accountId, out var list) ? list : null;
    }

    public static int Count(Dictionary<string, List<ComposerDraft>> drafts, string? accountId) =>
        For(drafts, accountId)?.Count ?? 0;

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static void Put(Dictionary<string, List<ComposerDraft>> drafts, string accountId, ComposerDraft draft)
    {
        if (!drafts.TryGetValue(accountId, out var list))
        {
            list = new List<ComposerDraft>();
            drafts[accountId] = list;
        }

        RemoveFrom(list, draft.Id);
        draft.SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        list.Insert(0, draft);
        if (list.Count > MaxPerAccount)
        {
            list.RemoveRange(MaxPerAccount, list.Count - MaxPerAccount);
        }

        Version++;
    }

    public static bool Remove(Dictionary<string, List<ComposerDraft>> drafts, string? accountId, string? draftId)
    {
        if (draftId is null || For(drafts, accountId) is not { } list || !RemoveFrom(list, draftId))
        {
            return false;
        }

        if (list.Count == 0)
        {
            drafts.Remove(accountId!);
        }

        Version++;
        return true;
    }

    public static List<string> ExistingPaths(List<string> paths, out int missing)
    {
        var existing = new List<string>(paths.Count);
        missing = 0;
        for (var index = 0; index < paths.Count; index++)
        {
            if (File.Exists(paths[index]))
            {
                existing.Add(paths[index]);
                continue;
            }

            missing++;
        }

        return existing;
    }

    public static string Preview(ComposerDraft draft) => PreviewText(draft.Text);

    public static string PreviewText(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Loc.T(L.Social.DraftNoText);
        }

        var lineBreak = text.IndexOf('\n');
        return lineBreak < 0 ? text : text[..lineBreak].TrimEnd();
    }

    public static string Meta(ComposerDraft draft)
    {
        var parts = new List<string>(4);
        if (draft.PhotoPaths.Count > 0)
        {
            parts.Add(Loc.Plural(L.Social.DraftPhotos, draft.PhotoPaths.Count));
        }

        if (draft.Gif is not null)
        {
            parts.Add(Loc.T(L.Social.DraftGif));
        }

        if (draft.PollOptions.Count > 0)
        {
            parts.Add(Loc.T(L.Social.DraftPoll));
        }

        if (draft.QuotedPostId is not null)
        {
            parts.Add(Loc.T(L.Social.DraftQuote));
        }

        parts.Add(TimeText.Short(draft.SavedAtUnix));
        return string.Join(MetaSeparator, parts);
    }

    private static bool RemoveFrom(List<ComposerDraft> list, string draftId)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (string.Equals(list[index].Id, draftId, StringComparison.Ordinal))
            {
                list.RemoveAt(index);
                return true;
            }
        }

        return false;
    }
}
