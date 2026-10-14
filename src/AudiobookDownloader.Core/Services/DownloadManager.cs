using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace AudiobookDownloader.Core;

/// <summary>Downloads a file and, when asked, strips DRM from Audible AAX/AAXC locally.</summary>
/// <remarks>
/// Two guarantees matter here. A download never overwrites an existing file in the
/// output folder, and an interrupted transfer is kept as a ".part" file so the next
/// attempt resumes instead of starting from zero.
/// </remarks>
public sealed partial class DownloadManager
{
    /// <summary>Suffix for an interrupted transfer, kept so it can be resumed later.</summary>
    public const string PartExtension = ".part";

    private const int MaxAttempts = 4;

    /// <summary>A transfer that goes this long without receiving a byte is treated as stalled.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    private readonly AppSettings _settings;

    public DownloadManager(AppSettings settings) => _settings = settings;

    public async Task<DownloadResult> DownloadAsync(
        BookInfo book,
        DownloadTicket ticket,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var folder = FolderFor(_settings, book);
        Directory.CreateDirectory(folder);

        var ext = ticket.NeedsDecryption
            ? Path.GetExtension(ticket.Url.ToString()).ToLowerInvariant()
            : (string.IsNullOrWhiteSpace(ticket.SuggestedExtension) ? ".zip" : ticket.SuggestedExtension);

        // Never clobber a file that is already there. A ".part" file from an earlier
        // attempt deliberately does not count, so retrying resumes that download.
        var decrypting = ticket.NeedsDecryption && _settings.RemoveDrm;
        var baseName = decrypting
            ? UniqueBaseName(folder, MakeFileName(book), ext, ".m4b")
            : UniqueBaseName(folder, MakeFileName(book), ext);

        var encryptedPath = Path.Combine(folder, baseName + ext);
        var finalPath = Path.Combine(folder, baseName + ".m4b");

        try
        {
            // 1) download (or copy, when the ticket points at a local file)
            var downloadedPath = ticket.Url.IsFile
                ? await CopyLocalFileAsync(ticket.Url, encryptedPath, progress, ct)
                : await DownloadFileAsync(ticket.Url, encryptedPath, progress, ct);
            if (downloadedPath is null)
                return new DownloadResult(false, null, "Download failed.");

            // The name may have been taken while we were downloading, in which case
            // the file was saved under a different one. Keep the pair consistent.
            if (!string.Equals(downloadedPath, encryptedPath, StringComparison.OrdinalIgnoreCase))
            {
                encryptedPath = downloadedPath;
                finalPath = Path.Combine(
                    Path.GetDirectoryName(downloadedPath)!,
                    Path.GetFileNameWithoutExtension(downloadedPath) + ".m4b");
            }

            // 2) decrypt
            if (decrypting)
            {
                progress?.Report(new DownloadProgress(DownloadPhase.Decrypting, 0, "Removing DRM…"));
                var decryptedPath = await DecryptAsync(encryptedPath, finalPath, ticket, progress, ct);
                if (decryptedPath is null)
                    return new DownloadResult(false, encryptedPath,
                        "Decryption failed. The encrypted original is kept so nothing is lost.");

                if (_settings.DeleteIntermediateFile)
                {
                    DeleteFile(encryptedPath);
                    progress?.Report(new DownloadProgress(DownloadPhase.Done, 100, $"Saved {decryptedPath}"));
                    return new DownloadResult(true, decryptedPath, null);
                }

                progress?.Report(new DownloadProgress(DownloadPhase.Done, 100, $"Saved {decryptedPath} (encrypted original kept)"));
                return new DownloadResult(true, decryptedPath, null);
            }

            // 3) plain (DRM-free) file: keep it exactly as downloaded
            progress?.Report(new DownloadProgress(DownloadPhase.Done, 100, $"Saved {encryptedPath}"));
            return new DownloadResult(true, encryptedPath, null);
        }
        catch (OperationCanceledException)
        {
            // The partial ".part" file is left in place on purpose so that retrying
            // the item resumes the transfer instead of downloading it all again.
            Log.Info($"Download of '{book.Title}' was cancelled; any partial file was kept so it can resume.");
            return new DownloadResult(false, null, "Cancelled. The partial download was kept so it can resume.")
            {
                Cancelled = true,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Download of '{book.Title}' failed: {ex.Message}");
            return new DownloadResult(false, null, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Naming
    // ------------------------------------------------------------------

    /// <summary>Every kind of file a provider can leave behind for a finished book.</summary>
    private static readonly string[] FinishedExtensions =
        [".m4b", ".m4a", ".mp3", ".ogg", ".aaxc", ".aax", ".zip", ".epub", ".html", ".txt"];

    /// <summary>
    /// The folder a book is saved in: the download folder, or a folder named after
    /// the author inside it when "organise by author" is on.
    /// </summary>
    public static string FolderFor(AppSettings settings, BookInfo book) =>
        settings.OrganizeByAuthor &&
        !DisplayText.IsMissing(book.Author) &&
        book.Author != BookInfo.UnknownAuthor && book.Author != "Unknown"
            ? Path.Combine(settings.OutputFolder, SafeName(book.Author, 80))
            : settings.OutputFolder;

    /// <summary>
    /// The file already saved for this book, or null. Only the plain name counts, not
    /// " (2)" copies and not unfinished ".part" files.
    /// </summary>
    public static string? FindExistingDownload(AppSettings settings, BookInfo book)
    {
        try
        {
            var folder = FolderFor(settings, book);
            if (!Directory.Exists(folder))
                return null;

            var name = MakeFileName(book);
            return FinishedExtensions
                .Select(extension => Path.Combine(folder, name + extension))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Find a base name whose files do not already exist in <paramref name="folder"/>,
    /// adding " (2)", " (3)" and so on. Interrupted transfers ("…part") are ignored so
    /// that a retry picks up the file it already started.
    /// </summary>
    public static string UniqueBaseName(string folder, string baseName, params string[] extensions)
    {
        for (var attempt = 1; attempt < 1000; attempt++)
        {
            var candidate = attempt == 1 ? baseName : $"{baseName} ({attempt})";
            if (extensions.All(ext => !File.Exists(Path.Combine(folder, candidate + ext))))
                return candidate;
        }

        // Absurdly unlikely; fall back to something guaranteed to be free.
        return $"{baseName} ({Guid.NewGuid():N})";
    }

    // ------------------------------------------------------------------
    // Downloading
    // ------------------------------------------------------------------

    /// <summary>
    /// Download <paramref name="url"/> to <paramref name="dest"/>, resuming an
    /// interrupted transfer when the server supports it and retrying short-lived
    /// failures with a growing delay. The bytes go to "&lt;dest&gt;.part" and are
    /// moved into place only when the transfer is complete, so <paramref name="dest"/>
    /// is never a truncated file.
    /// </summary>
    private async Task<string?> DownloadFileAsync(Uri url, string dest, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var partPath = dest + PartExtension;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (await TryTransferAsync(url, partPath, progress, ct))
                {
                    // Complete: give the partial file its real name and report where
                    // it actually ended up.
                    return Promote(partPath, dest);
                }

                // The connection ended before the last byte arrived. What we have is
                // still a byte-accurate prefix of the file, so the next attempt
                // resumes from it rather than throwing away real progress.
                Log.Warn($"Download of {url} ended early after {FileLength(partPath)} bytes; resuming.");
                progress?.Report(new DownloadProgress(DownloadPhase.Downloading, 0,
                    "The connection ended early. Resuming…"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                var delay = BackoffDelays[Math.Min(attempt - 1, BackoffDelays.Length - 1)];
                Log.Warn($"Download attempt {attempt} of {MaxAttempts} for {url} failed ({ex.Message}); " +
                         $"retrying in {delay.TotalSeconds:0} seconds. The partial file is kept so it can resume.");

                progress?.Report(new DownloadProgress(DownloadPhase.Downloading, 0,
                    $"Connection problem. Retrying in {delay.TotalSeconds:0} seconds…"));

                await Task.Delay(delay, ct);
            }
        }

        throw new IOException(
            $"The download did not finish after {MaxAttempts} attempts. " +
            "The partial file was kept, so trying again resumes where it stopped.");
    }

    /// <summary>
    /// One attempt at the transfer. Returns true only when the whole file arrived.
    /// </summary>
    private async Task<bool> TryTransferAsync(Uri url, string partPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var existing = FileLength(partPath);

        // The stall timer is reset every time bytes arrive, so a slow but working
        // download is never cut off while a dead connection is detected quickly.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var http = CreateHttpClient();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);

        if (existing > 0)
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // What we have is not usable (or already larger than the server's
                // copy), so throw it away and fetch the file from the beginning.
                Log.Warn("The server rejected a resumed download; starting over.");
                DeleteFile(partPath);
                return false;
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // The server ignored the range and is resending everything.
                Log.Warn("The server did not accept a resumed download; starting over.");
                existing = 0;
                DeleteFile(partPath);
            }
        }

        if (response.StatusCode != HttpStatusCode.PartialContent && response.StatusCode != HttpStatusCode.OK)
            response.EnsureSuccessStatusCode();

        var resumed = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;

        // Work out the full size of the finished file, whether or not the server
        // reports a length for this particular response.
        var total = response.Content.Headers.ContentRange?.Length
                    ?? (response.Content.Headers.ContentLength is { } length ? length + (resumed ? existing : 0) : -1);

        progress?.Report(new DownloadProgress(DownloadPhase.Downloading,
            total > 0 ? existing * 100.0 / total : 0,
            resumed ? "Resuming download…" : "Downloading…"));

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var target = new FileStream(
            partPath,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            useAsync: true);

        var buffer = new byte[1 << 16];
        long written = resumed ? existing : 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, stall.Token);
            if (read <= 0)
                break;

            await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
            written += read;

            // Bytes arrived, so the connection is alive: give it another minute.
            stall.CancelAfter(StallTimeout);

            if (total > 0)
                progress?.Report(new DownloadProgress(DownloadPhase.Downloading, written * 100.0 / total,
                    $"Downloading… {written / 1_000_000.0:0.0} / {total / 1_000_000.0:0.0} MB"));
        }

        await target.FlushAsync(ct);

        if (total < 0)
        {
            // Nothing was advertised to compare against; trust the clean end of stream.
            progress?.Report(new DownloadProgress(DownloadPhase.Downloading, 100, "Downloaded."));
            return true;
        }

        return written >= total;
    }

    private static HttpClient CreateHttpClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = true })
        {
            // The transfer may legitimately take hours; the stall timer above is
            // what protects against a connection that has died.
            Timeout = Timeout.InfiniteTimeSpan,
        };

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException => true,
        IOException => true,
        System.Net.Sockets.SocketException => true,
        // A stalled connection surfaces as a cancellation of the stall timer only.
        TaskCanceledException => true,
        OperationCanceledException => true,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>
    /// Give a finished partial file its real name and return the full path used. If
    /// something else claimed that name while we were downloading — a second copy of
    /// the app, or the user — both files are kept instead of one replacing the other.
    /// </summary>
    private static string Promote(string partPath, string dest)
    {
        try
        {
            File.Move(partPath, dest, overwrite: false);
            return dest;
        }
        catch (IOException)
        {
            var folder = Path.GetDirectoryName(dest) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(dest);
            var extension = Path.GetExtension(dest);
            var alternative = Path.Combine(folder, UniqueBaseName(folder, baseName, extension) + extension);

            File.Move(partPath, alternative, overwrite: false);
            Log.Warn($"\"{Path.GetFileName(dest)}\" appeared while the download was running; " +
                     $"it was saved as \"{Path.GetFileName(alternative)}\" instead.");
            return alternative;
        }
    }

