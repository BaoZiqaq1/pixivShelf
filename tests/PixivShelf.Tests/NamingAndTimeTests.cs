using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class FileNamePolicyTests
{
    private static Artwork Artwork(int id = 123, string title = "test title", string artist = "artist") => new()
    {
        ArtworkId = id,
        Title = title,
        Artist = new Artist { MemberId = 456, Name = artist, Account = "acc" },
        CreateDate = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
        PageCount = 2,
        OriginalUrls = new[] { "https://i.pximg.net/img-original/img/123_p0.jpg" },
    };

    [Fact]
    public void Page_index_is_zero_based()
    {
        var path = FileNamePolicy.BuildTarget(@"C:\out", "%image_id%_p%page_index%", Artwork(), 1, "https://x/123_p0.png");
        Assert.EndsWith("123_p0.png", path);
    }

    [Fact]
    public void Page_number_has_p_prefix_and_is_zero_based()
    {
        var first = FileNamePolicy.BuildTarget(@"C:\out", "%page_number% - %image_id%", Artwork(), 1, "https://x/123_p0.png");
        var second = FileNamePolicy.BuildTarget(@"C:\out", "%page_number% - %image_id%", Artwork(), 2, "https://x/123_p0.png");

        Assert.EndsWith("p0 - 123.png", first);
        Assert.EndsWith("p1 - 123.png", second);
    }

    [Fact]
    public void Source_extension_is_appended()
    {
        var path = FileNamePolicy.BuildTarget(@"C:\out", "%image_id%", Artwork(), 1, "https://x/123_p0.webp");
        Assert.EndsWith("123.webp", path);
    }

    [Fact]
    public void Output_extension_overrides_source()
    {
        var path = FileNamePolicy.BuildTarget(@"C:\out", "%image_id%", Artwork(), 1, "https://x/123_p0.jpg", "png");
        Assert.EndsWith("123.png", path);
    }

    [Fact]
    public void Title_slashes_do_not_create_directories()
    {
        var path = FileNamePolicy.BuildTarget(@"C:\out", "%title%", Artwork(title: "26/02"), 1, "https://x/123_p0.jpg");
        var relative = Path.GetRelativePath(@"C:\out", path);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, relative);
        Assert.Contains("26_02", relative);
    }

    [Fact]
    public void Template_directories_are_preserved()
    {
        var path = FileNamePolicy.BuildTarget(@"C:\out", "%artist%/%image_id%", Artwork(), 1, "https://x/123_p0.jpg");
        var relative = Path.GetRelativePath(@"C:\out", path);
        Assert.Equal(Path.Combine("artist", "123.jpg"), relative);
    }
}

public class BookmarkTimeTests
{
    private static List<BookmarkPeriod> Periods() => new()
    {
        new BookmarkPeriod { Period = "2026-11", Count = 3, Year = 2026, Month = 11 },
        new BookmarkPeriod { Period = "2026-10", Count = 4, Year = 2026, Month = 10 },
    };

    [Fact]
    public void Maps_every_id_into_the_right_month()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8));
        var ordered = new List<int> { 11, 12, 13, 21, 22, 23, 24 };

        var mapping = BookmarkTime.Estimate(Periods(), ordered, now);

        Assert.Equal(7, mapping.Count);
        foreach (var id in new[] { 11, 12, 13 })
        {
            Assert.Equal(11, mapping[id].Month);
        }
        foreach (var id in new[] { 21, 22, 23, 24 })
        {
            Assert.Equal(10, mapping[id].Month);
        }
    }

    [Fact]
    public void Spreads_oldest_to_newest_inside_a_month()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8));
        var ordered = new List<int> { 11, 12, 13, 21, 22, 23, 24 };

        var mapping = BookmarkTime.Estimate(Periods(), ordered, now);

        // ordered is newest-first, so 13 is the oldest of the November batch
        Assert.True(mapping[13] < mapping[12]);
        Assert.True(mapping[12] < mapping[11]);
    }

    [Fact]
    public void Never_exceeds_now()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8));
        var mapping = BookmarkTime.Estimate(Periods(), new List<int> { 11, 12, 13 }, now);
        Assert.All(mapping.Values, value => Assert.True(value <= now));
    }

    [Fact]
    public void Handles_empty_input()
    {
        Assert.Empty(BookmarkTime.Estimate(new List<BookmarkPeriod>(), new List<int> { 1 }));
        Assert.Empty(BookmarkTime.Estimate(Periods(), new List<int>()));
    }
}

public class FileTimesTests
{
    [Fact]
    public void Sets_modified_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pixiv-times-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "x");
        try
        {
            var when = new DateTimeOffset(2020, 5, 17, 12, 0, 0, TimeSpan.Zero);
            FileTimes.Apply(path, when, creation: false, modified: true);
            Assert.True(Math.Abs((File.GetLastWriteTimeUtc(path) - when.UtcDateTime).TotalSeconds) < 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sets_creation_time_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"pixiv-ctime-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "x");
        try
        {
            var when = new DateTimeOffset(2019, 3, 4, 12, 0, 0, TimeSpan.Zero);
            FileTimes.Apply(path, when, creation: true, modified: true);
            Assert.True(Math.Abs((File.GetCreationTimeUtc(path) - when.UtcDateTime).TotalSeconds) < 2);
            Assert.True(Math.Abs((File.GetLastWriteTimeUtc(path) - when.UtcDateTime).TotalSeconds) < 2);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
