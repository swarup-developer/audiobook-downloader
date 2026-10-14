namespace AudiobookDownloader.Core;

/// <summary>
/// "Provider" for AAX/AAXC files the user already has on disk (from any source).
/// Lets the app double as a standalone DRM remover: pick a folder, choose a file,
/// and the engine decrypts it to M4B locally.
/// </summary>
public sealed class LocalFilesProvider : IBookProvider
{
    public const string ProviderId = "localfiles";

    private readonly AppSettings _settings;
    private readonly IKeyPrompt? _keyPrompt;

    public LocalFilesProvider(AppSettings settings, IKeyPrompt? keyPrompt)
    {
        _settings = settings;
        _keyPrompt = keyPrompt;
    }

    public string Id => ProviderId;
    public string DisplayName => "Local files (remove DRM from AAX/AAXC)";
    public string Description => "Pick an AAX/AAXC file already on your computer and convert it to DRM-free M4B.";
    public bool RequiresLogin => false;
    public bool HasDrm => true;
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Disconnect() { }

    public Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default)
    {
        var folder = _settings.LocalFolder;
        var result = new List<BookInfo>();

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".aax", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".aaxc", StringComparison.OrdinalIgnoreCase));

            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrWhiteSpace(query) &&
                    !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new BookInfo(
                    ProviderId,
                    f,
                    name,
                    "Local file",
                    "",
                    0,
                    null,
                    new Uri(f).AbsoluteUri,
                    Path.GetExtension(f).ToUpperInvariant()));
            }
        }

        return Task.FromResult<IReadOnlyList<BookInfo>>(result);
    }

    public Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        var path = book.BookId; // for this provider BookId is the full file path
        var ext = Path.GetExtension(path).ToLowerInvariant();

        var cached = _settings.FindKeys(path);
        if (cached is not null)
            return Task.FromResult(MakeTicket(book, ext, cached));

        if (_keyPrompt is null)
            throw new InvalidOperationException(
                "Decryption keys are needed but no key prompt is available.");

        var keys = ext == ".aax"
            ? _keyPrompt.Prompt(path, askKeyIv: false, askActivationBytes: true)
            : _keyPrompt.Prompt(path, askKeyIv: true, askActivationBytes: false);

        if (keys is null)
            throw new OperationCanceledException("Decryption keys were not provided.");

        _settings.CacheKeys(path, keys);
        return Task.FromResult(MakeTicket(book, ext, keys));
    }

    private static DownloadTicket MakeTicket(BookInfo book, string ext, CachedKeys keys) =>
        ext == ".aax"
            ? new DownloadTicket(book.BookId, TicketFormat.Aax, new Uri(book.DownloadUrl!), null, null, keys.ActivationBytes)
            : new DownloadTicket(book.BookId, TicketFormat.Aaxc, new Uri(book.DownloadUrl!), keys.Key, keys.Iv, null);
}
