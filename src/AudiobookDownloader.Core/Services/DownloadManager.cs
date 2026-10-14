using AAXClean.Codecs;
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

        // The extension comes from the format, never from the address: Audible's
        // addresses end in a long signed query string, and taking the extension from
        // the whole address put that string into the file name, which Windows refuses.
        var ext = ticket.Format switch
        {
            TicketFormat.Aaxc => ".aaxc",
            TicketFormat.Aax => ".aax",
            _ => string.IsNullOrWhiteSpace(ticket.SuggestedExtension) ? ".zip" : ticket.SuggestedExtension,
        };

        // Never clobber a file that is already there. A ".part" file from an earlier
        // attempt deliberately does not count, so retrying resumes that download.
        var decrypting = ticket.NeedsDecryption && _settings.DrmRemovalEnabled;

        // A file already on this computer is decrypted where it is, never copied first.
        if (decrypting && ticket.Url.IsFile)
            return await DecryptLocalFileAsync(book, ticket, progress, ct);
        var finalExtension = decrypting && WantsMp3 ? ".mp3" : ".m4b";
        var baseName = decrypting
            ? UniqueBaseName(folder, MakeFileName(book), ext, finalExtension)
            : UniqueBaseName(folder, MakeFileName(book), ext);

        var encryptedPath = Path.Combine(folder, baseName + ext);
        var finalPath = Path.Combine(folder, baseName + finalExtension);

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
                    Path.GetFileNameWithoutExtension(downloadedPath) + finalExtension);
            }

            // 2) decrypt
            if (decrypting)
            {
                progress?.Report(new DownloadProgress(DownloadPhase.Decrypting, 0, "Removing DRM…"));
                var cover = await SaveCoverAsync(book, finalPath, ct);
                var decryptedPath = await DecryptAsync(encryptedPath, finalPath, ticket, book, cover, progress, ct);
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

            // 3) plain (DRM-free) file, or an Audible book kept with its DRM: keep it
            // exactly as downloaded. The keys are remembered, so turning DRM removal
            // on later can still unlock it without downloading it again.
            if (ticket.NeedsDecryption)
                RememberKeys(encryptedPath, ticket);
            await SaveCoverAsync(book, encryptedPath, ct);
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
            var file = FinishedExtensions
                .Select(extension => Path.Combine(folder, name + extension))
                .FirstOrDefault(File.Exists);

            // A book split into chapters is a folder of its own.
            var chapters = Path.Combine(folder, name);
            return file ?? (Directory.Exists(chapters) && Directory.EnumerateFiles(chapters).Any() ? chapters : null);
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

            await SpeedLimit.WaitAsync(read, _settings.SpeedLimitKBps, ct);

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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            // The transfer may legitimately take hours; the stall timer above is
            // what protects against a connection that has died.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        // Audible's content servers answer a request without a User-Agent with
        // 403 Forbidden, which made every Audible download fail.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookDownloader/" +
            (typeof(DownloadManager).Assembly.GetName().Version?.ToString(3) ?? "1.0"));
        return client;
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        // "Forbidden" or "Not found" will not change by asking again; only a timeout,
        // "too many requests" or a server error is worth another attempt.
        HttpRequestException { StatusCode: { } status } =>
            (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
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

    /// <summary>The keys that unlock an encrypted file, stored against its path.</summary>
    private void RememberKeys(string encryptedPath, DownloadTicket ticket)
    {
        try
        {
            _settings.CacheKeys(encryptedPath, new CachedKeys(encryptedPath, ticket.Key, ticket.Iv, ticket.ActivationBytes));
            Log.Info($"Kept with DRM: {Path.GetFileName(encryptedPath)}. Its keys are remembered for later.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remember the keys for {encryptedPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes DRM from an AAX or AAXC file already on disk. A file in the download
    /// folder (a book downloaded earlier with its DRM) gets its DRM-free copy beside
    /// it and, when the settings say so, the encrypted original is removed once the
    /// copy has been checked. A file anywhere else is converted into the download
    /// folder and never touched.
    /// </summary>
    private async Task<DownloadResult> DecryptLocalFileAsync(BookInfo book, DownloadTicket ticket,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var source = ticket.Url.LocalPath;
        if (!File.Exists(source))
            return new DownloadResult(false, null, $"The file {source} no longer exists.");

        var ours = IsInside(source, _settings.OutputFolder);
        var folder = ours ? Path.GetDirectoryName(source)! : FolderFor(_settings, book);
        Directory.CreateDirectory(folder);

        var extension = WantsMp3 ? ".mp3" : ".m4b";
        var name = ours ? Path.GetFileNameWithoutExtension(source) : MakeFileName(book);
        var output = Path.Combine(folder, UniqueBaseName(folder, name, extension) + extension);

        progress?.Report(new DownloadProgress(DownloadPhase.Decrypting, 0, "Removing DRM…"));
        var result = await DecryptAsync(source, output, ticket, book, null, progress, ct);
        if (result is null)
            return new DownloadResult(false, null, "Decryption failed. The encrypted file was left as it was.");

        if (ours && _settings.DeleteIntermediateFile)
        {
            DeleteFile(source);
            lock (_settings) _settings.KeyCache.RemoveAll(k => string.Equals(k.FileName, source, StringComparison.OrdinalIgnoreCase));
        }

        progress?.Report(new DownloadProgress(DownloadPhase.Done, 100, $"Saved {result}"));
        return new DownloadResult(true, result, null);
    }

    // ------------------------------------------------------------------
    // Backup
    // ------------------------------------------------------------------

    /// <summary>
    /// A OneDrive folder for backups, when OneDrive is set up on this computer: what
    /// is copied there is uploaded to the cloud by OneDrive itself.
    /// </summary>
    public static string? SuggestedBackupFolder()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Path.Combine(root, "Audiobooks");
        }
        return null;
    }

    /// <summary>
    /// Copies a finished book (a file, or a folder of chapters) and its cover into the
    /// backup folder, keeping its place under the download folder. Never fails a
    /// download: a backup that cannot be made is logged and reported as null.
    /// </summary>
    public async Task<string?> BackupAsync(string? finishedPath, CancellationToken ct = default)
    {
        if (!_settings.BackupEnabled || string.IsNullOrWhiteSpace(_settings.BackupFolder) || string.IsNullOrWhiteSpace(finishedPath))
            return null;

        try
        {
            var relative = IsInside(finishedPath, _settings.OutputFolder)
                ? Path.GetRelativePath(_settings.OutputFolder, finishedPath)
                : Path.GetFileName(finishedPath);
            var target = Path.Combine(_settings.BackupFolder, relative);

            if (Directory.Exists(finishedPath))
            {
                foreach (var file in Directory.EnumerateFiles(finishedPath, "*", SearchOption.AllDirectories))
                    await CopyIfMissingAsync(file, Path.Combine(target, Path.GetRelativePath(finishedPath, file)), ct);
            }
            else
            {
                await CopyIfMissingAsync(finishedPath, target, ct);
                var cover = Path.ChangeExtension(finishedPath, ".jpg");
                if (File.Exists(cover))
                    await CopyIfMissingAsync(cover, Path.ChangeExtension(target, ".jpg"), ct);
            }

            Log.Info($"Backed up to {target}");
            return target;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Could not back up {finishedPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies every finished book in the download folder that is not in the backup
    /// folder yet. Returns how many files were copied.
    /// </summary>
    public async Task<int> BackupEverythingAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.BackupFolder) || !Directory.Exists(_settings.OutputFolder))
            return 0;

        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(_settings.OutputFolder, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (file.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase) || IsInside(file, _settings.BackupFolder))
                continue;

            var target = Path.Combine(_settings.BackupFolder, Path.GetRelativePath(_settings.OutputFolder, file));
            if (File.Exists(target))
                continue;

            progress?.Report(Path.GetFileName(file));
            await CopyIfMissingAsync(file, target, ct);
            copied++;
        }

        Log.Info($"Backup: {copied} file(s) copied to {_settings.BackupFolder}.");
        return copied;
    }

    private static async Task CopyIfMissingAsync(string source, string target, CancellationToken ct)
    {
        if (File.Exists(target))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Into a partial file first, so a backup cut short never looks complete.
        var part = target + PartExtension;
        await using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
        await using (var to = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            await from.CopyToAsync(to, ct);
        File.Move(part, target, overwrite: false);
    }

    private static bool IsInside(string path, string folder)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypted Audible files in the download folder whose keys are known, so their
    /// DRM can be removed without downloading them again.
    /// </summary>
    public static IReadOnlyList<string> FindEncryptedDownloads(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(settings.OutputFolder))
                return [];

            return Directory.EnumerateFiles(settings.OutputFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".aaxc", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".aax", StringComparison.OrdinalIgnoreCase))
                .Where(f => settings.FindKeys(f) is { } keys &&
                            (!string.IsNullOrEmpty(keys.Key) || !string.IsNullOrEmpty(keys.ActivationBytes)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private bool WantsMp3 => string.Equals(_settings.OutputFormat, "mp3", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decrypt the file, returning the path of the DRM-free result (a file, or a
    /// folder of chapter files), or null on failure.
    /// </summary>
    private async Task<string?> DecryptAsync(string input, string output, DownloadTicket ticket, BookInfo book,
        byte[]? cover, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(input);
            using var aax = new AAXClean.AaxFile(fs, fs.Length);

            if (ticket.Format == TicketFormat.Aaxc)
                aax.SetDecryptionKey(ticket.Key!, ticket.Iv!);
            else
                aax.SetDecryptionKey(ticket.ActivationBytes!);

            var written = await ConvertAsync(aax, output, book, cover, progress, ct, ticket.Chapters);
            Verify(written, aax.Duration);
            return written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Decryption failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Makes sure the DRM-free copy really is the whole book: every chapter file is
    /// there and a single M4B plays for as long as the original. A copy that falls
    /// short is deleted and reported, so the encrypted original is never given up
    /// for a broken one.
    /// </summary>
    private static void Verify(string written, TimeSpan expected)
    {
        if (Directory.Exists(written))
        {
            if (!Directory.EnumerateFiles(written).Any(f => new FileInfo(f).Length > 0))
                throw new IOException("The chapter files were not written.");
            return;
        }

        var info = new FileInfo(written);
        if (!info.Exists || info.Length == 0)
            throw new IOException("The DRM-free file was not written.");

        if (!written.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase) || expected <= TimeSpan.Zero)
            return;

        TimeSpan actual;
        using (var check = File.OpenRead(written))
        using (var mp4 = new AAXClean.Mp4File(check, check.Length))
            actual = mp4.Duration;

        if (actual.TotalSeconds < expected.TotalSeconds * 0.98)
        {
            DeleteFile(written);
            throw new IOException(
                $"The DRM-free copy is {actual:hh\\:mm\\:ss} long but the book is {expected:hh\\:mm\\:ss}; it was discarded.");
        }
    }

    /// <summary>
    /// Writes an opened (and, for Audible, unlocked) audiobook out the way the
    /// settings ask: one M4B or MP3, or one file per chapter, with the book's
    /// details in its tags. Returns the file or chapter folder written.
    /// </summary>
    /// <remarks>Public so the self-test can drive it with an ordinary, unencrypted file.</remarks>
    public async Task<string> ConvertAsync(AAXClean.Mp4File source, string output, BookInfo book,
        byte[]? cover, IProgress<DownloadProgress>? progress, CancellationToken ct,
        IReadOnlyList<ChapterMark>? chapters = null)
    {
        var chapterInfo = ToChapterInfo(chapters);
        if (_settings.EmbedBookInfo)
            WriteBookInfo(source, book, cover);

        void Report(AAXClean.Mp4Operation op) =>
            op.ConversionProgressUpdate += (_, e) =>
                progress?.Report(new DownloadProgress(DownloadPhase.Decrypting, e.FractionCompleted * 100,
                    $"Converting… {e.ProcessPosition:hh\\:mm\\:ss} / {e.EndTime:hh\\:mm\\:ss}"));

        if (_settings.SplitByChapter)
        {
            chapterInfo ??= await ReadChaptersAsync(source, ct);
            if (chapterInfo is not null)
                return await SplitIntoChaptersAsync(source, chapterInfo, output, Report, ct);

            Log.Warn($"'{book.Title}' has no usable chapter list, so it was saved as one file instead of one per chapter.");
        }

        // Write into a partial file, so an interrupted conversion cannot be mistaken
        // for a finished one. The MP3 encoder writes its header at the end, so the
        // file has to be readable and seekable as well.
        var partPath = output + PartExtension;
        try
        {
            await using (var destination = new FileStream(partPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true))
            {
                var op = WantsMp3
                    ? AAXClean.Codecs.Mp4FileExtensions.ConvertToMp3Async(source, destination, source.GetDefaultLameConfig(), chapterInfo)
                    : source.ConvertToMp4aAsync(destination, chapterInfo);
                Report(op);
                op.Start();
                await op.OperationTask.WaitAsync(ct);

                // The converter closes the stream itself when it finishes; flushing a
                // closed stream throws, which used to fail every finished conversion.
                if (destination.CanWrite)
                    await destination.FlushAsync(ct);
            }

            return Promote(partPath, output);
        }
        catch
        {
            DeleteFile(partPath);
            throw;
        }
    }

    /// <summary>
    /// One file per chapter, numbered so they play in order, in a folder named after
    /// the book. Returns the folder.
    /// </summary>
    private async Task<string> SplitIntoChaptersAsync(AAXClean.Mp4File aax, Mpeg4Lib.ChapterInfo chapterInfo,
        string output, Action<AAXClean.Mp4Operation> report, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(output)!;
        var name = Path.GetFileNameWithoutExtension(output);
        var folder = Path.Combine(parent, name);
        for (var n = 2; Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(); n++)
            folder = Path.Combine(parent, $"{name} ({n})");
        Directory.CreateDirectory(folder);

        var extension = WantsMp3 ? ".mp3" : ".m4b";
        var count = chapterInfo.Count;
        var width = Math.Max(2, count.ToString().Length);
        var opened = new List<Stream>();

        string FileFor(int number, string? title) =>
            Path.Combine(folder, $"{number.ToString().PadLeft(width, '0')} - " +
                                 SafeName(string.IsNullOrWhiteSpace(title) ? $"Chapter {number}" : title, 100) + extension);

        try
        {
            AAXClean.Mp4Operation op;
            if (WantsMp3)
            {
                var lame = aax.GetDefaultLameConfig();
                var number = 0;
                op = AAXClean.Codecs.Mp4FileExtensions.ConvertToMultiMp3Async(aax, chapterInfo, split =>
                {
                    number++;
                    split.TrackNumber = number;
                    split.TrackCount = count;
                    split.TrackTitle = split.Chapter.Title;
                    split.LameConfig = lame;
                    var stream = new FileStream(FileFor(number, split.Chapter.Title), FileMode.Create, FileAccess.ReadWrite);
                    opened.Add(stream);
                    split.OutputFile = stream;
                }, lame);
            }
            else
            {
                var number = 0;
                op = aax.ConvertToMultiMp4aAsync(chapterInfo, split =>
                {
                    number++;
                    split.TrackNumber = number;
                    split.TrackCount = count;
                    split.TrackTitle = split.Chapter.Title;
                    var stream = new FileStream(FileFor(number, split.Chapter.Title), FileMode.Create, FileAccess.ReadWrite);
                    opened.Add(stream);
                    split.OutputFile = stream;
                });
            }

            report(op);
            op.Start();
            await op.OperationTask.WaitAsync(ct);
            Log.Info($"Split into {count} chapter files in {folder}.");
            return folder;
        }
        finally
        {
            foreach (var stream in opened)
                try { await stream.DisposeAsync(); } catch { /* already closed */ }
        }
    }

    private static Mpeg4Lib.ChapterInfo? ToChapterInfo(IReadOnlyList<ChapterMark>? chapters)
    {
        if (chapters is not { Count: > 0 })
            return null;

        var info = new Mpeg4Lib.ChapterInfo(chapters[0].Start);
        foreach (var chapter in chapters.Where(c => c.Length > TimeSpan.Zero))
            info.AddChapter(chapter.Title, chapter.Length);
        return info.Count > 0 ? info : null;
    }

    /// <summary>
    /// The chapter list inside the file, or null when there is none or it cannot be
    /// right: chapters that together cover less than nine tenths of the book would
    /// cut it into slivers.
    /// </summary>
    private static async Task<Mpeg4Lib.ChapterInfo?> ReadChaptersAsync(AAXClean.Mp4File source, CancellationToken ct)
    {
        // The file's own chapter track, read by this app's reader: the decryption
        // library's reader gets the times wrong in files whose chapter track keeps
        // its own time units.
        if (source.InputStream is FileStream { Name: { } path } && File.Exists(path))
        {
            var marks = await Task.Run(() => Mp4Chapters.Read(path), ct);
            if (marks.Count >= 2)
            {
                var list = new List<ChapterMark>();
                for (var i = 0; i < marks.Count; i++)
                {
                    var end = i + 1 < marks.Count ? marks[i + 1].Start : source.Duration;
                    if (end > marks[i].Start)
                        list.Add(new ChapterMark(marks[i].Title, marks[i].Start, end - marks[i].Start));
                }

                var covered = list.Sum(c => c.Length.TotalSeconds);
                if (list.Count >= 2 && covered >= source.Duration.TotalSeconds * 0.9)
                    return ToChapterInfo(list);
            }
        }

        Log.Warn("No usable chapter list was found in the file.");
        return null;
    }

    /// <summary>
    /// Puts the description, series, narrator and cover into the book's own tags, so
    /// any player shows them. Only fills what Audible left empty, and never fails a
    /// download over it.
    /// </summary>
    private static void WriteBookInfo(AAXClean.Mp4File aax, BookInfo book, byte[]? cover)
    {
        try
        {
            var tags = aax.AppleTags;
            if (tags is null)
                return;

            if (!DisplayText.IsMissing(book.Description) && DisplayText.IsMissing(tags.LongDescription))
                tags.LongDescription = book.Description;
            if (!DisplayText.IsMissing(book.Description) && DisplayText.IsMissing(tags.Comment))
                tags.Comment = book.Description!.Length > 250 ? book.Description[..250].TrimEnd() + "…" : book.Description;
            if (!DisplayText.IsMissing(book.Narrator) && DisplayText.IsMissing(tags.Narrator))
                tags.Narrator = book.Narrator;
            if (!DisplayText.IsMissing(book.Series) && DisplayText.IsMissing(tags.Album))
                tags.Album = book.Series;
            if (cover is { Length: > 0 } && tags.Cover is not { Length: > 0 })
                tags.Cover = cover;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the book's details into the file: {ex.Message}");
        }
    }

    private static readonly HttpClient CoverHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Fetches the cover picture, saving it next to the book as "name.jpg" when the
    /// setting is on. Returns the picture for the tags, or null. Never fails a download.
    /// </summary>
    private async Task<byte[]?> SaveCoverAsync(BookInfo book, string bookPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(book.CoverUrl) || (!_settings.SaveCoverImage && !_settings.EmbedBookInfo))
            return null;

        try
        {
            var bytes = await CoverHttp.GetByteArrayAsync(book.CoverUrl, ct);
            if (bytes.Length < 100)
                return null;

            if (_settings.SaveCoverImage)
            {
                var jpg = Path.Combine(Path.GetDirectoryName(bookPath)!, Path.GetFileNameWithoutExtension(bookPath) + ".jpg");
                if (!File.Exists(jpg))
                    await File.WriteAllBytesAsync(jpg, bytes, ct);
            }

            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Could not fetch the cover of '{book.Title}': {ex.Message}");
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

/// <summary>
/// One speed limit shared by every download running at the same time, so two
/// parallel downloads together stay under it.
/// </summary>
internal static class SpeedLimit
{
    private static readonly object Gate = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _allowedAt;   // seconds on the clock when the next byte may go

    public static Task WaitAsync(int bytes, int kilobytesPerSecond, CancellationToken ct)
    {
        if (kilobytesPerSecond <= 0)
            return Task.CompletedTask;

        double wait;
        lock (Gate)
        {
            var now = Clock.Elapsed.TotalSeconds;
            // Unused allowance does not pile up for more than a second.
            _allowedAt = Math.Max(_allowedAt, now - 1) + bytes / (kilobytesPerSecond * 1024.0);
            wait = _allowedAt - now;
        }

        return wait > 0.005 ? Task.Delay(TimeSpan.FromSeconds(wait), ct) : Task.CompletedTask;
    }
}
