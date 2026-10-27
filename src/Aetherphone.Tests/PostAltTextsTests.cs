using Aetherphone.Core.Media;
using Xunit;

namespace Aetherphone.Tests;

public sealed class PostAltTextsTests
{
    [Fact]
    public void DraftsLineUpWithThePhotoOrder()
    {
        var drafts = new Dictionary<string, string>(StringComparer.Ordinal) { ["b.png"] = "second photo" };

        var aligned = PostAltTexts.Aligned(new[] { "a.png", "b.png", "c.png" }, drafts);

        Assert.Equal(new[] { string.Empty, "second photo", string.Empty }, aligned);
    }

    [Fact]
    public void NoDescriptionsSendsNothing()
    {
        var drafts = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Null(PostAltTexts.Aligned(new[] { "a.png" }, drafts));

        drafts["gone.png"] = "a photo that was removed";
        Assert.Null(PostAltTexts.Aligned(new[] { "a.png" }, drafts));
    }

    [Fact]
    public void AnEmptyDraftClearsThePhotoAndPruneDropsRemovedPhotos()
    {
        var drafts = new Dictionary<string, string>(StringComparer.Ordinal);
        PostAltTexts.Set(drafts, "a.png", "kept");
        PostAltTexts.Set(drafts, "b.png", "removed later");
        PostAltTexts.Set(drafts, "c.png", "cleared");
        PostAltTexts.Set(drafts, "c.png", string.Empty);

        PostAltTexts.Prune(drafts, new List<string> { "a.png" });

        Assert.Equal("kept", Assert.Single(drafts).Value);
    }

    [Fact]
    public void SameComparesEveryPhotoSlot()
    {
        Assert.True(PostAltTexts.Same(null, new[] { string.Empty, string.Empty }, 2));
        Assert.True(PostAltTexts.Same(new[] { "one" }, new[] { "one", string.Empty }, 2));
        Assert.False(PostAltTexts.Same(new[] { "one" }, new[] { "one", "two" }, 2));
        Assert.Equal(string.Empty, PostAltTexts.At(new[] { "one" }, 3));
    }
}
