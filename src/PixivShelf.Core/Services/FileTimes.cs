using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PixivShelf.Core.Services;

/// <summary>Writes NTFS creation/modification times (Explorer's 创建日期 column).</summary>
public static class FileTimes
{
    private const uint FileWriteAttributes = 0x0100;
    private const uint FileShareAll = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const long FileTimeEpochDelta = 116444736000000000L; // 1601-01-01 -> 1970-01-01 in 100ns

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileTime")]
    private static extern bool SetFileTimeBoth(SafeFileHandle handle, ref long creation, IntPtr access, ref long write);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileTime")]
    private static extern bool SetFileTimeWriteOnly(SafeFileHandle handle, IntPtr creation, IntPtr access, ref long write);

    public static void Apply(string path, DateTimeOffset when, bool creation, bool modified)
    {
        if (!OperatingSystem.IsWindows() || !creation)
        {
            if (modified)
            {
                File.SetLastWriteTimeUtc(path, when.UtcDateTime);
            }
            return;
        }

        using var handle = CreateFileW(path, FileWriteAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFileW failed for {path}");
        }

        var fileTime = when.UtcTicks - DateTime.UnixEpoch.Ticks + FileTimeEpochDelta;

        var ok = modified
            ? SetFileTimeBoth(handle, ref fileTime, IntPtr.Zero, ref fileTime)
            : SetFileTimeWriteOnly(handle, IntPtr.Zero, IntPtr.Zero, ref fileTime);

        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SetFileTime failed for {path}");
        }
    }
}
