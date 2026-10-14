using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudiobookDownloader.Core;

/// <summary>
/// LibriVox: free public-domain audiobooks read by volunteers.
/// No login, no DRM — downloads are plain zip files of mp3 chapters.
///
/// Search covers the whole catalogue rather than a first page. The API serves
/// very large pages, so the entire catalogue is read in a handful of requests
/// and cached on disk; after that first read every search is instant and
/// complete. The old code asked for 100 titles once and searched only those,
/// which is why searching for almost any LibriVox book found nothing.
/// </summary>
public sealed class LibriVoxProvider : IBookProvider, ILanguageFilter
{
    public const string ProviderId = "librivox";

    private const string Fields =
        "id,title,authors,language,totaltimesecs,url_zip_file,url_librivox";

    /// <summary>Rows fetched when browsing with no search text.</summary>
    private const int BrowseCount = 100;

    /// <summary>
    /// Largest page the API honours. It used to take 10000; it now answers anything
    /// over 500 with HTTP 400 "Too many records requested", which the old code took
    /// for the end of the catalogue — so every LibriVox search found nothing.
    /// </summary>
    private const int DefaultPageSize = 500;

    /// <summary>Stops an endless loop if the API ever keeps claiming there is more.</summary>
    private const int MaxPages = 150;

    /// <summary>Most rows handed to the UI for one search.</summary>
    private const int MaxMatches = 200;

    // 2: every book also records its language, for the language filter.
    private const int CacheFormatVersion = 2;

    /// <summary>How long a downloaded catalogue is reused before it is rebuilt.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private volatile List<BookInfo>? _catalog;

    /// <summary>Page size used when paging the catalogue.</summary>
    public int CatalogPageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Pages fetched at the same time. With pages of 500 the catalogue is some fifty
    /// requests, and one after another that is well over a minute.
    /// </summary>
    public int ParallelPages { get; set; } = 6;

    /// <summary>Where the downloaded catalogue is kept between runs.</summary>
    public string CacheFilePath { get; set; } = DefaultCacheFilePath();

    /// <summary>The catalogue endpoint. Overridable so tests can serve their own.</summary>
    public string ApiBaseUrl { get; set; } = "https://librivox.org/api/feed/audiobooks";

    /// <summary>How many titles the catalogue currently holds (0 until one is read).</summary>
    public int SearchableTitleCount => _catalog?.Count ?? 0;

    public string Id => ProviderId;
    public string DisplayName => "LibriVox (free, public domain)";
    public string Description => "Search thousands of free public-domain audiobooks. No account needed, no DRM.";
    public bool RequiresLogin => false;
    public bool HasDrm => false;
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void Disconnect() => _catalog = null;

    private readonly AppSettings? _settings;
    private string _language = "";

    public LibriVoxProvider(AppSettings? settings = null) => _settings = settings;

