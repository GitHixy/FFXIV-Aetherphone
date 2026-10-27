using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Gifs;
using Aetherphone.Core.Localization;
using Aetherphone.Core.Social;

namespace Aetherphone.Apps.Chirper;

internal sealed partial class ChirperApp
{
    private string? composeScheduledId;
    private GifPick? restoredScheduledGif;

    private bool EditingScheduled => composeScheduledId is not null;

    private void OpenScheduledEdit(ScheduledPostDto item)
    {
        ClearComposeForm();
        var post = item.Post;
        composeScheduledId = item.Id;
        draft = post.Text;
        composeReplyPolicy = ChirperReplyPolicy.Clamp(post.ReplyPolicy);
        if (post.GifUrl is { Length: > 0 } gifUrl)
        {
            composeGif = new GifPick(new GifItem(post.GifSlug ?? string.Empty, gifUrl, post.MediaWidth, post.MediaHeight,
                gifUrl, post.MediaWidth, post.MediaHeight), string.Empty);
            restoredScheduledGif = composeGif;
            composeAttachments.Add(gifUrl);
            composeSensitive = post.Sensitive;
        }

        if (post.Poll is { } poll && composeAttachments.Count == 0 && post.QuotedPostId is null)
        {
            RestoreComposePoll(poll.Options, DurationIndexFor(poll.DurationHours));
        }

        if (post.QuotedPostId is { } quotedId)
        {
            LoadComposeQuote(quotedId);
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var stillAhead = ScheduleRules.Check(item.PublishAtUnix, nowUnix) == ScheduleIssue.None;
        composeScheduleAtUnix = stillAhead ? item.PublishAtUnix : ScheduleRules.Suggested(DateTime.Now);
        composeFocus = true;
    }

    private static int DurationIndexFor(int hours)
    {
        var hoursList = ChirperPolls.DurationHours;
        for (var index = 0; index < hoursList.Length; index++)
        {
            if (hoursList[index] == hours)
            {
                return index;
            }
        }

        return ChirperPolls.DefaultDurationIndex;
    }

    private LocString ScheduledComposeTitle(LocString fallback) =>
        EditingScheduled ? L.Chirper.EditScheduledTitle : fallback;

    private void SendScheduled(CreatePostRequest request, Action<bool> onComplete)
    {
        if (composeScheduledId is { } scheduledId)
        {
            store.UpdateScheduled(scheduledId, request, composeScheduleAtUnix, onComplete, composeFailure.Set);
            return;
        }

        store.SchedulePost(request, composeScheduleAtUnix, onComplete, composeFailure.Set);
    }
}
