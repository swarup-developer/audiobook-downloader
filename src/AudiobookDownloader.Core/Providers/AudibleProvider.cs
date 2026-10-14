using AudibleApi;
using AudibleApi.Common;
using System.Net;

namespace AudiobookDownloader.Core;

/// <summary>
/// Raised when a provider is asked for account-specific content while nobody is
/// signed in. The UI turns this into an explanation, never into pretend data.
/// </summary>
public sealed class NotSignedInException : InvalidOperationException
{
    public NotSignedInException(string message) : base(message) { }
}

/// <summary>
/// Audible provider: sign in with your Amazon account, list your library,
/// download AAXC (modern) or AAX (legacy) files and hand over the decryption keys.
/// Only books you actually own appear here — Audible's own licence check enforces that.
/// </summary>
/// <remarks>
/// Nothing account-specific is ever produced without a live authenticated session.
/// The library lives only in memory, is dropped on sign-out, and is never read from
/// a cache — so a signed-out app shows no books at all.
/// </remarks>
public sealed class AudibleProvider : IBookProvider, ILanguageFilter
{
    public const string ProviderId = "audible";

    private static string IdentityFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudiobookDownloader");

    private static string IdentityFile => Path.Combine(IdentityFolder, "audible-identity.json");

    private Api? _api;
    private List<Item>? _library;

    /// <summary>
    /// Raised the moment the live session appears or goes away, so every screen
    /// that shows account state updates from one signal.
    /// </summary>
    /// <remarks>
    /// Screens that each decided for themselves when to look at
    /// <see cref="IsConnected"/> are how the app ends up showing "Sign in" over an
    /// account that is already signed in: only the screen that started the sign-in
    /// knows to look again.
    /// </remarks>
    public event EventHandler? ConnectionChanged;

    public string Id => ProviderId;
    public string DisplayName => "Audible (your library)";
    public string Description => "Sign in with your Amazon account, then download books you own.";
    public bool RequiresLogin => true;
    public bool HasDrm => true;

    /// <summary>True only while this run holds a live, authenticated Audible session.</summary>
    public bool IsConnected => _api is not null;

    /// <summary>True when a sign-in from a previous run is still stored on disk.</summary>
    public bool HasSavedSignIn => File.Exists(IdentityFile);

    /// <summary>Audible store the account belongs to, e.g. "us" or "uk".</summary>
    public string AccountLocale => _settings.AudibleLocale ?? "us";

    /// <summary>Settings are injected after construction (the UI owns them).</summary>
    public AppSettings AppSettingsInstance
    {
        get => _settings;
        set => _settings = value;
    }

    private AppSettings _settings = new();

    /// <summary>
    /// The UI injects the window that shows Amazon's sign-in page and reports the
    /// address Amazon lands on afterwards. Keeps this library UI-free.
    /// </summary>
    public IAudibleSignInWindow? SignInWindow { get; set; }

