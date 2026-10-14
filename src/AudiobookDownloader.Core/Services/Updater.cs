using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace AudiobookDownloader.Core;

/// <summary>
/// Downloads a new version and puts it in place. An installed copy runs the new
/// setup program, which closes the app, installs over it and starts it again. A
/// copy run from a folder (the portable exe) is replaced by the new exe once the app
/// has closed, and started again.
/// </summary>
public static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookDownloader-Updater");
        return client;
    }

    /// <summary>The running exe, which a portable update replaces.</summary>
    public static string? RunningExe => Environment.ProcessPath;

    /// <summary>
    /// True when this copy was installed with the setup program (its uninstaller sits
    /// beside it), so an update runs the new setup program.
    /// </summary>
    public static bool IsInstalled(string? folder = null)
    {
        folder ??= AppContext.BaseDirectory;
        try
        {
            return Directory.EnumerateFiles(folder, "unins*.exe").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The file this copy should be updated with, or null when the release has none.</summary>
    public static string? FileFor(UpdateInfo update, bool installed) =>
        installed ? update.InstallerUrl ?? update.PortableUrl : update.PortableUrl ?? update.InstallerUrl;

    /// <summary>Downloads a release file into a fresh temporary folder, reporting percent done.</summary>
    public static async Task<string> DownloadAsync(string url, IProgress<int>? progress, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AudiobookDownloader-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = "AudiobookDownload-update.exe";
        var path = Path.Combine(folder, name);

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            var last = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0)
                {
                    var percent = (int)(done * 100 / total.Value);
                    if (percent != last)
                        progress?.Report(last = percent);
                }
            }
        }

        if (total is > 0 && new FileInfo(path).Length != total)
            throw new IOException("The download of the new version was incomplete.");

        Log.Info($"Update downloaded: {path}");
        return path;
    }

    /// <summary>
    /// Starts putting the downloaded file in place. The caller then closes the app:
    /// the setup program (or, for the portable exe, a small script) waits for that,
    /// replaces the app and starts it again.
    /// </summary>
    public static void Apply(string downloaded, bool installed)
    {
        if (installed || downloaded.Contains("setup", StringComparison.OrdinalIgnoreCase))
        {
            // Inno Setup: progress only, no questions; it closes the app if it is still
            // running and starts it again when it has finished.
            Process.Start(new ProcessStartInfo
            {
                FileName = downloaded,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true,
            });
            Log.Info("Update: the setup program was started.");
            return;
        }

        var target = RunningExe ?? throw new InvalidOperationException("The running program could not be found.");
        var script = Path.Combine(Path.GetDirectoryName(downloaded)!, "replace.cmd");
        // UTF-8 without a byte-order mark, read as such after "chcp 65001", so a path
        // with letters outside English (a Hindi user name) still works.
        File.WriteAllText(script, PortableScript(Environment.ProcessId, downloaded, target), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{script}\"\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });
        Log.Info($"Update: {target} will be replaced once the app has closed.");
    }

    /// <summary>
    /// Waits for the app to close, copies the new exe over the old one (trying again
    /// for a while if the file is still in use), starts it, and removes itself.
    /// </summary>
    public static string PortableScript(int processId, string newExe, string target) =>
        $"""
        @echo off
        chcp 65001 >nul
        :wait
        tasklist /fi "PID eq {processId}" | find "{processId}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
        set tries=0
        :copy
        copy /y "{newExe}" "{target}" >nul 2>&1 && goto start
        set /a tries+=1
        if %tries% lss 30 (timeout /t 1 /nobreak >nul & goto copy)
        goto end
        :start
        start "" "{target}"
        :end
        del "{newExe}" >nul 2>&1
        (goto) 2>nul & del "%~f0"
        """.Replace("\n", "\r\n");

    /// <summary>
    /// The release notes as plain text for the "What's new" box: Markdown marks are
    /// dropped, headings and list items kept as lines of their own.
    /// </summary>
    public static string PlainNotes(string notes)
    {
        var lines = new List<string>();
        foreach (var raw in (notes ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim() == "---")
                continue;
            line = line.TrimStart('#', ' ');
            line = line.Replace("**", "").Replace("`", "");
            if (line.StartsWith("- ", StringComparison.Ordinal))
                line = "• " + line[2..];
            lines.Add(line);
        }

        // At most one blank line in a row.
        var text = string.Join("\r\n", lines).Trim();
        while (text.Contains("\r\n\r\n\r\n", StringComparison.Ordinal))
            text = text.Replace("\r\n\r\n\r\n", "\r\n\r\n", StringComparison.Ordinal);
        return text;
    }
}
