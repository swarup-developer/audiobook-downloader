using System.Net.Http;
using System.Text.Json;

namespace AudiobookDownloader.Core;

/// <summary>
/// Project Gutenberg: over 70,000 free public-domain books in many languages,
/// including English, Hindi and dozens more. Search goes through the open
/// Gutendex API, which mirrors Project Gutenberg's catalogue and understands
/// language filters. No account, no DRM.
/// </summary>
public sealed class GutenbergProvider : IBookProvider, ILanguageFilter
{
    public const string ProviderId = "gutenberg";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // Gutendex asks clients to identify themselves.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookDownloader/1.0");
        return client;
    }

    private readonly AppSettings _settings;

    public GutenbergProvider(AppSettings settings) => _settings = settings;

    public string Id => ProviderId;
    public string DisplayName => "Project Gutenberg (free books)";
    public string Description =>
        "Search Project Gutenberg in English, Hindi and many other languages. Free, public domain, no account.";
    public bool RequiresLogin => false;
    public bool HasDrm => false;
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Disconnect() { }

    // ------------------------------------------------------------------
    // Language support
    // ------------------------------------------------------------------

    /// <summary>Shown first in the language selector: search every language.</summary>
    public static LanguageOption AllLanguages { get; } = new("", "All languages");

    /// <summary>
    /// Every language Project Gutenberg currently publishes in, with its short
    /// code. Kept in one place so the selector, the search and the settings all
    /// agree.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English"),
        new("hi", "Hindi"),
        new("ar", "Arabic"),
        new("bn", "Bengali"),
        new("ca", "Catalan"),
        new("zh", "Chinese"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("nl", "Dutch"),
        new("eo", "Esperanto"),
        new("et", "Estonian"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("fy", "Frisian"),
        new("gl", "Galician"),
        new("de", "German"),
        new("el", "Greek"),
        new("gu", "Gujarati"),
        new("he", "Hebrew"),
        new("hu", "Hungarian"),
        new("is", "Icelandic"),
        new("id", "Indonesian"),
        new("ga", "Irish"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("kn", "Kannada"),
        new("ko", "Korean"),
        new("la", "Latin"),
        new("lv", "Latvian"),
        new("lt", "Lithuanian"),
        new("ml", "Malayalam"),
        new("mr", "Marathi"),
        new("ne", "Nepali"),
        new("no", "Norwegian"),
        new("fa", "Persian"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("pa", "Punjabi"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sa", "Sanskrit"),
        new("sr", "Serbian"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("es", "Spanish"),
        new("sw", "Swahili"),
        new("sv", "Swedish"),
        new("tl", "Tagalog"),
        new("ta", "Tamil"),
        new("te", "Telugu"),
        new("th", "Thai"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("ur", "Urdu"),
        new("vi", "Vietnamese"),
        new("cy", "Welsh"),
        new("yi", "Yiddish"),
    ];

    IReadOnlyList<LanguageOption> ILanguageFilter.SupportedLanguages => Languages;

    /// <summary>Currently selected language code ("" = all languages). Persisted in Settings.</summary>
    public string CurrentLanguage
    {
        get => _settings.GutenbergLanguage ?? "";
        set => _settings.GutenbergLanguage = value ?? "";
    }

    /// <summary>Human name of the current language, for status messages.</summary>
    public string CurrentLanguageName =>
        Languages.FirstOrDefault(l => l.Code == CurrentLanguage)?.DisplayName ?? "All languages";

    // ------------------------------------------------------------------
    // Searching
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default)
    {
        var search = query?.Trim() ?? "";
        var language = CurrentLanguage;

        var url = "https://gutendex.com/books/?" +
                  $"search={Uri.EscapeDataString(search)}" +
                  (string.IsNullOrWhiteSpace(language) ? "" : $"&languages={Uri.EscapeDataString(language)}");

        try
        {
            var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var books = new List<BookInfo>();
            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    var book = ToBookInfo(item);
                    if (book is not null)
                        books.Add(book);
                }
            }

            if (books.Count == 0)
                Log.Warn("Project Gutenberg: no books matched that search.");

            return books;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Said out loud rather than returned as an empty list: "no books matched"
            // is a lie when the site could not be reached at all.
            Log.Error($"Project Gutenberg: search failed: {ex.Message}");
            throw new InvalidOperationException($"Project Gutenberg could not be searched: {ex.Message}", ex);
        }
    }

    private BookInfo? ToBookInfo(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var idElement))
            return null;

        var id = idElement.ValueKind == JsonValueKind.Number
            ? idElement.GetInt32().ToString()
            : idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
        var authors = new List<string>();
        if (item.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in a.EnumerateArray())
                if (author.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                    authors.Add(name);
        }

        var languages = new List<string>();
        if (item.TryGetProperty("languages", out var langs) && langs.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in langs.EnumerateArray())
                if (l.GetString() is { Length: > 0 } code)
                    languages.Add(code.ToUpperInvariant());
        }

        var (format, downloadUrl, extension, available) = PickFormat(item);
        var bookPage = $"https://www.gutenberg.org/ebooks/{id}";

        // Prefer the canonical Gutenberg page for the "access this book" link, but
        // hand the download engine the direct file URL.
        var info = $"Language: {(languages.Count > 0 ? string.Join(", ", languages) : "unknown")}. " +
                   $"Format: {format}. Gutenberg id {id}.";

        return new BookInfo(
            ProviderId,
            id,
            string.IsNullOrWhiteSpace(title) ? $"Gutenberg book {id}" : title!,
            authors.Count > 0 ? string.Join(", ", authors) : "Unknown author",
            "",
            0,
            $"https://www.gutenberg.org/cache/epub/{id}/pg{id}.cover.medium.jpg",
            downloadUrl ?? bookPage,
            info + (extension is null ? "" : $"\n{bookPage}"))
        {
            Formats = available,
        };
    }

    /// <summary>Choose which file to offer for a book, honouring the user's preference.</summary>
    private (string Describe, string? Url, string? Extension, IReadOnlyDictionary<string, string> Available) PickFormat(JsonElement item)
    {
        var formats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("formats", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in f.EnumerateObject())
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    formats[property.Name] = value!;
            }
        }

        string? Find(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (formats.TryGetValue(key, out var exact))
                    return exact;
            }

            // Gutenberg adds "; charset=…" to text entries; match on the prefix too.
            foreach (var key in keys)
            {
                foreach (var pair in formats)
                    if (pair.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                        return pair.Value;
            }

            return null;
        }

        const string epub = "application/epub+zip";
        const string html = "text/html";
        const string txt = "text/plain";
        const string audio = "audio/mpeg";

        // Every format this book has, so the one to download can be chosen later.
        var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, mime) in new[] { ("audio", audio), ("epub", epub), ("html", html), ("txt", txt) })
            if (Find(mime) is { } address)
                available[code] = address;

        var preference = (_settings.GutenbergFormat ?? "audio").Trim().ToLowerInvariant();

        var order = preference switch
        {
            "epub" => new[] { epub, html, txt, audio },
            "html" => new[] { html, epub, txt, audio },
            "txt" => new[] { txt, html, epub, audio },
            _ => new[] { audio, epub, html, txt }, // "audio" preference, with graceful fallback
        };

        foreach (var key in order)
        {
            var url = Find(key);
            if (url is null)
                continue;

            var extension = key switch
            {
                epub => ".epub",
                html => ".html",
                txt => ".txt",
                audio => ".mp3",
                _ => ".bin",
            };
            var describe = key switch
            {
                epub => "EPUB",
                html => "HTML",
                txt => "plain text",
                audio => "MP3 audio",
                _ => "file",
            };
            return (describe, url, extension, available);
        }

        return ("web page", null, null, available);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(book.DownloadUrl))
            throw new InvalidOperationException(
                "This Project Gutenberg title has no downloadable file in the selected format.");

        // Gutenberg file URLs often end in ".epub3.images" or ".html.images", so
        // look for the format marker anywhere in the address rather than trusting
        // the last dot.
        var url = book.DownloadUrl;
        var extension =
            Contains(url, ".epub") ? ".epub" :
            Contains(url, ".html") ? ".html" :
            Contains(url, ".mp3") ? ".mp3" :
            Contains(url, ".ogg") ? ".ogg" :
            Contains(url, ".txt") ? ".txt" :
            Path.GetExtension(new Uri(url).AbsolutePath);

        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
            extension = ".txt";

        return Task.FromResult(new DownloadTicket(
            book.BookId,
            TicketFormat.PlainUrl,
            new Uri(book.DownloadUrl),
            null, null, null,
            SuggestedExtension: extension));
    }
}
