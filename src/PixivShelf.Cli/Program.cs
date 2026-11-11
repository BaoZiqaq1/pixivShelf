using System.Net.Http;
using System.Text.Json;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;

namespace PixivShelf.Cli;

/// <summary>
/// Engine harness: verifies the C# port without the UI.
///   pixivfetch-cli check
///   pixivfetch-cli preview --pid 123 --pid 456
///   pixivfetch-cli download --artist 123 --tag 初音ミク
///   pixivfetch-cli backfill --root "D:\pixiv" --dry-run --rename --zero-based
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var settings = AppSettings.Load();
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";

        try
        {
            return command switch
            {
                "check" => await CheckAsync(settings).ConfigureAwait(false),
                "preview" => await PreviewAsync(settings, args).ConfigureAwait(false),
                "download" => await DownloadAsync(settings, args).ConfigureAwait(false),
                "backfill" => BackfillCommand(settings, args),
                _ => Help(),
            };
        }
        catch (PixivException ex)
        {
            Console.Error.WriteLine($"[pixiv] {ex.Kind}: {ex.Message}");
            if (ex.Kind == PixivErrorKind.Network)
            {
                Console.Error.WriteLine("提示：请确认能正常访问 pixiv（可能需要代理），或在设置里调大超时/重试。");
            }
            return 2;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            Console.Error.WriteLine($"[网络] 无法连接 pixiv：{ex.Message}");
            Console.Error.WriteLine("提示：请检查网络/代理；也可在 config.json 里调大 Network.Timeout 与 Network.RetryWait。");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 4;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            pixivfetch-cli <command> [options]

            check                        登录验证 + 收藏月份统计
            preview [source]             只抓取，不下载
            download [source]            抓取并下载
            backfill --root <dir>        给已有文件回填时间/改名

            source 选项:
              --pid <id>                 可重复
              --artist <userId> [--tag]
              --bookmarks [--tag]

            其它:
              --limit <n>                最多处理多少个作品
              --dry-run                  不实际下载
              --rename --zero-based      backfill 时改名（页码减一）
            """);
        return 1;
    }

    private static async Task<int> CheckAsync(AppSettings settings)
    {
        using var client = new PixivClient(settings, Console.WriteLine);
        var userId = await client.ResolveSelfUserIdAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"登录成功，用户 ID = {userId}");

        var periods = await client.GetBookmarkPeriodsAsync(userId, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"收藏月份：{periods.Count} 个月，合计 {periods.Sum(p => p.Count)} 个");
        foreach (var period in periods.Take(5))
        {
            Console.WriteLine($"  {period.Period}: {period.Count}");
        }
        return 0;
    }

    private static async Task<int> PreviewAsync(AppSettings settings, string[] args)
    {
        var request = BuildRequest(args);
        var engine = new DownloadEngine(settings, Console.WriteLine);
        using var client = new PixivClient(settings, Console.WriteLine);
        var ids = await engine.CollectAsync(client, request, CancellationToken.None, Console.WriteLine).ConfigureAwait(false);
        Console.WriteLine($"共 {ids.Count} 个作品：{string.Join(", ", ids.Take(20))}{(ids.Count > 20 ? " …" : "")}");
        return 0;
    }

    private static async Task<int> DownloadAsync(AppSettings settings, string[] args)
    {
        var request = BuildRequest(args);
        var dryRun = args.Contains("--dry-run");
        var limit = GetOption(args, "--limit") is { } raw && int.TryParse(raw, out var parsed) ? parsed : 20;

        if (GetOption(args, "--root") is { } rootOverride && !string.IsNullOrWhiteSpace(rootOverride))
        {
            settings.Storage.DownloadRoot = rootOverride;
        }

        var engine = new DownloadEngine(settings, Console.WriteLine);
        using var client = new PixivClient(settings, Console.WriteLine);
        var ids = await engine.CollectAsync(client, request, CancellationToken.None, Console.WriteLine).ConfigureAwait(false);

        if (ids.Count > limit)
        {
            Console.WriteLine($"只处理前 {limit} 个（共 {ids.Count}）。");
            ids = ids.Take(limit).ToList();
        }

        if (dryRun)
        {
            Console.WriteLine($"[dry] 将下载 {ids.Count} 个作品到 {settings.Storage.DownloadRoot}");
            return 0;
        }

        var progress = new Progress<DownloadProgress>(p => Console.WriteLine($"  [{p.ArtworksDone}/{p.ArtworksTotal}] {p.Message}"));
        var result = await engine.RunAsync(request, ids, progress, CancellationToken.None, Console.WriteLine).ConfigureAwait(false);
        Console.WriteLine($"成功 {result.ImagesOk} 张，跳过 {result.ImagesSkipped} 张，失败 {result.ImagesFailed} 张。");
        return result.ImagesFailed > 0 ? 1 : 0;
    }

    private static int BackfillCommand(AppSettings settings, string[] args)
    {
        var root = GetOption(args, "--root") ?? settings.Storage.DownloadRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Console.Error.WriteLine($"目录不存在：{root}");
            return 2;
        }

        var files = Backfill.Scan(root);
        Console.WriteLine($"扫描到 {files.Count} 个符合命名的图片。");

        using var client = new PixivClient(settings, Console.WriteLine);
        var userId = client.ResolveSelfUserIdAsync(CancellationToken.None).GetAwaiter().GetResult();
        var periods = client.GetBookmarkPeriodsAsync(userId, CancellationToken.None).GetAwaiter().GetResult();
        var ordered = client.CollectBookmarkIdsAsync(userId, "", CancellationToken.None).GetAwaiter().GetResult();
        var times = BookmarkTime.Estimate(periods, ordered);

        var plan = Backfill.Plan(files, times);
        Console.WriteLine($"可回填 {plan.Count} 个，未匹配 {files.Count - plan.Count} 个。");

        var result = Backfill.Apply(
            plan,
            dryRun: args.Contains("--dry-run"),
            rename: args.Contains("--rename"),
            zeroBased: args.Contains("--zero-based"),
            log: Console.WriteLine);

        Console.WriteLine($"写入时间 {result.Times}，改名 {result.Renamed}，跳过 {result.Skipped}，失败 {result.Failed}。");
        return 0;
    }

    private static DownloadRequest BuildRequest(string[] args)
    {
        var request = new DownloadRequest { Tag = GetOption(args, "--tag") ?? "" };
        var pids = ValuesOf(args, "--pid").Select(int.Parse).ToList();
        if (pids.Count > 0)
        {
            request.Source = DownloadSource.Pid;
            request.Pids = pids;
            return request;
        }

        if (GetOption(args, "--artist") is { } artist && int.TryParse(artist, out var artistId))
        {
            request.Source = DownloadSource.Artist;
            request.ArtistId = artistId;
            return request;
        }

        request.Source = DownloadSource.Bookmarks;
        return request;
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IEnumerable<string> ValuesOf(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                yield return args[i + 1];
            }
        }
    }
}
