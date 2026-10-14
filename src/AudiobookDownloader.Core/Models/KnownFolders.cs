using System.Runtime.InteropServices;

namespace AudiobookDownloader.Core;

/// <summary>Windows folders that have no <see cref="Environment.SpecialFolder"/> of their own.</summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// The Downloads folder, wherever Windows keeps it (it can be moved to another
    /// drive), or "Downloads" in the user's profile when it cannot be asked.
    /// </summary>
    public static string Downloads
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var pointer) == 0)
                    {
                        try
                        {
                            if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path)
                                return path;
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(pointer);
                        }
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    // Fall through to the profile folder.
                }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
