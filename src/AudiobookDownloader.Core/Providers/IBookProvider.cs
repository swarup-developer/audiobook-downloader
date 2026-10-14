namespace AudiobookDownloader.Core;

/// <summary>
/// A book source. Any service can plug in here — Audible today,
/// more providers later. The ComboBox in the UI lists every registered provider.
/// </summary>
public interface IBookProvider
{
    /// <summary>Stable id persisted in settings, e.g. "audible".</summary>
    string Id { get; }

    /// <summary>Human name shown in the ComboBox, e.g. "Audible (your library)".</summary>
    string DisplayName { get; }

    /// <summary>Shown in the UI so the user knows what each provider does.</summary>
    string Description { get; }

    /// <summary>True if the provider needs a sign-in step before listing books.</summary>
    bool RequiresLogin { get; }

    /// <summary>True if books from this provider typically arrive with DRM.</summary>
    bool HasDrm { get; }

    /// <summary>Sign in / connect. Idempotent: should no-op when already connected.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>True after a successful ConnectAsync.</summary>
    bool IsConnected { get; }

    /// <summary>Disconnect / forget credentials.</summary>
    void Disconnect();

    /// <summary>List available books. Empty query returns everything the provider offers.</summary>
    Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default);

    /// <summary>Get a download ticket for one book (URL + any keys needed for decryption).</summary>
    Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default);
}
