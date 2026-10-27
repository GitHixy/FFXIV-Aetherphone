using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;

namespace Aetherphone.Apps.Chirper;

internal static class ChirperEdits
{
    public const int MaxEdits = 5;
    public const long WindowSeconds = 3600L;

    private const string WindowClosedCode = "post_edit_window_closed";
    private const string LimitCode = "post_edit_limit";

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
    };

    public static string RefusalText(FailureSlot slot)
    {
        var code = slot.Failure.Code;
        if (code == WindowClosedCode)
        {
            return Loc.T(L.Chirper.EditWindowClosed);
        }

        if (code == LimitCode)
        {
            return Loc.T(L.Chirper.EditLimitReached);
        }

        return slot.Failed ? slot.Text() : Loc.T(L.Account.CannotReach);
    }
}
