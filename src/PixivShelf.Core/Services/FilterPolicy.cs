using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

public static class FilterPolicy
{
    /// <summary>
    /// Single source of truth for the content filter: returns whether the artwork
    /// should be downloaded plus the reason when it is skipped.
    /// </summary>
    public static (bool Keep, string? Reason) Evaluate(Artwork artwork, FilterSettings filter)
    {
        if (filter.SkipAiGenerated && artwork.AiType == 2)
        {
            return (false, $"跳过 AI 生成作品 ID={artwork.ArtworkId}");
        }

        // Extra switch on top of the scope: drop R-18G works.
        if (filter.BlockR18G && artwork.IsR18G)
        {
            return (false, $"按屏蔽 R18G 规则跳过作品 ID={artwork.ArtworkId}");
        }

        return filter.Scope switch
        {
            ContentScope.SkipR18 when artwork.IsR18 => (false, $"按全年龄规则跳过作品 ID={artwork.ArtworkId}"),
            ContentScope.R18Only when !artwork.IsR18 => (false, $"按成人内容规则跳过作品 ID={artwork.ArtworkId}"),
            _ => (true, null),
        };
    }

    public static bool ShouldDownload(Artwork artwork, FilterSettings filter) => Evaluate(artwork, filter).Keep;

    public static string? SkipReason(Artwork artwork, FilterSettings filter) => Evaluate(artwork, filter).Reason;
}
