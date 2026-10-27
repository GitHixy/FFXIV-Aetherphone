using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Social;
using Xunit;

namespace Aetherphone.Tests;

public sealed class TrendingTagListTests
{
    [Fact]
    public void TheRecentTagsTopUpTrendingWithoutRepeatingATag()
    {
        var trending = new[] { new TagSummaryDto("raid", 4, 4), new TagSummaryDto("glam", 2, 2) };
        var recent = new[]
        {
            new TagSummaryDto("glam", 2, 2),
            new TagSummaryDto("housing", 6, 1),
            new TagSummaryDto("gpose", 3, 0),
        };

        var merged = TrendingTagList.Merge(trending, recent);

        Assert.Equal(new[] { "raid", "glam", "housing", "gpose" }, merged.Select(tag => tag.Tag).ToArray());
        Assert.Same(trending[1], merged[1]);
    }

    [Fact]
    public void AnEmptyTrendingListFallsBackToTheRecentTags()
    {
        var recent = new[] { new TagSummaryDto("housing", 6, 1) };

        var merged = TrendingTagList.Merge(Array.Empty<TagSummaryDto>(), recent);

        Assert.Equal(new[] { "housing" }, merged.Select(tag => tag.Tag).ToArray());
    }
}
