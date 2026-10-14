using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Offline checks for the download engine: a download must never overwrite an
/// existing file, and an interrupted transfer must resume instead of starting
/// over. A tiny loopback HTTP server stands in for the internet, so this runs
/// anywhere and never touches the network.
/// </summary>
internal static class SelfTestDownloads
{
    public static void Run(Action<string> line, Action<string> fail)
    {
        var folder = Path.Combine(Path.GetTempPath(), "audiobook-selftest-download");
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            fail($"could not create the test folder {folder}: {ex.Message}");
            return;
        }

        try
        {
            CheckFileNaming(folder, line, fail);

            var payload = new byte[200_000];
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 251);

            // A pool thread has no UI synchronization context, so awaiting is safe here.
            Task.Run(() => CheckResumableDownloadsAsync(folder, payload, line, fail)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            fail($"the download checks could not run: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------
    // Naming
    // ------------------------------------------------------------------

    private static void CheckFileNaming(string folder, Action<string> line, Action<string> fail)
    {
        line("== Download file naming ==");

        // A finished file of the same name must survive: choose a new name instead.
        File.WriteAllText(Path.Combine(folder, "Author - Title.aaxc"), "already here");
        var second = DownloadManager.UniqueBaseName(folder, "Author - Title", ".aaxc", ".m4b");
        if (second == "Author - Title")
            fail("downloading a title twice would overwrite the first file");
        else
            line($"   existing file protected; the next name is \"{second}\"");

        // A leftover partial file must NOT change the name, or a retry could not resume it.
        File.WriteAllText(Path.Combine(folder, "Other - Book.mp3" + DownloadManager.PartExtension), "half");
        var resumed = DownloadManager.UniqueBaseName(folder, "Other - Book", ".mp3");
        if (resumed != "Other - Book")
            fail($"a partial download was renamed to \"{resumed}\", so it could never resume");
        else
            line("   a partial file keeps its name, so retrying resumes it");

        // Awkward titles must still produce a name Windows accepts.
        var book = new BookInfo("t", "id", "A: Title? With*Bad/Chars", "Author", "", 0, null, null, null);
        var name = DownloadManager.MakeFileName(book);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            fail($"\"{name}\" still contains characters Windows rejects");
        else
            line($"   awkward title cleaned to \"{name}\"");

        line("");
        CheckAlreadyDownloaded(folder, line, fail);
    }

    /// <summary>
    /// A finished book is recognised in the download folder (or its author's folder),
    /// a half-finished one is not, and the row says so in words.
    /// </summary>
    private static void CheckAlreadyDownloaded(string folder, Action<string> line, Action<string> fail)
    {
        line("== Already downloaded ==");

        var settings = new AppSettings { OutputFolder = folder };
        var finished = new BookInfo("t", "1", "Finished Book", "Some Writer", "", 0, null, null, null);
        var half = new BookInfo("t", "2", "Half Book", "Some Writer", "", 0, null, null, null);

        File.WriteAllText(Path.Combine(folder, DownloadManager.MakeFileName(finished) + ".m4b"), "done");
        File.WriteAllText(Path.Combine(folder, DownloadManager.MakeFileName(half) + ".mp3" + DownloadManager.PartExtension), "half");

        var wrong = 0;
        void Complain(string message) { wrong++; fail(message); }

        if (DownloadManager.FindExistingDownload(settings, finished) is null)
            Complain("a finished book in the download folder is not recognised as downloaded");
        if (DownloadManager.FindExistingDownload(settings, half) is not null)
            Complain("an unfinished .part file is taken for a finished download");

        settings.OrganizeByAuthor = true;
        var authorFolder = DownloadManager.FolderFor(settings, finished);
        if (!string.Equals(authorFolder, Path.Combine(folder, "Some Writer"), StringComparison.OrdinalIgnoreCase))
            Complain($"organising by author puts the book in \"{authorFolder}\"");
        if (DownloadManager.FolderFor(settings, finished with { Author = BookInfo.UnknownAuthor }) != folder)
            Complain("a book with no known author is given an \"Unknown author\" folder");

        var row = (finished with { AlreadyDownloaded = true }).ToString();
        if (!row.EndsWith($"({BookInfo.AlreadyDownloadedText})", StringComparison.Ordinal))
            Complain($"a downloaded book's row reads \"{row}\" without saying it is downloaded");

        if (wrong == 0)
        {
            line("   a finished file is recognised, a .part file is not, and the row says \"already downloaded\"");
            line("   organising by author uses the author's name as the folder, and no folder for unknown authors");
        }

        line("");
    }

    // ------------------------------------------------------------------
    // Resuming
    // ------------------------------------------------------------------

    private static async Task CheckResumableDownloadsAsync(string folder, byte[] payload, Action<string> line, Action<string> fail)
    {
        line("== Resumable downloads ==");

        var settings = new AppSettings { OutputFolder = Path.Combine(folder, "out") };
        Directory.CreateDirectory(settings.OutputFolder);
        var manager = new DownloadManager(settings);

        // 0) The ordinary case: one clean response, no retries.
        using (var server = new LoopbackFileServer(payload))
        {
            var book = new BookInfo("selftest", "v0", "Clean", "Author", "", 0, null, null, null);
            var result = await manager.DownloadAsync(book, server.TicketFor(book));

            var expected = Path.Combine(settings.OutputFolder, DownloadManager.MakeFileName(book) + ".m4a");
            if (!result.Success)
                fail($"a plain download failed: {result.ErrorMessage}");
            else if (!server.ServedTo(expected))
                fail("a plain download did not produce the expected file");
            else if (server.RangeRequests != 0)
                fail("a plain download asked for ranges when it did not need to");
            else
                line("   an ordinary download succeeded on the first attempt");
        }

        // 1) The connection is cut part way through; the retry must resume.
        using (var server = new LoopbackFileServer(payload) { TruncateFirstResponseAfter = 40_000 })
        {
            var book = new BookInfo("selftest", "v1", "Cut Short", "Author", "", 0, null, null, null);
            var result = await manager.DownloadAsync(book, server.TicketFor(book));

            var expected = Path.Combine(settings.OutputFolder, DownloadManager.MakeFileName(book) + ".m4a");
            if (!result.Success)
                fail($"a download cut short did not recover: {result.ErrorMessage}");
            else if (server.RangeRequests == 0)
                fail("the retry started from the beginning instead of resuming");
            else if (!server.ServedTo(expected))
                fail("the resumed file does not match the original data");
            else
                line($"   recovered from a cut connection with {server.RangeRequests} range request(s)");

            if (File.Exists(expected + DownloadManager.PartExtension))
                fail("a partial file was left behind after a successful download");

            if (File.Exists(expected) && File.Exists(expected + DownloadManager.PartExtension))
                fail("both the finished file and a partial file exist");
        }

        // 2) A server that refuses to resume must still end up with a correct file.
        using (var server = new LoopbackFileServer(payload) { TruncateFirstResponseAfter = 30_000, NeverHonourRange = true })
        {
            var book = new BookInfo("selftest", "v2", "No Ranges", "Author", "", 0, null, null, null);
            var result = await manager.DownloadAsync(book, server.TicketFor(book));

            var expected = Path.Combine(settings.OutputFolder, DownloadManager.MakeFileName(book) + ".m4a");
            if (!result.Success)
                fail($"a download from a server without range support failed: {result.ErrorMessage}");
            else if (!server.ServedTo(expected))
                fail("the file from a server without range support does not match the original");
            else
                line("   recovered from a server that does not support resuming");
        }

        // 4) If the name is claimed while the download is running, both files are kept.
        using (var server = new LoopbackFileServer(payload) { ChunkSize = 4096, ChunkDelayMs = 20 })
        {
            var book = new BookInfo("selftest", "v4", "Raced", "Author", "", 0, null, null, null);
            var expected = Path.Combine(settings.OutputFolder, DownloadManager.MakeFileName(book) + ".m4a");
            const string intruder = "created by someone else while the download was running";

            var claim = Task.Run(async () =>
            {
                await Task.Delay(200);
                await File.WriteAllTextAsync(expected, intruder);
            });

            var result = await manager.DownloadAsync(book, server.TicketFor(book));
            await claim;

            if (!result.Success)
                fail($"a download whose name was taken failed: {result.ErrorMessage}");
            else if (result.OutputFile == expected)
                fail("a download overwrote a file that appeared while it was running");
            else if (!File.Exists(expected) || !File.ReadAllText(expected).Equals(intruder))
                fail("the file that appeared during the download was damaged");
            else if (result.OutputFile is null || !server.ServedTo(result.OutputFile))
                fail("the download was not saved correctly under its new name");
            else
                line($"   a name taken mid-download was preserved; ours went to \"{Path.GetFileName(result.OutputFile)}\"");
        }

        // 3) Cancelling must report cancellation, and must keep what arrived so the
        //    item can be retried and resume rather than start again.
        using (var server = new LoopbackFileServer(payload) { ChunkSize = 4096, ChunkDelayMs = 20 })
        {
            var book = new BookInfo("selftest", "v3", "Stopped", "Author", "", 0, null, null, null);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var result = await manager.DownloadAsync(book, server.TicketFor(book), null, stop.Token);

            var part = Path.Combine(settings.OutputFolder,
                DownloadManager.MakeFileName(book) + ".m4a" + DownloadManager.PartExtension);

            if (!result.Cancelled)
                fail($"a cancelled download was reported as a failure: \"{result.ErrorMessage}\"");
            else if (!File.Exists(part))
                fail("cancelling removed the partial file, so the download could not resume");
            else
                line($"   cancelling kept {new FileInfo(part).Length} bytes so it can resume");
        }

        line("");
    }

}
