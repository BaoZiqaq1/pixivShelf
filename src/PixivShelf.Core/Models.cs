using System.Text;
using System.Text.Json.Serialization;

namespace PixivShelf.Core.Models;

/// <summary>0 = all-ages, 1 = R-18, 2 = R-18G. Authoritative rating from the API.</summary>
public enum ContentScope
{
    All = 0,
    R18Only = 1,
    SkipR18 = 2,
}

public enum FileTimeSource
{
    Publish = 0,
    Bookmark = 1,
}

public enum DownloadSource
{
    Pid = 0,
    Artist = 1,
    Bookmarks = 2,
}

/// <summary>Which bookmarks to pull: public, private, or both.</summary>
public enum BookmarkRest
{
    All = 0,
    Public = 1,
    Private = 2,
}

public enum DownloadTaskStatus
{
    Pending,
    Collecting,
    Running,
    Paused,
    Done,
    Cancelled,
    Failed,
}

public sealed class Artist
{
    public int MemberId { get; init; }
    public string Name { get; init; } = "";
    public string Account { get; init; } = "";
}

public sealed class Artwork
{
    public int ArtworkId { get; init; }
    public string Title { get; init; } = "";
    public Artist Artist { get; init; } = new();
    public DateTimeOffset CreateDate { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<string> OriginalUrls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public int BookmarkCount { get; init; }
    public int AiType { get; init; } = -1;
    public int XRestrict { get; init; }

    public bool IsR18G => XRestrict >= 2 || Tags.Any(t => Normalize(t) == "R-18G");

    // R-18G is stricter than R-18, so it also counts as R-18.
    public bool IsR18 => IsR18G || XRestrict >= 1 || Tags.Any(t => Normalize(t) == "R-18");

    private static string Normalize(string tag) => tag.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}

public sealed class BookmarkPeriod
{
    public string Period { get; init; } = "";
    public int Count { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
}

public sealed class DownloadRequest
{
    public DownloadSource Source { get; set; } = DownloadSource.Pid;
    public List<int> Pids { get; set; } = new();
    public int? ArtistId { get; set; }
    public string Tag { get; set; } = "";
    public BookmarkRest BookmarkRest { get; set; } = BookmarkRest.All;

    [JsonIgnore]
    public string Title => Source switch
    {
        DownloadSource.Pid => $"PID · {Pids.Count} 个作品",
        DownloadSource.Artist => $"画师 · {ArtistId}",
        _ => "收藏 · 我的",
    };
}

public sealed class DownloadProgress
{
    public int ArtworksTotal { get; init; }
    public int ArtworksDone { get; init; }
    public int ImagesDone { get; init; }
    public int ImagesFailed { get; init; }
    public string Message { get; init; } = "";
    public int? CurrentArtworkId { get; init; }
}

public sealed class TaskRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "";
    public DownloadSource Source { get; set; }
    public string ParamsJson { get; set; } = "{}";
    public DownloadTaskStatus Status { get; set; } = DownloadTaskStatus.Pending;
    public int Total { get; set; }
    public int Done { get; set; }
    public int Failed { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }

    [JsonIgnore]
    public double Progress => Total <= 0 ? 0 : Math.Min(1.0, (double)Done / Total);
}

public sealed class TaskItemRecord
{
    public string TaskId { get; init; } = "";
    public int ArtworkId { get; init; }
    public string Title { get; init; } = "";
    public string Status { get; init; } = "pending";
    public string Path { get; init; } = "";
    public string Error { get; init; } = "";
}
