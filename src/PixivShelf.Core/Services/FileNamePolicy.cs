using System.Text;
using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

/// <summary>
/// Flat file naming with the same tokens the Python version supported:
/// %artist% %member_id% %title% %image_id% %page_number% %page_index% %works_date%.
/// </summary>
public static class FileNamePolicy
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    public static string BuildTarget(
        string root,
        string template,
        Artwork artwork,
        int pageNumber,
        string fileUrl,
        string? outputExtension = null)
    {
        var sourceExt = ExtensionOf(fileUrl);
        var ext = string.IsNullOrWhiteSpace(outputExtension) ? sourceExt : outputExtension.TrimStart('.').ToLowerInvariant();
        if (ext == "jpeg")
        {
            ext = "jpg";
        }

        var text = template;
        text = text.Replace("%artist%", Escape(artwork.Artist.Name));
        text = text.Replace("%member_id%", artwork.Artist.MemberId.ToString());
        text = text.Replace("%member_token%", Escape(artwork.Artist.Account));
        text = text.Replace("%title%", Escape(artwork.Title));
        text = text.Replace("%image_id%", artwork.ArtworkId.ToString());
        text = text.Replace("%works_date%", artwork.CreateDate.ToString("yyyy-MM-dd"));
        // Page numbers are zero based: the first page is p0 (matches %page_index%).
        text = text.Replace("%page_number%", $"p{Math.Max(0, pageNumber - 1)}");
        text = text.Replace("%page_index%", Math.Max(0, pageNumber - 1).ToString());

        if (!text.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
        {
            text += "." + ext;
        }

        var relative = SafeJoin(text);
        return Path.Combine(root, relative);
    }

    private static string SafeJoin(string relative)
    {
        var parts = relative
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Escape)
            .ToArray();
        return parts.Length == 0 ? "_" : Path.Combine(parts);
    }

    public static string Escape(string? value)
    {
        var text = value ?? "";
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsControl(ch))
            {
                continue;
            }
            builder.Append(Invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim().Trim('.');
        return result.Length == 0 ? "_" : result;
    }

    private static string ExtensionOf(string url)
    {
        var path = url.Split('?')[0];
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "jpg" : name[(dot + 1)..].ToLowerInvariant();
    }
}
