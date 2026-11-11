using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class BookmarkRestTests
{
    [Fact]
    public void All_visits_both_rests()
    {
        Assert.Equal(new[] { "show", "hide" }, PixivClient.BookmarkRestsFor(BookmarkRest.All));
    }

    [Fact]
    public void Public_only_visits_show()
    {
        Assert.Equal(new[] { "show" }, PixivClient.BookmarkRestsFor(BookmarkRest.Public));
    }

    [Fact]
    public void Private_only_visits_hide()
    {
        Assert.Equal(new[] { "hide" }, PixivClient.BookmarkRestsFor(BookmarkRest.Private));
    }

    [Fact]
    public void Settings_default_to_all()
    {
        Assert.Equal(BookmarkRest.All, new FilterSettings().BookmarkRest);
    }
}