    /// <summary>Library loaded for this session only; cleared on sign-out.</summary>
    public int LoadedBookCount => _library?.Count ?? 0;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_api is not null)
            return;

        Directory.CreateDirectory(IdentityFolder);

        // When the user asked us not to remember the sign-in, never reuse the
        // stored one: make them sign in again.
        if (!_settings.AudibleRememberSignIn && File.Exists(IdentityFile))
        {
            try { File.Delete(IdentityFile); }
            catch (Exception ex) { Log.Warn($"Audible: could not clear the saved sign-in: {ex.Message}"); }
        }

        var locale = Localization.Get(_settings.AudibleLocale ?? "us");
        if (locale == Locale.Empty)
            locale = Localization.Get("us");

        try
        {
            // A saved sign-in that still works *is* the sign-in: the page is opened
            // only when there is nothing on disk to reuse, or when what is there is
            // no longer accepted. Deciding that here rather than leaning on the
            // library's own fallback keeps it visible, so "open the page only when
            // it is actually necessary" is something the app states rather than
            // something it hopes for.
            var api = File.Exists(IdentityFile)
                ? await TryReuseSavedSignInAsync(locale)
                : null;

            if (api is null)
            {
                Log.Info("Audible: no saved sign-in to reuse; opening Amazon's sign-in page.");
                api = await EzApiCreator.GetApiAsync(new SignInCallback(this), locale, IdentityFile);
            }

            // The session goes live here — the last moment at which anything can
            // fail — so every screen is told about it once, and told the truth.
            SetSession(api);
            Log.Info($"Audible: signed in ({_settings.AudibleLocale}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetSession(null);
            throw new InvalidOperationException(
                "Audible sign-in did not complete. " + ex.Message, ex);
        }
    }

    /// <summary>
    /// The one place the live session changes. Everything that shows account state
    /// reads <see cref="IsConnected"/>, and everything is told about a change from
    /// here, so the session and the screens showing it cannot drift apart.
    /// </summary>
    private void SetSession(Api? api)
    {
        var changed = (_api is null) != (api is null);

        _api = api;
        if (api is null)
            _library = null;

        if (!changed)
            return;

        Log.Info(api is null ? "Audible: session closed." : "Audible: session established.");

        try { ConnectionChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Warn($"Audible: a screen failed while the sign-in state changed: {ex.Message}"); }
    }

    /// <summary>
    /// Reuses the sign-in stored on disk, or returns null when nothing there still
    /// works. A failure is deliberately not raised: it means only that the account
    /// holder has to sign in, which the caller does next.
    /// </summary>
    private static async Task<Api?> TryReuseSavedSignInAsync(Locale locale)
    {
        try
        {
            var api = await EzApiCreator.GetApiAsync(locale, IdentityFile);
            Log.Info("Audible: reused the saved sign-in, so no sign-in page was opened.");
            return api;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Info($"Audible: the saved sign-in could not be reused ({ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// Sign out: drop the in-memory session and, after confirmation by the caller,
    /// delete the stored credentials so no content can be shown again.
    /// </summary>
    public void Disconnect()
    {
        SetSession(null);

        try
        {
            if (File.Exists(IdentityFile))
                File.Delete(IdentityFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Audible: could not delete the stored sign-in: {ex.Message}");
        }

        Log.Info("Audible: signed out.");
    }

    /// <summary>Sign out and also forget the saved sign-in, used by Settings.</summary>
    public void ForgetSavedSignIn()
    {
        SetSession(null);

        try
        {
            if (File.Exists(IdentityFile))
                File.Delete(IdentityFile);
        }
        catch { /* best effort */ }
    }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string query, CancellationToken ct = default)
    {
        // Deliberately no auto-connect: without an authenticated session there is
        // no library to show, and we must not silently start a sign-in either.
        if (_api is null)
            throw new NotSignedInException(
                "Not signed in to Audible. Press the Sign in button next to the provider list first.");

        // The title lives in product_desc/product_attrs and the authors and narrators
        // in contributors. Asking for product_attrs alone is why every book used to
        // arrive without a title and be listed by its ASIN instead.
        _library ??= await _api.GetAllLibraryItemsAsync(
            new LibraryOptions
            {
                ResponseGroups = LibraryResponseGroups,
                NumberOfResultPerPage = 250,
            });

        var words = (query ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var language = CurrentLanguage;

        ct.ThrowIfCancellationRequested();

        return _library
            .Where(i => !string.IsNullOrWhiteSpace(i.Asin))
            .Where(IsDownloadable)
            .Where(i => string.IsNullOrEmpty(language) ||
                        string.Equals(i.Language?.Trim(), language, StringComparison.OrdinalIgnoreCase))
            .Select(i => new BookInfo(
                ProviderId,
                i.Asin!,
                DisplayTitle(i),
                DisplayCredits(i.Authors, BookInfo.UnknownAuthor),
                DisplayCredits(i.Narrators, string.Empty),
                LengthOf(i),
                i.PictureLarge ?? i.PictureId,
                null,
                $"{LengthOf(i) / 60} hr {LengthOf(i) % 60} min"))
            // Every word has to appear in the title, an author or a narrator, so
            // "harry stone" finds "Harry Potter and the Philosopher's Stone".
            .Where(b => words.All(w =>
                b.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                b.Author.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                b.Narrator.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                b.BookId.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private const LibraryOptions.ResponseGroupOptions LibraryResponseGroups =
        LibraryOptions.ResponseGroupOptions.ProductAttrs
        | LibraryOptions.ResponseGroupOptions.ProductDesc
        | LibraryOptions.ResponseGroupOptions.ProductExtendedAttrs
        | LibraryOptions.ResponseGroupOptions.ProductDetails
        | LibraryOptions.ResponseGroupOptions.Contributors
        | LibraryOptions.ResponseGroupOptions.Media
        | LibraryOptions.ResponseGroupOptions.Relationships
        | LibraryOptions.ResponseGroupOptions.Series
        | LibraryOptions.ResponseGroupOptions.Origin
        | LibraryOptions.ResponseGroupOptions.IsPlayable
        | LibraryOptions.ResponseGroupOptions.CustomerRights;

    /// <summary>
    /// A podcast's parent entry is a folder of episodes, not something with audio of
    /// its own, so Audible refuses a licence for it ("License response not OK").
    /// </summary>
    private static bool IsDownloadable(Item item) =>
        !string.Equals(item.ContentDeliveryType, "PodcastParent", StringComparison.OrdinalIgnoreCase);

    private static int LengthOf(Item item) =>
        item.LengthInMinutes > 0 ? item.LengthInMinutes : item.RuntimeLengthMin ?? 0;

    // ------------------------------------------------------------------
    // Language filter — the same choice the Audible store offers
    // ------------------------------------------------------------------

    /// <summary>
    /// The languages Audible sells books in. Audible names a book's language in
    /// lower case English ("hindi"), which is what the code here holds.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("english", "English"),
        new("hindi", "Hindi"),
        new("tamil", "Tamil"),
        new("telugu", "Telugu"),
        new("marathi", "Marathi"),
        new("bengali", "Bengali"),
        new("malayalam", "Malayalam"),
        new("kannada", "Kannada"),
        new("gujarati", "Gujarati"),
        new("urdu", "Urdu"),
        new("german", "German"),
        new("french", "French"),
        new("spanish", "Spanish"),
        new("italian", "Italian"),
        new("portuguese", "Portuguese"),
        new("japanese", "Japanese"),
        new("chinese", "Chinese"),
        new("dutch", "Dutch"),
        new("russian", "Russian"),
        new("polish", "Polish"),
        new("swedish", "Swedish"),
        new("danish", "Danish"),
        new("norwegian", "Norwegian"),
        new("finnish", "Finnish"),
        new("turkish", "Turkish"),
        new("arabic", "Arabic"),
    ];

    IReadOnlyList<LanguageOption> ILanguageFilter.SupportedLanguages => Languages;

    /// <summary>The language the library is narrowed to; empty means every language.</summary>
    public string CurrentLanguage
    {
        get => _settings.AudibleLanguage ?? "";
        set => _settings.AudibleLanguage = value ?? "";
    }

    /// <summary>
    /// The title to show for one library item, falling back to its identifier when
    /// Amazon sends none.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>TitleWithSubtitle</c>: the Audible library answers
    /// "[null]" from that property when an item has no title, so it is never null
    /// and a null-coalescing fallback never runs. A placeholder is not a title to
    /// put in front of somebody, and the ASIN at least identifies the book — which
    /// is why <see cref="DisplayText.IsMissing"/> tests for the placeholder as well
    /// as for emptiness, instead of the title simply being null-checked.
    /// </remarks>
    public static string DisplayTitle(Item item)
    {
        if (DisplayText.IsMissing(item.Title))
            return DisplayText.FirstReal(BookInfo.UnknownTitle, item.Asin);

        var title = DisplayText.FirstReal(BookInfo.UnknownTitle, item.Title);
        return DisplayText.IsMissing(item.Subtitle)
            ? title
            : $"{title}: {item.Subtitle!.Trim()}";
    }

    /// <summary>
    /// The names to show for a credit list, with the blanks left out: Amazon sends
    /// people with no name at all, and "Smith, , Jones" is not a credit.
    /// </summary>
    public static string DisplayCredits(Person[]? people, string whenNobody)
    {
        var names = (people ?? [])
            .Select(person => person?.Name)
            .Where(name => !DisplayText.IsMissing(name))
            .Select(name => name!.Trim());

        var joined = string.Join(", ", names);
        return joined.Length > 0 ? joined : whenNobody;
    }

    public async Task<DownloadTicket> GetDownloadTicketAsync(BookInfo book, CancellationToken ct = default)
    {
        if (_api is null)
            throw new NotSignedInException(
                "Not signed in to Audible. Press the Sign in button before downloading Audible books.");

        // Modern flow: request a content licence. Audible returns the AAXC url plus
        // the per-download key and IV. Older titles fall back to AAX + activation bytes.
        AudibleApi.Common.ContentLicense license;
        try
        {
            license = await _api.GetDownloadLicenseAsync(book.BookId, DownloadQuality.High);
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   ex.Message.Contains("License response not", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Audible refused to issue a download licence for \"{book.Title}\". This happens for titles " +
                "that are no longer in your library (for example Audible Plus titles that were removed, " +
                "returned books or podcasts), or that belong to a different Audible country than the one " +
                $"you signed in to ({AccountLocale}).", ex);
        }
        var url = license.ContentMetadata?.ContentUrl?.OfflineUrl;

        if (!string.IsNullOrWhiteSpace(url) && license.Voucher?.Key is not null)
        {
            Log.Info($"Audible: AAXC licence granted for {book.Title}.");
            return new DownloadTicket(
                book.BookId,
                TicketFormat.Aaxc,
                new Uri(url),
                license.Voucher.Key,
                license.Voucher.Iv,
                null);
        }

        // Legacy AAX workaround (Libation-style): fetch activation bytes once and
        // download the older AAX file, which unlocks with those bytes.
        Log.Info("Audible: falling back to legacy AAX download.");
        var aaxUrl = await _api.DownloadAaxWorkaroundAsync(book.BookId);
        var activationBytes = await _api.GetActivationBytesAsync();
        return new DownloadTicket(
            book.BookId,
            TicketFormat.Aax,
            new Uri(aaxUrl),
            null, null,
            activationBytes);
    }

    /// <summary>
    /// Amazon's sign-in page is shown inside the app, in the window the UI
    /// injected. That window watches for the redirect Amazon sends once the
    /// account holder is signed in, takes the authorization code straight out of
    /// it and closes itself — so the sign-in finishes in the app instead of
    /// leaving the user stranded on a web page.
    /// </summary>
    private sealed class SignInCallback(AudibleProvider owner) : ILoginExternal
    {
        public string GetResponseUrl(string loginUrl, CookieCollection signInCookies)
        {
            var window = owner.SignInWindow
                ?? throw new InvalidOperationException(
                    "Audible sign-in needs a window to show Amazon's sign-in page in, but none was configured.");

            return window.ShowSignIn(new AudibleSignInRequest(loginUrl, signInCookies))
                ?? throw new OperationCanceledException("Audible sign-in was cancelled.");
        }
    }
}
