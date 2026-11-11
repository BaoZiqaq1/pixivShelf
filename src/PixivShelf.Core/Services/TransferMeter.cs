namespace PixivShelf.Core.Services;

/// <summary>
/// Process-wide counter of bytes written for downloaded images. The UI samples it once
/// per second and turns the delta into "↓12.3 MB/s"; concurrent workers all add here.
/// </summary>
public static class TransferMeter
{
    private static long _bytes;

    public static void Add(long count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _bytes, count);
        }
    }

    public static long Read() => Interlocked.Read(ref _bytes);

    public static void Reset() => Interlocked.Exchange(ref _bytes, 0);
}
