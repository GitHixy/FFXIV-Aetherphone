using System.Text.Json;
using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Gifs;
using Xunit;

namespace Aetherphone.Tests;

public sealed class KlipyRequestTests
{
    private static readonly GifSettings Settings = new("app/key", "c0ffee", "high");

    [Fact]
    public void TrendingCarriesThePageTheCustomerTheLocaleAndTheStrictFilter()
    {
        Assert.Equal("https://api.klipy.com/api/v1/app%2Fkey/gifs/trending?page=2&per_page=24&customer_id=c0ffee"
                     + "&locale=br&content_filter=high&format_filter=gif,webp",
            KlipyUrls.Trending(Settings, 2, "br"));
    }

    [Fact]
    public void SearchEscapesTheQueryAndNeverAsksForPageZero()
    {
        Assert.Equal("https://api.klipy.com/api/v1/app%2Fkey/gifs/search?q=happy%20cat%20%26%20dog&page=1"
                     + "&per_page=24&customer_id=c0ffee&locale=us&content_filter=high&format_filter=gif,webp",
            KlipyUrls.Search(Settings, "happy cat & dog", 0, "us"));
    }

    [Fact]
    public void ShareTargetsTheSlug()
    {
        Assert.Equal("https://api.klipy.com/api/v1/app%2Fkey/gifs/share/hello%20hi", KlipyUrls.Share(Settings, "hello hi"));
    }

    [Fact]
    public void TheShareBodyUsesKlipysFieldNames()
    {
        var json = JsonSerializer.Serialize(new KlipyShareRequest("c0ffee", "cats"),
            KlipyJsonContext.Default.KlipyShareRequest);

        Assert.Equal("{\"customer_id\":\"c0ffee\",\"q\":\"cats\"}", json);
    }

    [Theory]
    [InlineData("en-US", "us")]
    [InlineData("pt-BR", "br")]
    [InlineData("ja-JP", "jp")]
    [InlineData("zh-CN", "cn")]
    [InlineData("de-DE", "de")]
    [InlineData("en", "us")]
    [InlineData("", "us")]
    [InlineData("sr-Latn", "us")]
    public void TheLocaleIsTheCulturesCountry(string cultureName, string expected)
    {
        Assert.Equal(expected, KlipyUrls.LocaleOf(cultureName));
    }

    [Fact]
    public void SettingsComeOnlyFromACompleteEnabledConfig()
    {
        Assert.Equal(new GifSettings("k", "c", "high"), GifSettings.From(new GifConfigDto(true, "k", "c", "high")));
        Assert.Null(GifSettings.From(null));
        Assert.Null(GifSettings.From(new GifConfigDto(false, "k", "c", "high")));
        Assert.Null(GifSettings.From(new GifConfigDto(true, null, "c", "high")));
        Assert.Null(GifSettings.From(new GifConfigDto(true, "k", " ", "high")));
        Assert.Null(GifSettings.From(new GifConfigDto(true, "k", "c", null)));
    }

    [Fact]
    public void TheServerConfigParsesWhetherTheFeatureIsOnOrOff()
    {
        var on = JsonSerializer.Deserialize("{\"enabled\":true,\"appKey\":\"k\",\"customerId\":\"c\",\"contentFilter\":\"high\"}",
            AethernetJsonContext.Default.GifConfigDto);
        var off = JsonSerializer.Deserialize("{\"enabled\":false}", AethernetJsonContext.Default.GifConfigDto);

        Assert.Equal(new GifConfigDto(true, "k", "c", "high"), on);
        Assert.Equal(new GifConfigDto(false), off);
    }

    [Fact]
    public void GifPostsAndRepliesSendTheUrlInGifUrl()
    {
        var post = JsonSerializer.Serialize(
            new CreatePostRequest(string.Empty, "quoted", MediaWidth: 498, MediaHeight: 280, GifUrl: "https://static.klipy.com/a.gif"),
            AethernetJsonContext.Default.CreatePostRequest);
        var reply = JsonSerializer.Serialize(
            new CreateCommentRequest("hi", MediaWidth: 220, MediaHeight: 124, GifUrl: "https://static.klipy.com/b.gif"),
            AethernetJsonContext.Default.CreateCommentRequest);

        using var postDocument = JsonDocument.Parse(post);
        using var replyDocument = JsonDocument.Parse(reply);
        Assert.Equal("https://static.klipy.com/a.gif", postDocument.RootElement.GetProperty("gifUrl").GetString());
        Assert.Equal("quoted", postDocument.RootElement.GetProperty("quotedPostId").GetString());
        Assert.Equal(498, postDocument.RootElement.GetProperty("mediaWidth").GetInt32());
        Assert.Equal("https://static.klipy.com/b.gif", replyDocument.RootElement.GetProperty("gifUrl").GetString());
        Assert.Equal(JsonValueKind.Null, replyDocument.RootElement.GetProperty("mediaKey").ValueKind);
    }
}
