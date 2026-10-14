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

    /// <summary>
    /// The saved sign-in of the account in use. The first account keeps the original
    /// file name, so a sign-in saved by an earlier version is still found; every
    /// further account has a file of its own.
    /// </summary>
    private string IdentityFile => IdentityFileFor(ActiveAccount?.Id);

    private static string IdentityFileFor(string? accountId) =>
        string.IsNullOrEmpty(accountId) || accountId == DefaultAccountId
            ? Path.Combine(IdentityFolder, "audible-identity.json")
            : Path.Combine(IdentityFolder, $"audible-identity-{accountId}.json");

    private const string DefaultAccountId = "default";

    private Api? _api;
    private List<Item>? _library;

    /// <summary>
    /// The library request in flight, shared by everyone who asks while it runs, so
    /// signing in and choosing the provider at the same moment fetch it once.
    /// </summary>
    private Task<List<Item>>? _libraryLoad;

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
            EnsureAccount();

            var api = File.Exists(IdentityFile)
                ? await TryReuseSavedSignInAsync(locale, IdentityFile)
                : null;

            if (api is null)
            {
                Log.Info("Audible: no saved sign-in to reuse; opening Amazon's sign-in page.");
                api = await EzApiCreator.GetApiAsync(new SignInCallback(this), locale, IdentityFile);
            }

            // The session goes live here — the last moment at which anything can
            // fail — so every screen is told about it once, and told the truth.
            await NameAccountAsync(api);
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
        {
            _library = null;
            _libraryLoad = null;
            lock (_podcastNames) _podcastNames.Clear();
        }

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
    private static async Task<Api?> TryReuseSavedSignInAsync(Locale locale, string identityFile)
    {
        try
        {
            var api = await EzApiCreator.GetApiAsync(locale, identityFile);
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

        // Signing out forgets this account; another one signed in on this computer
        // becomes the account in use.
        if (ActiveAccount is { } gone)
        {
            _settings.AudibleAccounts.Remove(gone);
            _settings.ActiveAudibleAccountId = _settings.AudibleAccounts.FirstOrDefault()?.Id;
            if (ActiveAccount is { } next)
                _settings.AudibleLocale = next.Locale;
            _settings.Save();
        }

        Log.Info("Audible: signed out.");
    }

    // ------------------------------------------------------------------
    // Several accounts
    // ------------------------------------------------------------------

    /// <summary>The account in use, or null when none has signed in yet.</summary>
    public AudibleAccount? ActiveAccount =>
        _settings.AudibleAccounts.FirstOrDefault(a => a.Id == _settings.ActiveAudibleAccountId)
        ?? _settings.AudibleAccounts.FirstOrDefault();

    /// <summary>Every account signed in on this computer.</summary>
    public IReadOnlyList<AudibleAccount> Accounts
    {
        get
        {
            EnsureAccount(createIfNone: false);
            return _settings.AudibleAccounts;
        }
    }

    /// <summary>
    /// Makes sure the account list describes what is on disk: a sign-in saved by an
    /// earlier version, which knew only one account, becomes the first account.
    /// </summary>
    private void EnsureAccount(bool createIfNone = true)
    {
        if (_settings.AudibleAccounts.Count > 0)
            return;

        if (!createIfNone && !File.Exists(IdentityFileFor(DefaultAccountId)))
            return;

        _settings.AudibleAccounts.Add(new AudibleAccount
        {
            Id = DefaultAccountId,
            Name = "",
            Locale = _settings.AudibleLocale ?? "us",
        });
        _settings.ActiveAudibleAccountId = DefaultAccountId;
    }

    /// <summary>Names the account after its e-mail address, which is what people recognise.</summary>
    private async Task NameAccountAsync(Api api)
    {
        EnsureAccount();
        var account = ActiveAccount!;
        account.Locale = _settings.AudibleLocale ?? account.Locale;

        if (!string.IsNullOrWhiteSpace(account.Name))
            return;

        try
        {
            account.Name = (await api.GetEmailAsync())?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            Log.Info($"Audible: the account's e-mail address could not be read ({ex.Message}).");
        }

        if (string.IsNullOrWhiteSpace(account.Name))
            account.Name = $"Account {_settings.AudibleAccounts.IndexOf(account) + 1}";
        _settings.Save();
    }

    /// <summary>
    /// Starts adding another account: it becomes the account in use, with no sign-in
    /// yet, so the next <see cref="ConnectAsync"/> opens Amazon's page for it. The
    /// account that was in use keeps its saved sign-in.
    /// </summary>
    public void BeginNewAccount()
    {
        EnsureAccount(createIfNone: false);
        SetSession(null);

        var account = new AudibleAccount { Locale = _settings.AudibleLocale ?? "us" };
        _settings.AudibleAccounts.Add(account);
        _settings.ActiveAudibleAccountId = account.Id;
        _settings.AudibleCountryChosen = false;
    }

    /// <summary>Takes back an account that was never signed in to, returning to the previous one.</summary>
    public async Task AbandonNewAccountAsync()
    {
        if (ActiveAccount is { } added && !File.Exists(IdentityFile) && _api is null)
        {
            _settings.AudibleAccounts.Remove(added);
            _settings.ActiveAudibleAccountId = _settings.AudibleAccounts.FirstOrDefault()?.Id;
            if (ActiveAccount is { } back)
            {
                _settings.AudibleLocale = back.Locale;
                _settings.AudibleCountryChosen = true;
            }
            _settings.Save();
            await TryResumeSessionAsync();
        }
    }

    /// <summary>Changes to another account signed in on this computer, using its saved sign-in.</summary>
    public async Task<bool> SwitchAccountAsync(string accountId)
    {
        var account = _settings.AudibleAccounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
            return false;

        if (account == ActiveAccount && _api is not null)
            return true;

        SetSession(null);
        _settings.ActiveAudibleAccountId = account.Id;
        _settings.AudibleLocale = account.Locale;
        _settings.AudibleCountryChosen = true;
        _settings.Save();

        Log.Info($"Audible: switching to {account.Name} ({account.Locale}).");
        return await TryResumeSessionAsync();
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

        if (_library is null)
        {
            var load = _libraryLoad ??= LoadLibraryAsync(CancellationToken.None);
            try
            {
                _library = await load.WaitAsync(ct);
            }
            catch when (load.IsFaulted || load.IsCanceled)
            {
                // A failed read is not remembered, so the next search tries again.
                if (ReferenceEquals(_libraryLoad, load))
                    _libraryLoad = null;
                throw;
            }
        }

        var words = (query ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var language = CurrentLanguage;

        ct.ThrowIfCancellationRequested();

        return _library
            .Where(i => !string.IsNullOrWhiteSpace(i.Asin))
            .Where(IsDownloadable)
            .Where(i => string.IsNullOrEmpty(language) ||
                        string.Equals(i.Language?.Trim(), language, StringComparison.OrdinalIgnoreCase))
            .Select(ToBookInfo)
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

    /// <summary>One library item as a row, with everything the details and the file tags use.</summary>
    private BookInfo ToBookInfo(Item i)
    {
        var title = DisplayTitle(i);
        // An episode says which show it is from, unless its own title already does.
        if (_podcastNames.TryGetValue(i.Asin!, out var podcast) && !title.Contains(podcast, StringComparison.OrdinalIgnoreCase))
            title = $"{podcast}: {title}";

        return new BookInfo(
            ProviderId,
            i.Asin!,
            title,
            DisplayCredits(i.Authors, BookInfo.UnknownAuthor),
            DisplayCredits(i.Narrators, string.Empty),
            LengthOf(i),
            CoverUrl(i),
            null,
            $"{LengthOf(i) / 60} hr {LengthOf(i) % 60} min")
        {
            Language = DisplayText.IsMissing(i.Language) ? null : Capitalise(i.Language!.Trim()),
            Description = DisplayText.FirstReal("", i.PublisherSummary, i.MerchandisingSummary, i.Description) is { Length: > 0 } d
                ? StripHtml(d)
                : null,
            Series = DescribeSeries(i.Series),
            SeriesName = FirstSeries(i.Series) is { } series ? DisplayText.FirstReal("", series.SeriesName, series.Title) : null,
            SeriesPosition = FirstSeries(i.Series) is { } position ? SeriesNumber(position) : 0,
            DateAdded = i.PurchaseDate != default ? i.PurchaseDate
                : i.DateAdded != default ? new DateTimeOffset(DateTime.SpecifyKind(i.DateAdded, DateTimeKind.Utc))
                : null,
        };
    }

    /// <summary>Podcast episode ASIN to the podcast's own name, so an episode row says which show it is from.</summary>
    private readonly Dictionary<string, string> _podcastNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The whole library, and — when asked for — the episodes of every podcast in it,
    /// which the library lists only under their podcast.
    /// </summary>
    private async Task<List<Item>> LoadLibraryAsync(CancellationToken ct)
    {
        // The title lives in product_desc/product_attrs and the authors and narrators
        // in contributors. Asking for product_attrs alone is why every book used to
        // arrive without a title and be listed by its ASIN instead.
        var items = await _api!.GetAllLibraryItemsAsync(
            new LibraryOptions
            {
                ResponseGroups = LibraryResponseGroups,
                NumberOfResultPerPage = 250,
            });

        if (!_settings.AudibleIncludePodcasts)
            return items;

        var parents = items.Where(i => !IsDownloadable(i) && !string.IsNullOrWhiteSpace(i.Asin)).ToList();
        if (parents.Count == 0)
            return items;

        using var gate = new SemaphoreSlim(4);
        var episodeLists = await Task.WhenAll(parents.Select(async parent =>
        {
            await gate.WaitAsync(ct);
            try { return await GetEpisodesAsync(parent, ct); }
            finally { gate.Release(); }
        }));

        var known = new HashSet<string>(items.Select(i => i.Asin ?? ""), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var episode in episodeLists.SelectMany(list => list))
        {
            if (episode.Asin is { } asin && known.Add(asin))
            {
                items.Add(episode);
                added++;
            }
        }

        Log.Info($"Audible: {parents.Count} podcast(s) in the library, {added} episode(s) listed.");
        return items;
    }

    /// <summary>The episodes of one podcast, or none when Audible will not list them.</summary>
    private async Task<IReadOnlyList<Item>> GetEpisodesAsync(Item parent, CancellationToken ct)
    {
        try
        {
            var options = new LibraryOptions { ResponseGroups = LibraryResponseGroups, NumberOfResultPerPage = 1000 };
            var json = await _api!.GetLibraryAsync(
                options.ToQueryString() + "&parent_asin=" + Uri.EscapeDataString(parent.Asin!));
            ct.ThrowIfCancellationRequested();

            var episodes = AudibleApi.Common.LibraryDtoV10.FromJson(json)?.Items ?? [];
            var name = DisplayTitle(parent);
            lock (_podcastNames)
            {
                foreach (var episode in episodes)
                    if (episode.Asin is { } asin)
                        _podcastNames[asin] = name;
            }

            return episodes.Where(e => IsDownloadable(e) && e.Asin != parent.Asin).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Audible: could not list the episodes of \"{parent.Title}\": {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Reconnects with the sign-in saved on this computer, without ever opening a
    /// sign-in page. Used at start-up so a remembered account is simply signed in.
    /// </summary>
    public async Task<bool> TryResumeSessionAsync()
    {
        if (_api is not null)
            return true;

        EnsureAccount(createIfNone: false);
        if (!_settings.AudibleRememberSignIn || !File.Exists(IdentityFile))
            return false;

        var locale = Localization.Get(_settings.AudibleLocale ?? "us");
        if (locale == Locale.Empty)
            locale = Localization.Get("us");

        var api = await TryReuseSavedSignInAsync(locale, IdentityFile);
        if (api is null)
            return false;

        await NameAccountAsync(api);
        SetSession(api);
        Log.Info($"Audible: signed in again with the saved sign-in ({_settings.AudibleLocale}).");
        return true;
    }

    /// <summary>
    /// Books added to the library since the last check that have no file yet. The
    /// first check only records the time, so turning the feature on never starts a
    /// download of the whole library.
    /// </summary>
    public async Task<IReadOnlyList<BookInfo>> GetNewPurchasesAsync(Func<BookInfo, bool> alreadyDownloaded, CancellationToken ct = default)
    {
        var since = _settings.AudibleLastSync;
        var now = DateTimeOffset.UtcNow;

        // Everything, regardless of the language filter or a search.
        var language = CurrentLanguage;
        CurrentLanguage = "";
        IReadOnlyList<BookInfo> all;
        try { all = await GetBooksAsync("", ct); }
        finally { CurrentLanguage = language; }

        _settings.AudibleLastSync = now;
        _settings.Save();

        if (since is null)
        {
            Log.Info("Audible: automatic downloads are on; books bought from now on will be downloaded.");
            return [];
        }

        return all.Where(b => b.DateAdded is { } added && added > since && !alreadyDownloaded(b)).ToList();
    }

    // ------------------------------------------------------------------
    // The Audible store: search, wishlist and store pages
    // ------------------------------------------------------------------

    private const string StoreResponseGroups =
        "contributors,media,product_attrs,product_desc,product_extended_attrs,series,customer_rights";

    /// <summary>Searches the Audible store of the account's country.</summary>
    public async Task<IReadOnlyList<BookInfo>> SearchStoreAsync(string query, CancellationToken ct = default)
    {
        var api = _api ?? throw new NotSignedInException(
            "Not signed in to Audible. Press the Sign in button next to the provider list first.");

        var owned = await OwnedAsync(ct);
        var url = "/1.0/catalog/products?num_results=50&products_sort_by=Relevance" +
                  $"&response_groups={StoreResponseGroups}&keywords={Uri.EscapeDataString(query.Trim())}";

        using var response = await api.AdHocAuthenticatedGetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = Newtonsoft.Json.Linq.JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        var items = AudibleApi.Common.ProductsDtoV10.FromJson(json)?.Products ?? [];

        Log.Info($"Audible store: {items.Length} result(s) for \"{query.Trim()}\".");
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Asin))
            .Select(i => ToBookInfo(i) with { ProviderId = AudibleStoreProvider.ProviderId, InLibrary = owned.Contains(i.Asin!) })
            .ToList();
    }

    /// <summary>The account's wishlist, newest first.</summary>
    public async Task<IReadOnlyList<BookInfo>> GetWishlistAsync(CancellationToken ct = default)
    {
        var api = _api ?? throw new NotSignedInException(
            "Not signed in to Audible. Press the Sign in button next to the provider list first.");

        var owned = await OwnedAsync(ct);
        var products = await api.GetWishListProductsAsync(
            new WishListOptions
            {
                ResponseGroups = WishListOptions.ResponseGroupOptions.ProductAttrs
                    | WishListOptions.ResponseGroupOptions.Contributors
                    | WishListOptions.ResponseGroupOptions.Media
                    | WishListOptions.ResponseGroupOptions.ProductDesc,
                NumberOfResultPerPage = 50,
            });

        Log.Info($"Audible: {products.Count} book(s) on the wishlist.");
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Asin))
            .Select(p =>
            {
                var minutes = (int?)p.SourceJson?["runtime_length_min"] ?? 0;
                var title = DisplayText.FirstReal(p.Asin!, p.Title);
                if (!DisplayText.IsMissing(p.Subtitle))
                    title += ": " + p.Subtitle!.Trim();
                string Names(AudibleApi.Common.Author[]? people, string none) =>
                    string.Join(", ", (people ?? []).Select(a => a?.Name).Where(n => !DisplayText.IsMissing(n))) is { Length: > 0 } joined
                        ? joined : none;

                return new BookInfo(AudibleStoreProvider.ProviderId, p.Asin!, title,
                    Names(p.Authors, BookInfo.UnknownAuthor), Names(p.Narrators, ""),
                    minutes, p.ProductImages?.The500?.ToString(), null, $"{minutes / 60} hr {minutes % 60} min")
                {
                    Language = DisplayText.IsMissing(p.Language) ? null : Capitalise(p.Language!.Trim()),
                    Description = DisplayText.IsMissing(p.MerchandisingSummary) ? null : StripHtml(p.MerchandisingSummary!),
                    DateAdded = p.AddedTimestamp == default ? null : p.AddedTimestamp,
                    InLibrary = owned.Contains(p.Asin!),
                };
            })
            .OrderByDescending(b => b.DateAdded)
            .ToList();
    }

    public async Task AddToWishlistAsync(string asin)
    {
        var api = _api ?? throw new NotSignedInException("Not signed in to Audible.");
        await api.AddToWishListAsync(asin);
        Log.Info($"Audible: {asin} added to the wishlist.");
    }

    public async Task RemoveFromWishlistAsync(string asin)
    {
        var api = _api ?? throw new NotSignedInException("Not signed in to Audible.");
        await api.DeleteFromWishListAsync(asin);
        Log.Info($"Audible: {asin} removed from the wishlist.");
    }

    /// <summary>The book's page in the account's Audible store, where it can be bought.</summary>
    public string StorePageUrl(string asin)
    {
        var locale = Localization.Get(_settings.AudibleLocale ?? "us");
        var domain = locale == Locale.Empty || string.IsNullOrWhiteSpace(locale.TopDomain) ? "com" : locale.TopDomain;
        return $"https://www.audible.{domain}/pd/{Uri.EscapeDataString(asin)}";
    }

    /// <summary>Whether a book is in the library (loading the library if need be).</summary>
    public async Task<bool> OwnsAsync(string asin, CancellationToken ct = default) =>
        (await OwnedAsync(ct)).Contains(asin);

    private async Task<HashSet<string>> OwnedAsync(CancellationToken ct)
    {
        await GetBooksAsync("", ct);
        return new HashSet<string>((_library ?? []).Select(i => i.Asin ?? ""), StringComparer.OrdinalIgnoreCase);
    }

    private static string? CoverUrl(Item i)
    {
        var url = i.ProductImages?.The1215?.ToString() ?? i.ProductImages?.The500?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
            return url;

        return DisplayText.IsMissing(i.PictureLarge) ? null : i.PictureLarge;
    }

    private static AudibleApi.Common.Series? FirstSeries(AudibleApi.Common.Series[]? series) =>
        series?.FirstOrDefault(s => !DisplayText.IsMissing(s?.SeriesName ?? s?.Title));

    /// <summary>"3", "2.5" or "Book 4" as a number to sort by; 0 when there is none.</summary>
    private static float SeriesNumber(AudibleApi.Common.Series series)
    {
        if (series.Index > 0)
            return series.Index;

        var digits = new string((series.Sequence ?? "").SkipWhile(c => !char.IsDigit(c))
            .TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return float.TryParse(digits, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static string? DescribeSeries(AudibleApi.Common.Series[]? series)
    {
        var first = series?.FirstOrDefault(s => !DisplayText.IsMissing(s?.SeriesName ?? s?.Title));
        if (first is null)
            return null;

        var name = DisplayText.FirstReal("", first.SeriesName, first.Title);
        return DisplayText.IsMissing(first.Sequence) ? name : $"{name}, Book {first.Sequence}";
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string StripHtml(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "))
            .Replace("  ", " ").Trim();

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
        | LibraryOptions.ResponseGroupOptions.CustomerRights
        | LibraryOptions.ResponseGroupOptions.ProductPlans;

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
                null)
            {
                Chapters = FlattenChapters(license.ContentMetadata?.ChapterInfo?.Chapters),
            };
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
    /// The licence's chapter tree as a flat list in playing order. A part that has
    /// chapters of its own ("Part One", then its chapters) keeps any opening stretch
    /// before its first chapter as a chapter of its own, titled after the part.
    /// </summary>
    public static IReadOnlyList<ChapterMark>? FlattenChapters(AudibleApi.Common.Chapter[]? chapters)
    {
        if (chapters is not { Length: > 0 })
            return null;

        var flat = new List<ChapterMark>();

        void Walk(IEnumerable<AudibleApi.Common.Chapter> level, string prefix)
        {
            foreach (var chapter in level)
            {
                var title = DisplayText.FirstReal($"Chapter {flat.Count + 1}", chapter.Title);
                var start = TimeSpan.FromMilliseconds(chapter.StartOffsetMs);

                if (chapter.Chapters is { Length: > 0 } children)
                {
                    var intro = children[0].StartOffsetMs - chapter.StartOffsetMs;
                    if (intro > 1000)
                        flat.Add(new ChapterMark(prefix + title, start, TimeSpan.FromMilliseconds(intro)));
                    Walk(children, prefix + title + ": ");
                }
                else
                {
                    flat.Add(new ChapterMark(prefix + title, start, TimeSpan.FromMilliseconds(chapter.LengthMs)));
                }
            }
        }

        Walk(chapters, "");
        return flat.Count > 0 ? flat : null;
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
