namespace AudiobookDownloader.Core;

/// <summary>
/// The Audible store of the signed-in account's country: search every book Audible
/// sells, and see the wishlist when the search box is empty. Books already in the
/// library download as usual; anything else can be opened in the store, to buy it,
/// or added to the wishlist.
/// </summary>
/// <remarks>
/// Uses the same session as <see cref="AudibleProvider"/>; it signs in and out with it.
/// </remarks>
public sealed class AudibleStoreProvider(AudibleProvider audible) : IBookProvider
{
    public const string ProviderId = "audiblestore";

    public AudibleProvider Audible => audible;

    public string Id => ProviderId;
    public string DisplayName => "Audible store (search and wishlist)";
    public string Description => "Search Audible's store, see your wishlist, and open a book's store page to buy it.";
    public bool RequiresLogin => true;
    public bool HasDrm => true;
    public bool IsConnected => audible.IsConnected;

    public Task ConnectAsync(CancellationToken ct = default) => audible.ConnectAsync(ct);

    public void Disconnect() => audible.Disconnect();

    /// <summary>An empty search shows the wishlist; anything else searches the store.</summary>
    public Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? audible.GetWishlistAsync(ct)
            : audible.SearchStoreAsync(query, ct);

    public async Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        if (!await audible.OwnsAsync(book.BookId, ct))
            throw new InvalidOperationException(
                $"\"{book.Title}\" is not in your Audible library, so it cannot be downloaded. " +
                "Open its store page to buy it (the book list's context menu, or Ctrl+Shift+S).");

        return await audible.GetDownloadTicketAsync(book, ct);
    }
}
