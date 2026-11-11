namespace PixivShelf.Core.Services;

/// <summary>
/// The naming presets offered by the UI, in menu order, plus the sample text shown
/// under the format box. Kept in Core so the settings page and the first-run wizard
/// cannot drift apart.
/// </summary>
public static class FileNamePresets
{
    public static IReadOnlyList<(string Label, string Format)> All { get; } = new[]
    {
        ("PID_序号", "%image_id%_p%page_index%"),
        ("PID_第几P", "%image_id%_%page_number%"),
        ("投稿名_第几P_PID", "%title%_%page_number%_%image_id%"),
        ("画师_投稿名_第几P", "%artist%_%title%_%page_number%"),
        ("画师_PID_第几P", "%artist%_%image_id%_%page_number%"),
        ("投稿日期_画师_投稿名", "%works_date%_%artist%_%title%"),
        ("投稿日期_PID_第几P", "%works_date%_%image_id%_%page_number%"),
        ("画师ID_投稿名_PID", "%member_id%_%title%_%image_id%"),
    };

    /// <summary>Renders "<format> → 示例.jpg" using dummy metadata, for live preview.</summary>
    public static string BuildSample(string format, string extension = "jpg")
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return "";
        }

        var sample = format
            .Replace("%image_id%", "123456789")
            .Replace("%page_number%", "p0")
            .Replace("%page_index%", "0")
            .Replace("%title%", "夕焼けの少女")
            .Replace("%artist%", "あかり")
            .Replace("%member_id%", "987654")
            .Replace("%member_token%", "akari")
            .Replace("%works_date%", "2024-05-01");

        return sample + "." + extension;
    }
}
