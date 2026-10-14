using System.Text;
using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The updater, offline: a release is read with its setup program and portable exe,
/// the new version downloads complete, the notes read as plain text, an installed
/// copy is told from a portable one, and the replace script names the right files.
/// Nothing is installed or replaced.
/// </summary>
internal static class SelfTestUpdates
{
    public static void Run(Action<string> line, Action<string> fail)
    {
        line("== Updater ==");
        var previousApi = UpdateChecker.ApiUrl;
        try
        {
            Task.Run(() => CheckAsync(line, fail)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            fail($"the updater check stopped: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            UpdateChecker.ApiUrl = previousApi;
        }
        line("");
    }

    private static async Task CheckAsync(Action<string> line, Action<string> fail)
    {
        // A release as GitHub describes it, with both files attached.
        var release = """
            {
              "tag_name": "v99.1.0",
              "html_url": "https://example.invalid/release",
              "body": "## What's new\n\n- **Bold** item with `code`\n- Second item\n\n---\n",
              "assets": [
                { "name": "AudiobookDownloader-Setup-99.1.0.exe", "browser_download_url": "https://example.invalid/setup.exe" },
                { "name": "AudiobookDownload.exe", "browser_download_url": "https://example.invalid/AudiobookDownload.exe" }
              ]
            }
            """;
        using (var api = new LoopbackFileServer(Encoding.UTF8.GetBytes(release)))
        {
            UpdateChecker.ApiUrl = $"http://127.0.0.1:{api.Port}/releases/latest";
            var update = await UpdateChecker.CheckAsync(new Version(1, 0, 0));
            if (update is null)
                fail("a newer release was not recognised");
            else if (update.InstallerUrl != "https://example.invalid/setup.exe" ||
                     update.PortableUrl != "https://example.invalid/AudiobookDownload.exe")
                fail($"the release's files were read wrongly: setup {update.InstallerUrl}, exe {update.PortableUrl}");
            else if (Updater.FileFor(update, installed: true) != update.InstallerUrl ||
                     Updater.FileFor(update, installed: false) != update.PortableUrl)
                fail("an installed copy should take the setup program and a portable copy the exe");
            else
                line("   a newer release is read with its setup program and its portable exe");

            if (await UpdateChecker.CheckAsync(new Version(99, 1, 0)) is not null)
                fail("the version already running was offered as an update");
        }

        var notes = Updater.PlainNotes("## What's new\n\n- **Bold** item with `code`\n\n\n\n---\n- Second");
        if (notes.Contains("**") || notes.Contains('`') || notes.Contains("##") || notes.Contains("---") ||
            !notes.Contains("• Bold item with code") || notes.Contains("\r\n\r\n\r\n"))
            fail($"the notes are not plain text: \"{notes}\"");
        else
            line("   What's new reads as plain lines, without Markdown marks");

        // The new version downloads whole.
        var payload = new byte[300_000];
        new Random(7).NextBytes(payload);
        using (var files = new LoopbackFileServer(payload) { ChunkSize = 32_000 })
        {
            var percents = new List<int>();
            var path = await Updater.DownloadAsync($"http://127.0.0.1:{files.Port}/AudiobookDownloader-Setup-99.1.0.exe",
                new SyncProgress(percents.Add), CancellationToken.None);
            try
            {
                if (!files.ServedTo(path))
                    fail("the downloaded update is not the file that was served");
                else if (!percents.Contains(100))
                    fail("the update download never reported 100 percent");
                else if (!path.EndsWith("AudiobookDownloader-Setup-99.1.0.exe", StringComparison.Ordinal))
                    fail($"the update was saved under the wrong name: {Path.GetFileName(path)}");
                else
                    line($"   the new version downloads complete ({payload.Length:N0} bytes), with progress");
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* temp */ }
            }
        }

        // Installed or portable: the setup program leaves its uninstaller beside the exe.
        var folder = Path.Combine(Path.GetTempPath(), "selftest-installed-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        try
        {
            var portable = Updater.IsInstalled(folder);
            File.WriteAllText(Path.Combine(folder, "unins000.exe"), "");
            var installed = Updater.IsInstalled(folder);
            if (portable || !installed)
                fail("an installed copy is not told apart from a portable one");
            else
                line("   an installed copy is told apart from a portable one");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        var script = Updater.PortableScript(4242, @"C:\Temp\new\AudiobookDownload.exe", @"D:\Apps\मेरी किताबें\AudiobookDownload.exe");
        if (!script.Contains("PID eq 4242") ||
            !script.Contains(@"copy /y ""C:\Temp\new\AudiobookDownload.exe"" ""D:\Apps\मेरी किताबें\AudiobookDownload.exe""") ||
            !script.Contains(@"start """" ""D:\Apps\मेरी किताबें\AudiobookDownload.exe""") ||
            !script.Contains("chcp 65001"))
            fail("the portable update script does not wait, copy and restart the right files");
        else
            line("   the portable update waits for the app to close, replaces it and starts it again");
    }

    /// <summary>Reports on the calling thread, so the list is complete when the download returns.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
