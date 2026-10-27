using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Social;

// Trending only names tags at least two accounts used, so a quiet week can leave it nearly empty while
// tags still exist. Below Minimum the list is topped up from the recent tags search, the source the
// trending lists read before /tags/trending, keeping the real trending tags first.
internal static class TrendingTagList
{
    public const int Minimum = 3;

    public static TagSummaryDto[] Merge(TagSummaryDto[] trending, TagSummaryDto[] recent)
    {
        var merged = new List<TagSummaryDto>(trending.Length + recent.Length);
        merged.AddRange(trending);
        for (var recentIndex = 0; recentIndex < recent.Length; recentIndex++)
        {
            if (!Contains(trending, recent[recentIndex].Tag))
            {
                merged.Add(recent[recentIndex]);
            }
        }

        return merged.ToArray();
    }

    private static bool Contains(TagSummaryDto[] tags, string tag)
    {
        for (var index = 0; index < tags.Length; index++)
        {
            if (string.Equals(tags[index].Tag, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
