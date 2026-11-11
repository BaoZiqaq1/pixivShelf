using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class FileNamePresetsTests
{
    [Fact]
    public void Every_preset_has_a_label_and_a_format()
    {
        Assert.NotEmpty(FileNamePresets.All);
        foreach (var (label, format) in FileNamePresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Contains("%", format);
        }
    }

    [Fact]
    public void Presets_are_unique()
    {
        Assert.Equal(FileNamePresets.All.Count, FileNamePresets.All.Select(p => p.Format).Distinct().Count());
        Assert.Equal(FileNamePresets.All.Count, FileNamePresets.All.Select(p => p.Label).Distinct().Count());
    }

    [Fact]
    public void Sample_replaces_every_token()
    {
        foreach (var (_, format) in FileNamePresets.All)
        {
            var sample = FileNamePresets.BuildSample(format);

            Assert.DoesNotContain("%", sample);
            Assert.EndsWith(".jpg", sample);
        }
    }

    [Fact]
    public void Sample_is_empty_for_blank_format()
    {
        Assert.Equal("", FileNamePresets.BuildSample(""));
        Assert.Equal("", FileNamePresets.BuildSample("   "));
    }
}

public class FilterEvaluateTests
{
    private static Artwork Artwork(List<string>? tags = null, int xRestrict = 0, int aiType = 0) => new()
    {
        ArtworkId = 7,
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
    public void Kept_artwork_has_no_reason()
    {
        var (keep, reason) = FilterPolicy.Evaluate(Artwork(), new FilterSettings());

        Assert.True(keep);
        Assert.Null(reason);
    }

    [Fact]
    public void Skipped_artwork_explains_why()
    {
        var filter = new FilterSettings { SkipAiGenerated = true };
        var (keep, reason) = FilterPolicy.Evaluate(Artwork(aiType: 2), filter);

        Assert.False(keep);
        Assert.Contains("AI", reason);
    }
}
