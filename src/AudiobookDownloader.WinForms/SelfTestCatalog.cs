using System.Net;
using System.Net.Sockets;
using System.Text;
using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Offline checks that LibriVox search reads the whole catalogue: the API is
/// paged until it ends, titles that live far past the old 100-title limit are
/// found, and a downloaded catalogue is reused instead of re-read.
/// A tiny loopback server stands in for librivox.org, so this never touches
/// the network and never writes to the real catalogue cache.
/// </summary>
internal static class SelfTestCatalog
{
    /// <summary>Small page size, so a few hundred books need several requests.</summary>
    private const int PageSize = 100;

    public static void Run(Action<string> line, Action<string> fail, bool live = false)
    {
        var cacheFile = Path.Combine(Path.GetTempPath(), "audiobook-selftest-librivox-catalog.json");
        DeleteCache(cacheFile);

        try
        {
            // Every check that waits on asynchronous work must run on a pool
            // thread: this one has a UI synchronization context, so blocking it
            // while an await wants to come back here would deadlock.
            Task.Run(() =>
            {
                CheckPagingAndSearch(cacheFile, line, fail);
                CheckCatalogueEndsWithNotFound(cacheFile, line, fail);
                CheckEmptyCatalogueIsNotCached(cacheFile, line, fail);
                CheckShortPagesAreNotTheEnd(cacheFile, line, fail);
                if (live)
                    CheckAgainstTheRealCatalogue(line, fail);
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            fail($"the catalogue checks could not run: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DeleteCache(cacheFile);
        }
    }

    private static void DeleteCache(string cacheFile)
    {
        try { if (File.Exists(cacheFile)) File.Delete(cacheFile); } catch { /* best effort */ }
        try { if (File.Exists(cacheFile + ".tmp")) File.Delete(cacheFile + ".tmp"); } catch { /* best effort */ }
    }

    private static void CheckPagingAndSearch(string cacheFile, Action<string> line, Action<string> fail)
    {
        line("== LibriVox catalogue search ==");

        using var server = new TinyCatalogServer(250);
        var provider = NewProvider(server, cacheFile);

        // 1) The title under test sits at position 150, well past the first 100
        //    titles the old implementation would ever have seen.
        var matches = provider.GetBooksAsync("pride prejudice").GetAwaiter().GetResult();

        if (matches.Count == 0)
            fail("LibriVox: a title on page 2 of the catalogue could not be found");
        else
            line($"   found \"{matches[0].Title}\" by {matches[0].Author} (catalogue position 150)");

        // 2) Reading the catalogue must walk it in pages and stop only when the
        //    API says there is nothing more.
        var offsets = server.Offsets;
        int[] expected = [0, PageSize, PageSize * 2, PageSize * 3];
        if (!offsets.SequenceEqual(expected))
            fail("LibriVox: expected catalogue pages at " +
                 $"[{string.Join(", ", expected)}] but asked for [{string.Join(", ", offsets)}]");
        else if (server.Requests != 4)
            fail($"LibriVox: reading 250 titles in pages of {PageSize} took {server.Requests} requests, expected 4");
        else
            line($"   250 titles read in {server.Requests} paged requests, then stopped");

        // 3) A second search must not re-read the API.
        var after = server.Requests;
        var more = provider.GetBooksAsync("filler 240").GetAwaiter().GetResult();
        if (more.Count == 0)
            fail("LibriVox: a title on the last page of the catalogue could not be found");
        else if (server.Requests != after)
            fail("LibriVox: a repeat search re-read the catalogue instead of using it");
        else
            line($"   a second search reused the catalogue with no further requests (\"{more[0].Title}\")");

        // 4) A new instance must take the saved catalogue rather than re-read it.
        if (!File.Exists(cacheFile))
        {
            fail("LibriVox: the catalogue was not saved, so every run would re-read it");
        }
        else
        {
            var second = NewProvider(server, cacheFile);
            var reused = second.GetBooksAsync("pride prejudice").GetAwaiter().GetResult();
            if (reused.Count == 0)
                fail("LibriVox: the saved catalogue could not be searched");
            else if (server.Requests != after)
                fail("LibriVox: a saved catalogue was ignored and the API read again");
            else
                line($"   a saved catalogue of {new FileInfo(cacheFile).Length:n0} bytes was reused " +
                     "without touching the API");
        }

        // 5) Browsing with no search text must stay a single cheap request.
        var beforeBrowse = server.Requests;
        var browse = provider.GetBooksAsync("").GetAwaiter().GetResult();
        if (browse.Count != PageSize)
            fail($"LibriVox: browsing returned {browse.Count} titles, expected the first {PageSize}");
        else if (server.Requests != beforeBrowse + 1)
            fail($"LibriVox: browsing cost {server.Requests - beforeBrowse} requests, expected 1");
        else
            line($"   browsing with no search text costs 1 request and lists {browse.Count} titles");

        // 6) Every word must match, across title and author.
        if (LibriVoxProvider.Search(browse, "pride prejudice", 200).Count != 0)
            fail("LibriVox: searching the first page alone should not have matched the page-2 title");
        if (provider.GetBooksAsync("austen pride").GetAwaiter().GetResult().Count == 0)
            fail("LibriVox: a query matching one word in the title and one in the author found nothing");

        line("");
    }

    private static void CheckCatalogueEndsWithNotFound(string cacheFile, Action<string> line, Action<string> fail)
    {
        DeleteCache(cacheFile);

        // Exactly 200 titles: two full pages, so only the "past the end" answer
        // (HTTP 404, as the real API sends) can tell the loop it has finished.
        using var server = new TinyCatalogServer(200);
        var provider = NewProvider(server, cacheFile);

        var matches = provider.GetBooksAsync("pride prejudice").GetAwaiter().GetResult();
        if (matches.Count == 0)
            fail("LibriVox: the catalogue could not be read when it ends on a page boundary");
        else if (!server.Offsets.SequenceEqual([0, PageSize, PageSize * 2]))
            fail($"LibriVox: expected pages at 0, {PageSize} and {PageSize * 2} when the catalogue ends " +
                 $"on a boundary, got [{string.Join(", ", server.Offsets)}]");
        else
            line("   a catalogue ending on a page boundary stops cleanly on the not-found answer");
    }

    private static void CheckEmptyCatalogueIsNotCached(string cacheFile, Action<string> line, Action<string> fail)
    {
        DeleteCache(cacheFile);

        using var server = new TinyCatalogServer(0);
        var provider = NewProvider(server, cacheFile);

        // A catalogue that cannot be read is now said out loud (an error the window
        // shows) instead of being passed off as a search that found nothing.
        IReadOnlyList<BookInfo> matches;
        try { matches = provider.GetBooksAsync("anything").GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { matches = []; }

        if (matches.Count != 0)
            fail("LibriVox: an empty catalogue produced results");
        else if (File.Exists(cacheFile))
            fail("LibriVox: an unreadable catalogue was saved, so the app would look empty for a week");
        else
            line("   a catalogue that could not be read is not saved, so the next search tries again");
    }

    /// <summary>
    /// Opt-in, and the only check here that touches the network. It reads the
    /// real catalogue, so a change to the LibriVox API — a renamed field, say —
    /// is caught here instead of by users staring at an empty list. It uses the
    /// app's own catalogue cache, so running it also warms that cache up.
    /// </summary>
    private static void CheckAgainstTheRealCatalogue(Action<string> line, Action<string> fail)
    {
        line("== LibriVox live catalogue (uses the network) ==");

        var provider = new LibriVoxProvider();
        var started = DateTime.UtcNow;
        IReadOnlyList<BookInfo> matches;

        try
        {
            matches = provider.GetBooksAsync("pride prejudice").GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            fail($"LibriVox: the live catalogue could not be read: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var seconds = (DateTime.UtcNow - started).TotalSeconds;

        if (matches.Count == 0)
        {
            fail("LibriVox: no live titles came back (offline, or the catalogue API changed)");
            return;
        }

        line($"   {provider.SearchableTitleCount:n0} live titles searchable, read in {seconds:n1}s");

        var book = matches.FirstOrDefault(
            b => b.Title.Contains("Pride and Prejudice", StringComparison.OrdinalIgnoreCase));

        if (book is null)
            fail("LibriVox: the live catalogue does not contain \"Pride and Prejudice\"");
        else if (!book.Author.Contains("Austen", StringComparison.OrdinalIgnoreCase))
            fail($"LibriVox: the author came back as \"{book.Author}\", so the API fields have changed");
        else if (string.IsNullOrWhiteSpace(book.DownloadUrl))
            fail("LibriVox: a live result has no download link");
        else if (book.LengthMinutes <= 0)
            fail("LibriVox: a live result has no length, so the API fields have changed");
        else
            line($"   found \"{book.Title}\" by {book.Author}, {book.LengthMinutes} minutes long");

        if (provider.SearchableTitleCount < 15_000)
            fail($"LibriVox: only {provider.SearchableTitleCount:n0} titles were read, so the whole " +
                 "catalogue is not being searched");
        else if (!File.Exists(provider.CacheFilePath))
            fail("LibriVox: the live catalogue was not saved for the next run");
        else
            line($"   saved for the next run at {provider.CacheFilePath}");

        line("");
    }

    /// <summary>
    /// A page can come back shorter than asked for without being the end of the
    /// catalogue: records the app cannot use are dropped, and the live API
    /// returns fewer rows than requested for its own reasons. Stopping there
    /// silently left most of the catalogue unsearchable.
    /// </summary>
    private static void CheckShortPagesAreNotTheEnd(string cacheFile, Action<string> line, Action<string> fail)
    {
        DeleteCache(cacheFile);

        // Every tenth title has no download link, so every full page comes back short.
        using var server = new TinyCatalogServer(250) { DropEveryNth = 10 };
        var provider = NewProvider(server, cacheFile);

        var matches = provider.GetBooksAsync("pride prejudice").GetAwaiter().GetResult();

        if (matches.Count == 0)
        {
            fail("LibriVox: a title was lost because a short page was taken for the end of the catalogue");
            return;
        }

        if (!server.Offsets.SequenceEqual([0, PageSize, PageSize * 2, PageSize * 3]))
            fail("LibriVox: a page shorter than requested stopped the read, so only part of the " +
                 $"catalogue is searchable (pages asked for: [{string.Join(", ", server.Offsets)}])");
        else
            line($"   short pages did not end the read ({provider.SearchableTitleCount} titles searchable)");
    }

    private static LibriVoxProvider NewProvider(TinyCatalogServer server, string cacheFile) => new()
    {
        CatalogPageSize = PageSize,
        // One page at a time, so the request counts below are exact.
        ParallelPages = 1,
        CacheFilePath = cacheFile,
        ApiBaseUrl = $"http://127.0.0.1:{server.Port}/api/feed/audiobooks",
    };

    // ------------------------------------------------------------------
    // A minimal stand-in for the LibriVox catalogue API
    // ------------------------------------------------------------------

    /// <summary>
    /// Serves a synthetic catalogue of <c>count</c> titles in pages, answering
    /// HTTP 404 with an error body past the end — exactly like the real API.
    /// </summary>
    private sealed class TinyCatalogServer : IDisposable
    {
        private readonly int _count;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<int> _offsets = [];
        private int _requests;

        public TinyCatalogServer(int count)
        {
            _count = count;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Requests => Volatile.Read(ref _requests);

        /// <summary>Omit the download link on every n-th title, so pages come back short.</summary>
        public int DropEveryNth { get; init; }

        /// <summary>The offset of every catalogue page that was asked for, in order.</summary>
        public List<int> Offsets
        {
            get { lock (_offsets) return [.. _offsets]; }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { return; }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var target = await ReadTargetAsync(stream, _cts.Token);
                    if (target is null)
                        return;

                    Interlocked.Increment(ref _requests);

                    var (offset, limit) = ParseQuery(target);
                    lock (_offsets) _offsets.Add(offset);

                    string body;
                    string status;
                    if (offset >= _count || limit <= 0)
                    {
                        status = "404 Not Found";
                        body = "{\"error\":\"Audiobooks could not be found\"}";
                    }
                    else
                    {
                        status = "200 OK";
                        var end = Math.Min(offset + limit, _count);
                        var json = new StringBuilder("{\"books\":[");
                        for (var i = offset; i < end; i++)
                        {
                            if (i > offset)
                                json.Append(',');
                            json.Append(Record(i));
                        }
                        json.Append("]}");
                        body = json.ToString();
                    }

                    var payload = Encoding.UTF8.GetBytes(body);
                    var head = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\n" +
                               $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _cts.Token);
                    await stream.WriteAsync(payload, _cts.Token);
                }
            }
            catch
            {
                // A client that goes away is normal for these tests.
            }
        }

        /// <summary>Two real titles buried past the first page, then filler.</summary>
        private (string Title, string Author) BookAt(int index) => index switch
        {
            150 => ("Pride and Prejudice", "Jane Austen"),
            151 => ("Pride and Prejudice (version 2)", "Jane Austen"),
            _ => ($"Filler Item {index}", $"Generic Writer {index % 7}"),
        };

        private string Record(int index)
        {
            var (title, author) = BookAt(index);
            var id = 1000 + index;
            var lastSpace = author.LastIndexOf(' ');
            var first = author[..lastSpace];
            var last = author[(lastSpace + 1)..];

            var zip = DropEveryNth > 0 && index % DropEveryNth == 0
                ? string.Empty
                : $"\"url_zip_file\":\"https://example.invalid/{id}.zip\",";

            return $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"language\":\"English\"," +
                   zip +
                   $"\"url_librivox\":\"https://example.invalid/{id}\"," +
                   $"\"totaltimesecs\":3600," +
                   $"\"authors\":[{{\"first_name\":\"{first}\",\"last_name\":\"{last}\"}}]}}";
        }

        private static (int Offset, int Limit) ParseQuery(string target)
        {
            var question = target.IndexOf('?');
            if (question < 0)
                return (0, 0);

            var offset = 0;
            var limit = 0;
            foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = pair[..equals];
                var value = pair[(equals + 1)..];
                if (key.Equals("offset", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out offset);
                else if (key.Equals("limit", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out limit);
            }

            return (offset, limit);
        }

        private static async Task<string?> ReadTargetAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var text = new StringBuilder();
            var total = 0;

            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
                if (read <= 0)
                    break;

                text.Append(Encoding.ASCII.GetString(buffer, total, read));
                total += read;

                if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            var request = text.ToString();
            if (request.Length == 0)
                return null;

            var line = request.Split("\r\n")[0];
            var parts = line.Split(' ');
            return parts.Length >= 2 ? parts[1] : null;
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* best effort */ }
            try { _listener.Stop(); } catch { /* best effort */ }
            _cts.Dispose();
        }
    }
}
