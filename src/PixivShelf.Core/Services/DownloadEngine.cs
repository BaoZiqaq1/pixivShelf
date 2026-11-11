using System.Collections.Concurrent;
using System.Threading.Channels;
using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

public sealed class RunResult
{
    public int ImagesOk { get; set; }
    public int ImagesSkipped { get; set; }
    public int ImagesFailed { get; set; }
    public int ArtworksDone { get; set; }
    public List<string> Errors { get; } = new();
}

public sealed class DownloadEngine
{
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;

    public DownloadEngine(AppSettings settings, Action<string>? log = null)
    {
        _settings = settings;
        _log = log;
    }

    // ------------------------------------------------------------------ //
    // collection
    // ------------------------------------------------------------------ //
    public async Task<List<int>> CollectAsync(DownloadRequest request, CancellationToken ct, Action<string>? log = null)
    {
        using var client = new PixivClient(_settings, _log);
        return await CollectAsync(client, request, ct, log).ConfigureAwait(false);
    }

    public async Task<List<int>> CollectAsync(PixivClient client, DownloadRequest request, CancellationToken ct, Action<string>? log = null)
    {
        switch (request.Source)
        {
            case DownloadSource.Pid:
                return request.Pids.Distinct().ToList();

            case DownloadSource.Artist:
                if (request.ArtistId is null or <= 0)
                {
                    throw new ArgumentException("请先填写画师 ID。");
                }
                log?.Invoke($"抓取画师 {request.ArtistId} 的作品…");
                var artistIds = await client.CollectArtistIdsAsync(request.ArtistId.Value, request.Tag, ct).ConfigureAwait(false);
                log?.Invoke($"画师作品抓取完成：{artistIds.Count} 个。");
                return artistIds;

            case DownloadSource.Bookmarks:
                log?.Invoke("抓取收藏列表…");
                var userId = await client.ResolveSelfUserIdAsync(ct).ConfigureAwait(false);
                var bookmarkIds = await client.CollectBookmarkIdsAsync(userId, request.Tag, ct, log, request.BookmarkRest).ConfigureAwait(false);
                log?.Invoke($"收藏抓取完成：{bookmarkIds.Count} 个。");
                return bookmarkIds;

            default:
                return new List<int>();
        }
    }

