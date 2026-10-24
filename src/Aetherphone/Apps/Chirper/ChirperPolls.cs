using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Localization;

namespace Aetherphone.Apps.Chirper;

internal static class ChirperPolls
{
    public const int MinOptions = 2;
    public const int MaxOptions = 4;
    public const int MaxOptionLength = 40;
    public const int DefaultDurationIndex = 2;

    public static readonly int[] DurationHours = { 1, 6, 24, 72, 168 };

    public static readonly LocString[] DurationLabels =
    {
        L.Chirper.PollOneHour,
        L.Chirper.PollSixHours,
        L.Chirper.PollOneDay,
        L.Chirper.PollThreeDays,
        L.Chirper.PollSevenDays,
    };

    public static bool HasEnded(PostPollDto poll, long nowUnix) =>
        poll.Ended || (poll.EndsAtUnix > 0 && nowUnix >= poll.EndsAtUnix);

    public static bool ResultsVisible(PostPollDto poll) => poll.Counts is not null;

    public static bool CanVote(PostPollDto poll, bool isAuthor, long nowUnix) =>
        !isAuthor && poll.MyVote < 0 && !HasEnded(poll, nowUnix);

    public static int CountAt(PostPollDto poll, int optionIndex)
    {
        var counts = poll.Counts;
        return counts is not null && optionIndex >= 0 && optionIndex < counts.Length ? counts[optionIndex] : 0;
    }

    public static PostPollDto ApplyLocalVote(PostPollDto poll, int optionIndex)
    {
        if (poll.MyVote >= 0 || optionIndex < 0 || optionIndex >= poll.Options.Length)
        {
            return poll;
        }

        int[]? counts = null;
        if (poll.Counts is { } current)
        {
            counts = (int[])current.Clone();
            if (optionIndex < counts.Length)
            {
                counts[optionIndex]++;
            }
        }

        return poll with { MyVote = optionIndex, TotalVotes = poll.TotalVotes + 1, Counts = counts };
    }

    public static PostPollDto Swap(PostPollDto current, PostPollDto expected, PostPollDto replacement) =>
        ReferenceEquals(current, expected) ? replacement : current;

    public static PostDto WithPoll(PostDto post, PostPollDto poll) =>
        ReferenceEquals(post.Poll, poll) ? post : post with { Poll = poll };
}