    private static long FileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private async Task<string?> CopyLocalFileAsync(Uri fileUri, string dest, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var srcPath = fileUri.LocalPath;
        if (!File.Exists(srcPath))
            return null;

        progress?.Report(new DownloadProgress(DownloadPhase.Downloading, 0, "Reading local file…"));

        // Copy to a partial file first, so a cancelled or failed copy never leaves a
        // half-written file that looks complete.
        var partPath = dest + PartExtension;
        await using (var source = File.OpenRead(srcPath))
        await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            await source.CopyToAsync(target, ct);
        }

        progress?.Report(new DownloadProgress(DownloadPhase.Downloading, 100, "Read local file."));
        return Promote(partPath, dest);
    }

    /// <summary>Decrypt the file, returning the path of the DRM-free result, or null on failure.</summary>
    private async Task<string?> DecryptAsync(string input, string output, DownloadTicket ticket, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        // Decrypt into a partial file too, so an interrupted decryption cannot be
        // mistaken for a finished one.
        var partPath = output + PartExtension;
        try
        {
            await using var fs = File.OpenRead(input);
            using var aax = new AAXClean.AaxFile(fs, fs.Length);

            if (ticket.Format == TicketFormat.Aaxc)
                aax.SetDecryptionKey(ticket.Key!, ticket.Iv!);
            else
                aax.SetDecryptionKey(ticket.ActivationBytes!);

            await using var destination = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var op = aax.ConvertToMp4aAsync(destination);
            op.ConversionProgressUpdate += (_, e) =>
                progress?.Report(new DownloadProgress(DownloadPhase.Decrypting, e.FractionCompleted * 100,
                    $"Removing DRM… {e.ProcessPosition:hh\\:mm\\:ss} / {e.EndTime:hh\\:mm\\:ss}"));
            op.Start();
            await op.OperationTask.WaitAsync(ct);

            await destination.FlushAsync(ct);
            return Promote(partPath, output);
        }
        catch (Exception ex)
        {
            Log.Error($"Decryption failed: {ex.Message}");
            DeleteFile(partPath);
            return null;
        }
    }

    public static string MakeFileName(BookInfo book) =>
        SafeName(string.IsNullOrWhiteSpace(book.Author)
            ? book.Title
            : $"{book.Author} - {book.Title}", 150);

    /// <summary>A name Windows accepts for a file or folder, at most <paramref name="max"/> characters.</summary>
    private static string SafeName(string name, int max)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name.Replace(':', ' '))
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? ' ' : c);
        var cleaned = MyWhitespaceRegex().Replace(sb.ToString().Trim(), " ").TrimEnd('.', ' ');
        cleaned = cleaned.Length > max ? cleaned[..max].Trim() : cleaned;
        return cleaned.Length > 0 ? cleaned : "Unnamed";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MyWhitespaceRegex();
}