    /// <summary>The languages LibriVox volunteers read in most; the code is the name the catalogue uses.</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        .. new[]
        {
            "English", "German", "Spanish", "French", "Italian", "Dutch", "Portuguese", "Russian",
            "Polish", "Chinese", "Japanese", "Latin", "Greek", "Ancient Greek", "Hebrew", "Finnish",
            "Swedish", "Danish", "Hungarian", "Tagalog", "Bulgarian", "Esperanto", "Hindi", "Tamil",
        }.Select(name => new LanguageOption(name, name)),
    ];

    IReadOnlyList<LanguageOption> ILanguageFilter.SupportedLanguages => Languages;

    /// <summary>The language searches are narrowed to; empty means every language.</summary>
    public string CurrentLanguage
    {
        get => _settings?.LibriVoxLanguage ?? _language;
        set
        {
            _language = value ?? "";
            if (_settings is not null)
                _settings.LibriVoxLanguage = _language;
        }
    }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default)
    {
        var language = CurrentLanguage;

        // Browsing: one cheap request, no need to read the whole catalogue.
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrEmpty(language))
            return (await FetchPageAsync(0, BrowseCount, ct)).Books;

        var catalog = await EnsureCatalogAsync(ct);
        if (catalog.Count == 0)
            throw new InvalidOperationException(
                "The LibriVox catalogue could not be read. Check your internet connection and try again; " +
                "the activity log has the details.");

        if (!string.IsNullOrEmpty(language))
            catalog = catalog.Where(b => string.Equals(b.Language, language, StringComparison.OrdinalIgnoreCase)).ToList();

        if (string.IsNullOrWhiteSpace(query))
            return catalog.Take(MaxMatches).ToList();

        var matches = Search(catalog, query, MaxMatches);

        var showing = matches.Count == MaxMatches ? $" (showing the first {MaxMatches})" : string.Empty;
        Log.Info(matches.Count == 0
            ? $"LibriVox: nothing in {catalog.Count:n0} titles matched \"{query.Trim()}\"."
            : $"LibriVox: {matches.Count} match(es) for \"{query.Trim()}\" out of {catalog.Count:n0} titles{showing}.");
        return matches;
    }

    public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(book.DownloadUrl))
            throw new InvalidOperationException("This book has no download URL.");

        // LibriVox serves plain 64kbps mp3 zips.
        return Task.FromResult(new DownloadTicket(
            book.BookId, TicketFormat.PlainUrl, new Uri(book.DownloadUrl), null, null, null,
            SuggestedExtension: ".zip"));
    }

    /// <summary>
    /// Match every word of the query against the title or the author, so
    /// "pride prejudice" finds "Pride and Prejudice" and "austen emma" works.
    /// Title matches that contain the whole query are listed first.
    /// </summary>
    public static List<BookInfo> Search(IReadOnlyList<BookInfo> catalog, string query, int max)
    {
        var words = query.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || max <= 0)
            return [];

        var needle = query.Trim();
        var best = new List<BookInfo>();
        var rest = new List<BookInfo>();

        foreach (var book in catalog)
        {
            var matchesAll = true;
            foreach (var word in words)
            {
                if (!book.Title.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                    !book.Author.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    matchesAll = false;
                    break;
                }
            }
            if (!matchesAll)
                continue;

            if (book.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
                best.Add(book);
            else
                rest.Add(book);
        }

        best.AddRange(rest);
        if (best.Count > max)
            best.RemoveRange(max, best.Count - max);
        return best;
    }

    /// <summary>
    /// The full searchable catalogue: from memory, else from a fresh cache file,
    /// else paged down from the API and cached.
    /// </summary>
    private async Task<IReadOnlyList<BookInfo>> EnsureCatalogAsync(CancellationToken ct)
    {
        var cached = _catalog;
        if (cached is not null)
            return cached;

        await _catalogGate.WaitAsync(ct);
        try
        {
            if (_catalog is not null)
                return _catalog;

            // Reading and parsing a few megabytes should not stall the window.
            var fromDisk = await Task.Run(TryLoadCache, ct);
            if (fromDisk is not null)
            {
                Log.Info($"LibriVox: loaded {fromDisk.Count:n0} searchable titles from the saved catalogue.");
                _catalog = fromDisk;
                return fromDisk;
            }

            var (books, complete) = await DownloadCatalogAsync(ct);

            // Partial results still beat nothing, but they are never cached, or a
            // single flaky moment would leave searches incomplete for a week.
            if (books.Count > 0)
                _catalog = books;
            if (complete)
                await SaveCacheAsync(books);

            return books;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private async Task<(List<BookInfo> Books, bool Complete)> DownloadCatalogAsync(CancellationToken ct)
    {
        var books = new List<BookInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var complete = false;

        try
        {
            var wave = Math.Max(1, ParallelPages);

            for (var page = 0; page < MaxPages && !complete; page += wave)
            {
                Log.Info(page == 0
                    ? "LibriVox: reading the whole catalogue to make it searchable (first time only)…"
                    : $"LibriVox: catalogue so far — {books.Count:n0} titles…");

                var pages = await Task.WhenAll(Enumerable
                    .Range(page, Math.Min(wave, MaxPages - page))
                    .Select(p => FetchPageAsync(p * CatalogPageSize, CatalogPageSize, ct)));

                foreach (var (batch, endOfCatalogue) in pages)
                {
                    // Only the API saying "there is nothing here" ends the read. A page
                    // also comes back short when a few records are unusable, and taking
                    // that for the end would quietly search part of the catalogue —
                    // which is exactly what the live check caught.
                    if (endOfCatalogue || batch.Count == 0)
                    {
                        if (books.Count == 0)
                        {
                            Log.Error("LibriVox: the catalogue returned no books. " +
                                      "Check your internet connection and try again.");
                            return (books, false);
                        }
                        complete = true;
                        break;
                    }

                    foreach (var book in batch)
                    {
                        if (seen.Add(book.BookId))
                            books.Add(book);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"LibriVox: could not download the catalogue: {ex.Message}");
        }

        if (complete)
            Log.Info($"LibriVox: catalogue ready — {books.Count:n0} titles are searchable.");
        else if (books.Count > 0)
            Log.Warn($"LibriVox: catalogue download stopped early, so searching {books.Count:n0} titles. " +
                     "Results may be incomplete; try again in a moment.");

        return (books, complete);
    }

    /// <summary>
    /// One server-side page. Past the end of the catalogue the API answers HTTP
    /// 404 with an {"error": …} body, and that is the only signal telling us the
    /// read is complete — the number of rows that come back cannot be trusted,
    /// because unusable records are filtered out here.
    /// </summary>
    private async Task<(List<BookInfo> Books, bool EndOfCatalogue)> FetchPageAsync(
        int offset, int limit, CancellationToken ct)
    {
        var url = $"{ApiBaseUrl}?format=json&fields={Fields}&offset={offset}&limit={limit}";

        using var resp = await Http.GetAsync(url, ct);

        // 404 "could not be found" is the end of the catalogue. Anything else that
        // fails — 400 "too many records" included — is a real error, never an end.
        if (resp.StatusCode is HttpStatusCode.NotFound)
            return ([], true);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"LibriVox answered {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");

        var feed = await resp.Content.ReadFromJsonAsync<LibriVoxFeed>(cancellationToken: ct);

        var books = feed?.Books?
            .Where(b => !string.IsNullOrWhiteSpace(b.Title) && !string.IsNullOrWhiteSpace(b.ZipUrl))
            .Select(ToBook)
            .ToList() ?? [];

        return (books, false);
    }

    private static BookInfo ToBook(LibriVoxBook b) => new(
        ProviderId,
        b.Id?.ToString() ?? b.Title!,
        b.Title!.Trim(),
        DescribeAuthors(b.Authors),
        "",                                    // the catalogue feed does not expose narrators
        (b.TotalTimeSecs ?? 0) / 60,
        null,
        b.ZipUrl,
        string.IsNullOrWhiteSpace(b.Language) ? "Public domain" : $"{b.Language} · Public domain")
    {
        Language = b.Language?.Trim(),
    };

    private static string DescribeAuthors(LibriVoxAuthor[]? authors)
    {
        var names = new List<string>();
        foreach (var author in authors ?? [])
        {
            var name = string.Join(' ', new[] { author.FirstName?.Trim(), author.LastName?.Trim() }
                .Where(part => !string.IsNullOrEmpty(part)));
            if (name.Length > 0)
                names.Add(name);
        }

        return names.Count > 0 ? string.Join(", ", names) : "Unknown";
    }

    private static string DefaultCacheFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudiobookDownloader", "librivox-catalog.json");

    private List<BookInfo>? TryLoadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
                return null;

            var cache = JsonSerializer.Deserialize<CatalogCache>(File.ReadAllText(CacheFilePath), CacheJson);
            if (cache is null || cache.Version != CacheFormatVersion || cache.Books is not { Count: > 0 })
                return null;

            if (DateTimeOffset.UtcNow - cache.BuiltAt > CacheLifetime)
            {
                Log.Info("LibriVox: the saved catalogue is over a week old; reading a fresh one.");
                return null;
            }

            return cache.Books;
        }
        catch (Exception ex)
        {
            Log.Warn($"LibriVox: could not read the saved catalogue ({ex.Message}); reading a fresh one.");
            return null;
        }
    }

    private async Task SaveCacheAsync(List<BookInfo> books)
    {
        try
        {
            var json = await Task.Run(() => JsonSerializer.Serialize(
                new CatalogCache(CacheFormatVersion, DateTimeOffset.UtcNow, books), CacheJson));

            var folder = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            // Write beside the real file, then swap, so a failure never leaves
            // a half-written catalogue behind.
            var temp = CacheFilePath + ".tmp";
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, CacheFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"LibriVox: could not save the catalogue for next time: {ex.Message}");
        }
    }
}

/// <summary>The downloaded catalogue, kept between runs so search stays instant.</summary>
internal sealed record CatalogCache(int Version, DateTimeOffset BuiltAt, List<BookInfo> Books);

#pragma warning disable CS8618 // non-nullable JSON DTO properties
internal sealed class LibriVoxFeed
{
    [JsonPropertyName("books")]
    public LibriVoxBook[]? Books { get; set; }
}

internal sealed class LibriVoxBook
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }

    /// <summary>
    /// The API returns authors as an array of objects. The old code asked for a
    /// singular "author" string and a "url_zip_spec" field, neither of which
    /// exists, so every book was dropped.
    /// </summary>
    [JsonPropertyName("authors")] public LibriVoxAuthor[]? Authors { get; set; }

    [JsonPropertyName("url_zip_file")] public string? ZipUrl { get; set; }
    [JsonPropertyName("url_librivox")] public string? PageUrl { get; set; }
    [JsonPropertyName("totaltimesecs")] public int? TotalTimeSecs { get; set; }
}

internal sealed class LibriVoxAuthor
{
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
}
#pragma warning restore CS8618
