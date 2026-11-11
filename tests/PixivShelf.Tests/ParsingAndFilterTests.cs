using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class PasteParserTests
{
    [Theory]
    [InlineData("12345678", new[] { 12345678 })]
    [InlineData("https://www.pixiv.net/artworks/12345678", new[] { 12345678 })]
    [InlineData("https://www.pixiv.net/en/artworks/12345678#1", new[] { 12345678 })]
    [InlineData("https://www.pixiv.net/artworks/12345678?foo=1", new[] { 12345678 })]
    [InlineData("https://www.pixiv.net/users/123/artworks/456789", new[] { 456789 })]
    [InlineData("illust_id=12345678", new[] { 12345678 })]
    [InlineData("12345678\n87654321", new[] { 12345678, 87654321 })]
    [InlineData("12345678 12345678", new[] { 12345678 })]
    public void Parses_ids_and_links(string text, int[] expected)
    {
        var (ids, _) = PasteParser.Parse(text);
        Assert.Equal(expected, ids);
    }

    [Fact]
    public void Reports_rejected_tokens()
    {
        var (ids, rejected) = PasteParser.Parse("12345678 hello world");
        Assert.Equal(new[] { 12345678 }, ids);
        Assert.Equal(new[] { "hello", "world" }, rejected);
    }

    [Fact]
    public void Ignores_bookmark_page_url()
    {
        var (ids, rejected) = PasteParser.Parse("https://www.pixiv.net/users/123/bookmarks/artworks");
        Assert.Empty(ids);
        Assert.NotEmpty(rejected);
    }

    [Theory]
    [InlineData("1234567", 1234567)]
    [InlineData("https://www.pixiv.net/users/1234567", 1234567)]
    [InlineData("https://www.pixiv.net/users/1234567/artworks", 1234567)]
    [InlineData("https://www.pixiv.net/en/users/1234567", 1234567)]
    public void Parses_artist_ids(string text, int expected)
    {
        Assert.Equal(expected, PasteParser.ParseArtistId(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("https://www.pixiv.net/artworks/87654321")]
    public void Rejects_non_artist_text(string text)
    {
        Assert.Null(PasteParser.ParseArtistId(text));
    }
}

public class FilterPolicyTests
{
    private static Artwork Artwork(List<string>? tags = null, int xRestrict = 0, int aiType = 0) => new()
    {
        ArtworkId = 1,
        Title = "t",
        Artist = new Artist { MemberId = 1, Name = "a", Account = "a" },
        CreateDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        PageCount = 1,
        OriginalUrls = new[] { "https://example/1.png" },
        Tags = tags ?? new List<string>(),
        AiType = aiType,
        XRestrict = xRestrict,
    };

    [Fact]
    public void SkipR18_covers_r18g()
    {
        var filter = new FilterSettings { Scope = ContentScope.SkipR18 };
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "R-18" }), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "R-18G" }), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(xRestrict: 1), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(xRestrict: 2), filter));
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "オリジナル" }), filter));
    }

    [Fact]
    public void R18Only_keeps_only_r18()
    {
        var filter = new FilterSettings { Scope = ContentScope.R18Only };
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "R-18" }), filter));
        Assert.True(FilterPolicy.ShouldDownload(Artwork(xRestrict: 2), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "オリジナル" }), filter));
    }

    [Fact]
    public void All_scope_keeps_everything()
    {
        var filter = new FilterSettings { Scope = ContentScope.All };
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "R-18" }, xRestrict: 1), filter));
    }

    [Fact]
    public void Ai_filter_applies()
    {
        var filter = new FilterSettings { Scope = ContentScope.All, SkipAiGenerated = true };
        Assert.False(FilterPolicy.ShouldDownload(Artwork(aiType: 2), filter));
        Assert.True(FilterPolicy.ShouldDownload(Artwork(aiType: 0), filter));
    }

    [Fact]
    public void BlockR18G_drops_only_r18g()
    {
        var filter = new FilterSettings { Scope = ContentScope.All, BlockR18G = true };
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "オリジナル" }), filter));
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "R-18" }, xRestrict: 1), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "R-18G" }, xRestrict: 2), filter));
    }

    [Fact]
    public void BlockR18G_with_adult_scope_keeps_plain_r18()
    {
        var filter = new FilterSettings { Scope = ContentScope.R18Only, BlockR18G = true };
        Assert.True(FilterPolicy.ShouldDownload(Artwork(xRestrict: 1), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(xRestrict: 2), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "オリジナル" }), filter));
    }

    [Fact]
    public void BlockR18G_is_redundant_for_all_ages_scope()
    {
        var filter = new FilterSettings { Scope = ContentScope.SkipR18, BlockR18G = true };
        Assert.True(FilterPolicy.ShouldDownload(Artwork(new() { "オリジナル" }), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(xRestrict: 1), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(xRestrict: 2), filter));
    }

    [Fact]
    public void Tag_variants_are_normalized()
    {
        var filter = new FilterSettings { Scope = ContentScope.SkipR18 };
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { " r-18 " }), filter));
        Assert.False(FilterPolicy.ShouldDownload(Artwork(new() { "Ｒ－１８" }), filter));
    }
}
