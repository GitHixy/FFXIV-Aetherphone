using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;

namespace Aetherphone.Apps.Chirper;

internal static class ChirperEdits
{
    public const int MaxEdits = 5;
    public const long WindowSeconds = 3600L;

    public static bool CanEdit(PostDto post, string? viewerId, long nowUnix) =>
        viewerId is not null
        && post.AuthorId == viewerId
        && post.RepostOfId is null
        && post.Poll is null
        && post.EditCount < MaxEdits
        && nowUnix - post.CreatedAtUnix < WindowSeconds;

    public static int EditsLeft(PostDto post) => Math.Max(0, MaxEdits - post.EditCount);

    public static bool IsEdited(PostDto post) => post.EditCount > 0 && post.EditedAtUnix is not null;

    public static bool Unchanged(string draft, string original) =>
        draft.AsSpan().Trim().SequenceEqual(original.AsSpan());

    // Every copy keeps its own counters and viewer state; only what the edit changed is carried over,
    // so a reposted or quoted copy of the chirp shows the new words without losing its own fields.
    public static PostDto WithEdit(PostDto host, PostDto edited) => host with
    {
        Text = edited.Text,
        Lang = edited.Lang,
        Mentions = edited.Mentions,
        EditedAtUnix = edited.EditedAtUnix,
        EditCount = edited.EditCount,
        AltTexts = edited.AltTexts,
    };

    public static string RefusalText(FailureSlot slot) => slot.Failed ? slot.Text() : Loc.T(L.Account.CannotReach);
}
