using System;
using PixivShelf.Core.Models;

namespace PixivShelf.Services;

/// <summary>Opt-in startup tracing (set PIXIV_TRACE=1) to diagnose launch crashes.</summary>
internal static class StartupTrace
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("PIXIV_TRACE"), "1", StringComparison.Ordinal);

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            var logDirectory = System.IO.Path.Combine(AppSettings.DefaultDirectory, "logs");
            System.IO.Directory.CreateDirectory(logDirectory);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDirectory, "startup.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // tracing must never break startup
        }
    }
}
