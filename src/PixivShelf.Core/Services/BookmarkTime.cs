using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

/// <summary>
/// pixiv only exposes per-month bookmark counts; within a month the exact day is
/// estimated by spreading works evenly in bookmark order (newest first input).
/// </summary>
public static class BookmarkTime
{
    public static Dictionary<int, DateTimeOffset> Estimate(
        IReadOnlyList<BookmarkPeriod> periods,
        IReadOnlyList<int> orderedIds,
        DateTimeOffset? now = null)
    {
        var result = new Dictionary<int, DateTimeOffset>();
        if (periods.Count == 0 || orderedIds.Count == 0)
        {
            return result;
        }

        var current = now ?? DateTimeOffset.Now;
        var offset = current.Offset;
        var index = 0;

        foreach (var period in periods)
        {
            if (period.Count <= 0)
            {
                continue;
            }

            var monthIds = new List<int>();
            for (var i = 0; i < period.Count && index < orderedIds.Count; i++, index++)
            {
                monthIds.Add(orderedIds[index]);
            }

            if (monthIds.Count == 0)
            {
                break;
            }

            var start = new DateTimeOffset(new DateTime(period.Year, period.Month, 1, 0, 0, 0), offset);
            var next = period.Month == 12
                ? new DateTimeOffset(new DateTime(period.Year + 1, 1, 1, 0, 0, 0), offset)
                : new DateTimeOffset(new DateTime(period.Year, period.Month + 1, 1, 0, 0, 0), offset);
            var end = next < current ? next : current;
            if (end <= start)
            {
                end = start.AddMinutes(monthIds.Count);
            }

            var spanMinutes = Math.Max((int)(end - start).TotalMinutes, monthIds.Count * 2);

            // monthIds is newest-first; assigning oldest-first keeps time ascending.
            for (var position = 0; position < monthIds.Count; position++)
            {
                var id = monthIds[monthIds.Count - 1 - position];
                var offsetMinutes = (int)Math.Round((position + 1) * (double)spanMinutes / (monthIds.Count + 1));
                offsetMinutes = Math.Min(offsetMinutes, spanMinutes - 1);
                result[id] = start.AddMinutes(offsetMinutes);
            }

            if (index >= orderedIds.Count)
            {
                break;
            }
        }

        return result;
    }
}
