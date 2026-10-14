using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookDownloader.Core;

/// <summary>
/// Internet Archive provider: search the archive's audio collections (including
/// full LibriVox mirrors) and download an item as a zip of its audio files.
/// Public domain / Creative Commons content — no DRM.
/// </summary>
public sealed class InternetArchiveProvider : IBookProvider
{
    public const string ProviderId = "internetarchive";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string Id => ProviderId;
    public string DisplayName => "Internet Archive (free audio)";
    public string Description => "Search the Internet Archive's free audio collections and download items.";
    public bool RequiresLogin => false;
    public bool HasDrm => false;
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Disconnect() { }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default)
    {
        // Every word must match (the archive would otherwise OR them), the search is
        // limited to audio, and the most downloaded items come first, so a search
        // for a book finds the book rather than a random recording that mentions it.
        var words = (query ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = words.Length == 0
            ? "collection:(librivoxaudio OR audio_bookspoetry)"
            : "(" + string.Join(" AND ", words.Select(w => w.Replace("\"", ""))) + ")";

        var url = "https://archive.org/advancedsearch.php" +
                  $"?q={Uri.EscapeDataString(text + " AND mediatype:(audio)")}" +
                  "&fl%5B%5D=identifier&fl%5B%5D=title&fl%5B%5D=creator" +
                  "&sort%5B%5D=downloads+desc" +
                  "&rows=100&page=1&output=json";

        try
        {
            var resp = await Http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            var results = new List<BookInfo>();
            if (doc.RootElement.TryGetProperty("response", out var response) &&
                response.TryGetProperty("docs", out var docs) &&
                docs.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in docs.EnumerateArray())
                {
                    var identifier = d.TryGetProperty("identifier", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(identifier))
                        continue;

                    var title = FirstString(d, "title");
                    var creator = FirstString(d, "creator");

                    results.Add(new BookInfo(
                        ProviderId,
                        identifier,
                        title ?? identifier,
                        creator ?? "Unknown",
                        "",
                        0,
                        $"https://archive.org/services/img/{identifier}",
                        $"https://archive.org/compress/{identifier}",
                        "Public domain / Creative Commons"));
                }
            }

            if (results.Count == 0)
                Log.Warn("Internet Archive: no results for that search.");
            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Internet Archive: search failed: {ex.Message}");
            throw new InvalidOperationException($"The Internet Archive could not be searched: {ex.Message}", ex);
        }
    }

    /// <summary>The archive sends a field as a string or as an array of strings.</summary>
    private static string? FirstString(JsonElement doc, string name)
    {
        if (!doc.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString())),
            _ => null,
        };
    }

    public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(book.DownloadUrl))
            throw new InvalidOperationException("This item has no download link.");

        return Task.FromResult(new DownloadTicket(
            book.BookId,
            TicketFormat.PlainUrl,
            new Uri(book.DownloadUrl),
            null, null, null,
            SuggestedExtension: ".zip"));
    }
}