    // ------------------------------------------------------------------ //
    // download
    // ------------------------------------------------------------------ //
    public async Task<RunResult> RunAsync(
        DownloadRequest request,
        IReadOnlyList<int> ids,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct,
        Action<string>? log = null,
        Action<int>? onArtworkCompleted = null)
    {
        var result = new RunResult();
        using var client = new PixivClient(_settings, _log);
        var workers = Math.Clamp(_settings.Network.MaxWorkers, 1, 8);
        var lang = "en";

        Dictionary<int, DateTimeOffset> bookmarkTimes = new();
        if (_settings.Storage.FileTimeSource == FileTimeSource.Bookmark && request.Source == DownloadSource.Bookmarks && ids.Count > 0)
        {
            try
            {
                var userId = await client.ResolveSelfUserIdAsync(ct).ConfigureAwait(false);
                var periods = await client.GetBookmarkPeriodsAsync(userId, ct).ConfigureAwait(false);
                var ordered = await client.CollectBookmarkIdsAsync(userId, request.Tag, ct).ConfigureAwait(false);
                bookmarkTimes = BookmarkTime.Estimate(periods, ordered);
                log?.Invoke($"收藏月份推算完成：{bookmarkTimes.Count} 个作品。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log?.Invoke($"收藏月份获取失败，回退为投稿时间：{ex.Message}");
            }
        }

        var channel = Channel.CreateBounded<(Artwork Artwork, int Page)>(new BoundedChannelOptions(Math.Max(2, workers * 2))
        {
            SingleWriter = true,
            SingleReader = false,
        });

        var failed = 0;
        var skipped = 0;
        var ok = 0;
        var artworksDone = 0;
        var errors = new ConcurrentBag<string>();
        // Remaining page count per artwork, so an artwork counts as done only when
        // every one of its pages has been processed.
        var pendingPages = new ConcurrentDictionary<int, int>();

        void CompletePage(int artworkId)
        {
            if (!pendingPages.TryGetValue(artworkId, out _))
            {
                return;
            }

            var left = pendingPages.AddOrUpdate(artworkId, 0, (_, value) => value - 1);
            if (left <= 0)
            {
                pendingPages.TryRemove(artworkId, out _);
                Interlocked.Increment(ref artworksDone);
                onArtworkCompleted?.Invoke(artworkId);
                emit(Volatile.Read(ref artworksDone), $"完成 {artworkId}");
            }
        }

        // producers write downloads, one artwork at a time (keeps filters + logs deterministic)
        var producer = Task.Run(async () =>
        {
            emit(0, "开始解析作品…");
            foreach (var artworkId in ids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var artwork = await client.GetArtworkAsync(artworkId, lang, ct).ConfigureAwait(false);
                    if (!FilterPolicy.ShouldDownload(artwork, _settings.Filter))
                    {
                        log?.Invoke(FilterPolicy.SkipReason(artwork, _settings.Filter) ?? $"跳过作品 {artworkId}");
                        Interlocked.Increment(ref artworksDone);
                        onArtworkCompleted?.Invoke(artwork.ArtworkId);
                        emit(Volatile.Read(ref artworksDone), $"跳过 {artwork.Title}");
                        continue;
                    }

                    pendingPages[artwork.ArtworkId] = artwork.OriginalUrls.Count;
                    for (var page = 1; page <= artwork.OriginalUrls.Count; page++)
                    {
                        await channel.Writer.WriteAsync((artwork, page), ct).ConfigureAwait(false);
                    }

                    if (artwork.OriginalUrls.Count == 0)
                    {
                        pendingPages.TryRemove(artwork.ArtworkId, out _);
                        Interlocked.Increment(ref artworksDone);
                        onArtworkCompleted?.Invoke(artwork.ArtworkId);
                        emit(Volatile.Read(ref artworksDone), $"无图片 {artwork.Title}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PixivException ex) when (ex.Kind is PixivErrorKind.Auth)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{artworkId}: {ex.Message}");
                    log?.Invoke($"跳过作品 {artworkId}：{ex.Message}");
                    Interlocked.Increment(ref artworksDone);
                    onArtworkCompleted?.Invoke(artworkId);
                    emit(Volatile.Read(ref artworksDone), $"失败 {artworkId}");
                }
            }

            channel.Writer.Complete();
        }, ct);

        // consumers download images concurrently
        var consumers = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            await foreach (var (artwork, page) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                var url = artwork.OriginalUrls[page - 1];
                var target = FileNamePolicy.BuildTarget(
                    _settings.Storage.DownloadRoot,
                    _settings.Storage.FileNameFormat,
                    artwork,
                    page,
                    url);

                // Skip existing files before the rate-limit delay: resuming a big
                // task should not wait one second per already-downloaded page.
                if (!_settings.Storage.Overwrite && File.Exists(target))
                {
                    Interlocked.Increment(ref skipped);
                    log?.Invoke($"已存在，跳过：{Path.GetFileName(target)}");
                    CompletePage(artwork.ArtworkId);
                    continue;
                }

                if (_settings.Network.DownloadDelay > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Network.DownloadDelay), ct).ConfigureAwait(false);
                }

                try
                {
                    var downloaded = await client
                        .DownloadAsync(url, $"https://www.pixiv.net/artworks/{artwork.ArtworkId}", target, _settings.Storage.Overwrite, ct)
                        .ConfigureAwait(false);

                    if (downloaded)
                    {
                        ApplyFileTime(target, artwork, bookmarkTimes);
                        Interlocked.Increment(ref ok);
                        log?.Invoke($"下载完成：{artwork.ArtworkId} 第{page}张 → {Path.GetFileName(target)}");
                    }
                    else
                    {
                        Interlocked.Increment(ref skipped);
                        log?.Invoke($"已存在，跳过：{Path.GetFileName(target)}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PixivException ex) when (ex.Kind is PixivErrorKind.Auth)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    errors.Add($"{artwork.ArtworkId}: {ex.Message}");
                    log?.Invoke($"下载失败 {artwork.ArtworkId} 第{page}张：{ex.Message}");
                }

                CompletePage(artwork.ArtworkId);
            }
        }, ct)).ToArray();

        try
        {
            await producer.ConfigureAwait(false);
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        result.ImagesOk = ok;
        result.ImagesSkipped = skipped;
        result.ImagesFailed = failed;
        result.ArtworksDone = Volatile.Read(ref artworksDone);
        result.Errors.AddRange(errors);
        log?.Invoke($"任务结束：成功 {ok} 张，跳过 {skipped} 张，失败 {failed} 张。");
        return result;

        void ApplyFileTime(string path, Artwork artwork, IReadOnlyDictionary<int, DateTimeOffset> times)
        {
            var when = artwork.CreateDate;
            if (_settings.Storage.FileTimeSource == FileTimeSource.Bookmark &&
                times.TryGetValue(artwork.ArtworkId, out var bookmarkWhen))
            {
                var earliest = artwork.CreateDate.AddHours(1);
                when = bookmarkWhen < earliest ? earliest : bookmarkWhen;
                if (when > DateTimeOffset.Now)
                {
                    when = DateTimeOffset.Now;
                }
            }

            try
            {
                FileTimes.Apply(path, when, _settings.Storage.SetCreationTime, modified: true);
            }
            catch (Exception ex)
            {
                log?.Invoke($"写入文件时间失败：{Path.GetFileName(path)}（{ex.Message}）");
            }
        }

        void emit(int artworksDone, string message)
        {
            progress?.Report(new DownloadProgress
            {
                ArtworksTotal = ids.Count,
                ArtworksDone = artworksDone,
                ImagesDone = Volatile.Read(ref ok),
                ImagesFailed = Volatile.Read(ref failed),
                Message = message,
            });
        }
    }
}
