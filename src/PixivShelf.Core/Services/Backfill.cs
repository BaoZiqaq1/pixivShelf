using System.Text.RegularExpressions;

namespace PixivShelf.Core.Services;

public sealed record BackfillItem(string Path, int Pid, int Page, DateTimeOffset Target);

public sealed class BackfillResult
{
    public int Times { get; set; }
    public int Renamed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

/// <summary>Backfills bookmark times / renames existing files (the old tools steps 3-4).</summary>
public static class Backfill
{
    private static readonly Regex PidFirst = new(@"^(\d+)_p(\d+)$", RegexOptions.Compiled);
    private static readonly Regex PageThenPid = new(@"^.*_p(\d+)_(\d+)$", RegexOptions.Compiled);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
    };

    public static (int Pid, int Page)? ParseImageName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (!Extensions.Contains(ext))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = PidFirst.Match(stem);
        if (match.Success)
        {
            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }

        match = PageThenPid.Match(stem);
        if (match.Success)
        {
            return (int.Parse(match.Groups[2].Value), int.Parse(match.Groups[1].Value));
        }

        return null;
    }

    public static List<(string Path, int Pid, int Page)> Scan(string root)
    {
        var result = new List<(string, int, int)>();
        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var parsed = ParseImageName(path);
            if (parsed is not null)
            {
                result.Add((path, parsed.Value.Pid, parsed.Value.Page));
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return result;
    }

    public static List<BackfillItem> Plan(
        IEnumerable<(string Path, int Pid, int Page)> files,
        IReadOnlyDictionary<int, DateTimeOffset> times)
    {
        var plan = new List<BackfillItem>();
        foreach (var (path, pid, page) in files)
        {
            if (times.TryGetValue(pid, out var when))
            {
                plan.Add(new BackfillItem(path, pid, page, when));
            }
        }
        return plan;
    }

    public static BackfillResult Apply(
        IEnumerable<BackfillItem> plan,
        bool dryRun = false,
        bool rename = false,
        bool zeroBased = false,
        Action<string>? log = null)
    {
        var result = new BackfillResult();

        foreach (var item in plan)
        {
            try
            {
                if (!dryRun)
                {
                    FileTimes.Apply(item.Path, item.Target, creation: true, modified: true);
                }
                result.Times++;

                if (!rename)
                {
                    continue;
                }

                var page = zeroBased ? item.Page - 1 : item.Page;
                if (page < 0)
                {
                    result.Skipped++;
                    continue;
                }

                var directory = Path.GetDirectoryName(item.Path) ?? ".";
                var target = Path.Combine(directory, $"{item.Pid}_p{page}{Path.GetExtension(item.Path).ToLowerInvariant()}");
                if (string.Equals(target, item.Path, StringComparison.OrdinalIgnoreCase) || File.Exists(target))
                {
                    result.Skipped++;
                    continue;
                }

                if (dryRun)
                {
                    log?.Invoke($"[dry] {Path.GetFileName(item.Path)} → {Path.GetFileName(target)}");
                }
                else
                {
                    File.Move(item.Path, target);
                }
                result.Renamed++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                log?.Invoke($"处理失败：{item.Path}（{ex.Message}）");
            }
        }

        return result;
    }
}
