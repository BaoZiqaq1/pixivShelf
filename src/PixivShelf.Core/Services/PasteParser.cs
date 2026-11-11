using System.Text.RegularExpressions;

namespace PixivShelf.Core.Services;

/// <summary>Parses pasted text into artwork ids (bare ids or pixiv links).</summary>
public static class PasteParser
{
    private static readonly Regex ArtworkUrl = new(@"/artworks/(\d+)", RegexOptions.Compiled);
    private static readonly Regex UserUrl = new(@"/users/(\d+)", RegexOptions.Compiled);
    private static readonly Regex IllustIdQuery = new(@"illust_id=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Splitter = new(@"[\s,;]+", RegexOptions.Compiled);

    /// <summary>
    /// Reads an artist id from "1234567", "https://www.pixiv.net/users/1234567" or
    /// ".../users/1234567/artworks". Returns null when the text is not an artist reference.
    /// </summary>
    public static int? ParseArtistId(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var match = UserUrl.Match(value);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var fromUrl) && fromUrl > 0)
        {
            return fromUrl;
        }

        if (value.All(char.IsDigit) && int.TryParse(value, out var bare) && bare > 0)
        {
            return bare;
        }

        return null;
    }

    public static (List<int> Ids, List<string> Rejected) Parse(string? text)
    {
        var ids = new List<int>();
        var rejected = new List<string>();
        var seen = new HashSet<int>();

        foreach (var rawToken in Splitter.Split((text ?? "").Trim()))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            string? found = null;
            var match = ArtworkUrl.Match(token);
            if (!match.Success)
            {
                match = IllustIdQuery.Match(token);
            }
            if (match.Success)
            {
                found = match.Groups[1].Value;
            }
            else if (token.All(char.IsDigit) && token.Length is >= 3 and <= 12)
            {
                found = token;
            }

            if (found is null)
            {
                rejected.Add(token);
                continue;
            }

            if (!int.TryParse(found, out var value) || value <= 0)
            {
                rejected.Add(token);
                continue;
            }

            if (seen.Add(value))
            {
                ids.Add(value);
            }
        }

        return (ids, rejected);
    }
}
