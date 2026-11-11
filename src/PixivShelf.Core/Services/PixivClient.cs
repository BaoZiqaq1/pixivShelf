using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

public enum PixivErrorKind
{
    Network,
    Auth,
    Api,
    NotFound,
}

public sealed class PixivException : Exception
{
    public PixivErrorKind Kind { get; }

    public PixivException(PixivErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

/// <summary>Thin pixiv web client: JSON endpoints plus image downloads.</summary>
public sealed class PixivClient : IDisposable
{
    private const string Base = "https://www.pixiv.net";

    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly Action<string>? _log;

    public PixivClient(AppSettings settings, Action<string>? log = null)
    {
        _settings = settings;
        _log = log;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.Network.Timeout)),
        };

        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.Network.UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,ja;q=0.8");
        if (!string.IsNullOrWhiteSpace(settings.Auth.Cookie))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Auth.Cookie.Trim());
        }
    }

    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------------ //
    // low level
    // ------------------------------------------------------------------ //
    private async Task<HttpResponseMessage> SendAsync(string url, string? referer, bool stream, CancellationToken ct)
    {
        var attempts = Math.Max(0, _settings.Network.Retry) + 1;
        Exception? last = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    request.Headers.TryAddWithoutValidation("Referer", referer);
                }

                var response = await _http
                    .SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    response.Dispose();
                    throw new PixivException(PixivErrorKind.Auth, $"认证失败（HTTP {(int)response.StatusCode}），请重新填写 cookie。");
                }

                if ((int)response.StatusCode == 429)
                {
                    response.Dispose();
                    throw new PixivException(PixivErrorKind.Network, "请求过于频繁（HTTP 429），正在等待后重试。");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    throw new PixivException(PixivErrorKind.NotFound, "作品不存在或不可访问（HTTP 404）。");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    response.Dispose();
                    throw new PixivException(PixivErrorKind.Api, $"HTTP {(int)response.StatusCode}: {Trim(text)}");
                }

                return response;
            }
            catch (PixivException ex) when (ex.Kind is PixivErrorKind.Auth or PixivErrorKind.NotFound)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt >= attempts - 1)
                {
                    break;
                }

                var wait = Math.Max(1, _settings.Network.RetryWait) * (attempt + 1);
                _log?.Invoke($"网络错误（{ex.Message}），{wait} 秒后重试（{attempt + 1}/{_settings.Network.Retry}）。");
                await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
            }
        }

        throw new PixivException(PixivErrorKind.Network, $"请求失败：{last?.Message}", last);
    }

    private static string Trim(string text) => text.Length <= 300 ? text : text[..300];

    public async Task<JsonDocument> GetJsonAsync(string url, string? referer, CancellationToken ct)
    {
        using var response = await SendAsync(url, referer, stream: false, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
            {
                var message = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                document.Dispose();
                throw new PixivException(PixivErrorKind.Api, $"pixiv 接口返回错误：{message}");
            }
            return document;
        }
        catch (JsonException ex)
        {
            throw new PixivException(PixivErrorKind.Api, $"接口返回了非法 JSON：{Trim(payload)}", ex);
        }
    }

    // ------------------------------------------------------------------ //
    // account
    // ------------------------------------------------------------------ //
    public async Task<int> ResolveSelfUserIdAsync(CancellationToken ct)
    {
        using (var response = await _http.GetAsync(Base, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            if (response.Headers.TryGetValues("x-userid", out var values))
            {
                var raw = values.FirstOrDefault();
                if (int.TryParse(raw, out var uid) && uid > 0)
                {
                    return uid;
                }
            }
        }

        using var document = await GetJsonAsync($"{Base}/ajax/user/self", Base, ct).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("userData", out var userData) &&
            userData.TryGetProperty("id", out var idElement))
        {
            var text = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
            if (int.TryParse(text, out var id))
            {
                return id;
            }
        }

        throw new PixivException(PixivErrorKind.Auth, "无法识别当前登录账号，请检查 cookie 是否有效。");
    }

    /// <summary>Reads the display name for an account (empty string when unavailable).</summary>
    public async Task<string> GetUserNameAsync(int userId, CancellationToken ct)
    {
        using var document = await GetJsonAsync(
            $"{Base}/ajax/user/{userId}?lang=zh",
            $"{Base}/users/{userId}",
            ct).ConfigureAwait(false);

        if (document.RootElement.TryGetProperty("body", out var body) &&
            body.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? "";
        }

        return "";
    }

    // ------------------------------------------------------------------ //
    // artwork
    // ------------------------------------------------------------------ //
    public async Task<Artwork> GetArtworkAsync(int artworkId, string lang, CancellationToken ct)
    {
        var url = $"{Base}/ajax/illust/{artworkId}?lang={Uri.EscapeDataString(lang)}";
        using var document = await GetJsonAsync(url, $"{Base}/artworks/{artworkId}", ct).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("body", out var body))
        {
            throw new PixivException(PixivErrorKind.Api, "作品详情缺少 body。");
        }

        var pageCount = GetInt(body, "pageCount", 1);
        var original = body.GetProperty("urls").GetProperty("original").GetString() ?? "";
        var urls = new List<string>();
        if (pageCount <= 1)
        {
            urls.Add(original);
        }
        else
        {
            for (var i = 0; i < pageCount; i++)
            {
                urls.Add(original.Replace("_p0", $"_p{i}"));
            }
        }

        var tags = new List<string>();
        if (body.TryGetProperty("tags", out var tagsElement) && tagsElement.TryGetProperty("tags", out var tagArray))
        {
            foreach (var tag in tagArray.EnumerateArray())
            {
                var value = tag.TryGetProperty("tag", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(value))
                {
                    tags.Add(value);
                }
            }
        }

        var createRaw = body.TryGetProperty("createDate", out var cd) ? cd.GetString() : null;
        var createDate = DateTimeOffset.TryParse(createRaw, out var parsed) ? parsed : DateTimeOffset.Now;

        return new Artwork
        {
            ArtworkId = artworkId,
            Title = body.TryGetProperty("illustTitle", out var title) ? title.GetString() ?? "" : "",
            Artist = new Artist
            {
                MemberId = GetInt(body, "userId", 0),
                Name = body.TryGetProperty("userName", out var name) ? name.GetString() ?? "" : "",
                Account = body.TryGetProperty("userAccount", out var account) ? account.GetString() ?? "" : "",
            },
            CreateDate = createDate,
            PageCount = pageCount,
            OriginalUrls = urls,
            Tags = tags,
            BookmarkCount = GetInt(body, "bookmarkCount", 0),
            AiType = GetInt(body, "aiType", -1),
            XRestrict = GetInt(body, "xRestrict", 0),
        };
    }

    // ------------------------------------------------------------------ //
    // collections
    // ------------------------------------------------------------------ //
    public async Task<List<BookmarkPeriod>> GetBookmarkPeriodsAsync(int userId, CancellationToken ct)
    {
        var url = $"{Base}/ajax/user/{userId}/illusts/bookmarks/periods?rest=show&lang=zh";
        using var document = await GetJsonAsync(url, $"{Base}/users/{userId}/bookmarks/artworks", ct).ConfigureAwait(false);

        var result = new List<BookmarkPeriod>();
        if (!document.RootElement.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in body.EnumerateArray())
        {
            var period = item.TryGetProperty("period", out var p) ? p.GetString() ?? "" : "";
            var parts = period.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month))
            {
                continue;
            }

            var count = GetInt(item, "count", 0);
            if (count <= 0)
            {
                continue;
            }

            result.Add(new BookmarkPeriod { Period = period, Count = count, Year = year, Month = month });
        }

        return result;
    }

    /// <summary>Maps the visibility choice to the pixiv "rest" query values to walk.</summary>
    public static IReadOnlyList<string> BookmarkRestsFor(BookmarkRest visibility) => visibility switch
    {
        BookmarkRest.Public => new[] { "show" },
        BookmarkRest.Private => new[] { "hide" },
        _ => new[] { "show", "hide" },
    };

    public async Task<List<int>> CollectBookmarkIdsAsync(
        int userId,
        string tag,
        CancellationToken ct,
        Action<string>? log = null,
        BookmarkRest visibility = BookmarkRest.All)
    {
        var ids = new List<int>();
        var seen = new HashSet<int>();

        var rests = BookmarkRestsFor(visibility);

        foreach (var rest in rests)
        {
            var offset = 0;
            const int limit = 100;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var url = $"{Base}/ajax/user/{userId}/illusts/bookmarks?tag={Uri.EscapeDataString(tag ?? "")}" +
                          $"&offset={offset}&limit={limit}&rest={rest}&order=desc&mode=all&lang=zh";
                using var document = await GetJsonAsync(url, $"{Base}/users/{userId}/bookmarks/artworks", ct).ConfigureAwait(false);

                if (!document.RootElement.TryGetProperty("body", out var body) ||
                    !body.TryGetProperty("works", out var works) ||
                    works.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;
                foreach (var work in works.EnumerateArray())
                {
                    if (work.TryGetProperty("isAdContainer", out var ad) && ad.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    var raw = work.TryGetProperty("id", out var id) ? id.GetRawText().Trim('"') : "";
                    if (!int.TryParse(raw, out var artworkId))
                    {
                        continue;
                    }

                    pageCount++;
                    if (seen.Add(artworkId))
                    {
                        ids.Add(artworkId);
                    }
                }

                log?.Invoke($"抓取收藏列表：{ids.Count} 个（{(rest == "show" ? "公开" : "非公开")}，offset {offset}）");
                if (pageCount == 0)
                {
                    break;
                }

                offset += limit;
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }

        return ids;
    }

    public async Task<List<int>> CollectArtistIdsAsync(int artistId, string tag, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();
            var page = 1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var url = $"{Base}/ajax/user/{artistId}/illustmanga/tag?tag={Uri.EscapeDataString(tag)}&offset={(page - 1) * 48}&limit=48";
                using var document = await GetJsonAsync(url, $"{Base}/users/{artistId}", ct).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("body", out var body) ||
                    !body.TryGetProperty("works", out var works) ||
                    works.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var count = 0;
                foreach (var work in works.EnumerateArray())
                {
                    var raw = work.TryGetProperty("id", out var id) ? id.GetRawText().Trim('"') : "";
                    if (int.TryParse(raw, out var artworkId) && seen.Add(artworkId))
                    {
                        ids.Add(artworkId);
                        count++;
                    }
                }

                if (count == 0)
                {
                    break;
                }

                page++;
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            return ids;
        }

        using var profile = await GetJsonAsync($"{Base}/ajax/user/{artistId}/profile/all", $"{Base}/users/{artistId}", ct).ConfigureAwait(false);
        var all = new List<int>();
        if (profile.RootElement.TryGetProperty("body", out var bodyElement))
        {
            foreach (var key in new[] { "illusts", "manga" })
            {
                if (bodyElement.TryGetProperty(key, out var group) && group.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in group.EnumerateObject())
                    {
                        if (int.TryParse(property.Name, out var id))
                        {
                            all.Add(id);
                        }
                    }
                }
            }
        }

        all.Sort((a, b) => b.CompareTo(a));
        return all;
    }

    // ------------------------------------------------------------------ //
    // images
    // ------------------------------------------------------------------ //
    public async Task<bool> DownloadAsync(string url, string referer, string targetPath, bool overwrite, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        if (File.Exists(targetPath) && !overwrite)
        {
            return false;
        }

        var temp = targetPath + ".pixiv";
        var expected = -1L;

        try
        {
            using var response = await SendAsync(url, referer, stream: true, ct).ConfigureAwait(false);
            expected = response.Content.Headers.ContentLength ?? -1;

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                // Manual copy so every chunk can be reported to the speed meter.
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    TransferMeter.Add(read);
                }
            }

            if (expected > 0 && new FileInfo(temp).Length != expected)
            {
                throw new PixivException(PixivErrorKind.Network, $"下载不完整：{new FileInfo(temp).Length}/{expected} 字节。");
            }

            if (overwrite && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(temp, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* ignore */ }
            }
            throw;
        }
    }

    // ------------------------------------------------------------------ //
    private static int GetInt(JsonElement element, string name, int fallback)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }
}
