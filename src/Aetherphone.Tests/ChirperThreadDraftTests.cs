using Aetherphone.Apps.Chirper;
using Aetherphone.Core.Gifs;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ChirperThreadDraftTests
{
    private static GifPick Gif(string url) => new(new GifItem("slug", url, 10, 10, url, 20, 20), "wave");

    [Fact]
    public void AFollowUpWithOnlyPhotosCountsAsWritten()
    {
        var draft = new ChirperThreadDraft();
        draft.Add();
        Assert.False(draft.AllWritten(500));

        draft.Attachments(0).Add("C:/photos/one.png");

        Assert.True(draft.AllWritten(500));
        Assert.True(draft.HasPhotos());
        Assert.True(draft.Holds("C:/photos/one.png"));
    }

    [Fact]
    public void RemovingABoxCarriesTheNextBoxesMediaAlong()
    {
        var draft = new ChirperThreadDraft();
        draft.Add();
        draft.Add();
        draft.Add();
        draft.Text(0) = "first";
        draft.Attachments(0).Add("C:/photos/first.png");
        draft.Text(1) = "second";
        draft.AttachGif(1, Gif("https://static.klipy.com/a/wave.gif"));
        draft.Text(2) = "third";

        draft.RemoveAt(0);

        Assert.Equal(2, draft.Count);
        Assert.Equal("second", draft.Text(0));
        Assert.Equal("https://static.klipy.com/a/wave.gif", draft.AttachedGif(0)!.Item.Url);
        Assert.Equal("third", draft.Text(1));
        Assert.Empty(draft.Attachments(1));
        Assert.Null(draft.AttachedGif(1));
        Assert.False(draft.Holds("C:/photos/first.png"));
        Assert.False(draft.HasPhotos());
    }

    [Fact]
    public void AGifStopsCountingOnceAPhotoReplacesIt()
    {
        var draft = new ChirperThreadDraft();
        draft.Add();
        draft.AttachGif(0, Gif("https://static.klipy.com/b/dance.gif"));
        Assert.NotNull(draft.GifFor("https://static.klipy.com/b/dance.gif"));

        draft.Attachments(0).Clear();
        draft.Attachments(0).Add("C:/photos/still.png");

        Assert.Null(draft.AttachedGif(0));
        Assert.Null(draft.GifFor("https://static.klipy.com/b/dance.gif"));
        Assert.True(draft.HasPhotos());
    }
}
