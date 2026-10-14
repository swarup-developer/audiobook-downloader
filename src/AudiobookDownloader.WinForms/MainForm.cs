using AudiobookDownloader.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AudiobookDownloader.WinForms;

public partial class MainForm : Form, IAudibleSignInWindow
{
    private readonly AppSettings _settings;
    private readonly DownloadQueue _queue;
    private readonly AudibleProvider _audible;
    private readonly BindingSource _booksBinding = new();

    /// <summary>What the provider returned for the last search.</summary>
    private List<BookInfo> _found = [];

    /// <summary>What the list shows: <see cref="_found"/>, sorted, without hidden rows.</summary>
    private List<BookInfo> _books = [];
    private QueueItem? _lastAnnounced;

    private bool _suppressProviderEvents;
    private bool _helpOpen;
    private int _lastProgressMilestone = -1;

    /// <summary>
    /// Bumped whenever the list is about to show something else, so a search that
    /// finishes late cannot overwrite the answer to a newer one.
    /// </summary>
    private int _loadGeneration;

    /// <summary>
    /// Arrowing through the provider or language list changes the selection on
    /// every key press. The list is loaded only once the selection has settled, so
    /// passing an entry on the way to another does not start a search for it.
    /// </summary>
    private ToolStripMenuItem _menuSortSeries = null!;
    private ToolStripMenuItem _menuStats = null!;
    private ToolStripMenuItem _menuBackupNow = null!;
    private ContextMenuStrip _bookMenu = null!;
    private ToolStripMenuItem _bookMenuDownload = null!;
    private ToolStripMenuItem _bookMenuPlay = null!;
    private ToolStripMenuItem _bookMenuDetails = null!;

    private readonly System.Windows.Forms.Timer _providerSettle = new() { Interval = 450 };

    /// <summary>Downloads already greeted with the success sound, so each gets it once.</summary>
    private readonly HashSet<Guid> _finishedDownloads = [];

    /// <summary>
    /// A screen of progress is drawn once however many reports arrive while it is
    /// waiting: downloads report far more often than anyone can hear, and a message
    /// for each one kept the window, and the screen reader with it, busy for seconds.
    /// </summary>
    private int _queueChangePending;

    /// <summary>The last percent beeped for, and for which download.</summary>
    private (Guid Item, int Percent) _lastBeep = (Guid.Empty, -1);

    public MainForm()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        Strings.SetLanguage(_settings.UiLanguage);

        _audible = new AudibleProvider { AppSettingsInstance = _settings };
        _audible.SignInWindow = this;

        // The live session is the one thing the account button and the library both
        // depend on, so they follow the provider's own signal rather than being
        // updated by whichever screen happened to start the sign-in.
        _audible.ConnectionChanged += OnAudibleConnectionChanged;

        // The selected books download one after another (or a few at once, as set in
        // Settings). Nothing is kept for the next run: there is no queue to manage.
        _queue = new DownloadQueue(new DownloadManager(_settings))
        {
            Parallelism = () => _settings.MaxParallelDownloads,
        };
        _queue.Changed += OnQueueChanged;
        _queue.QueueFinished += OnQueueFinished;

        RegisterProviders();
        WireMenus();

        _providerSettle.Tick += (_, _) =>
        {
            _providerSettle.Stop();
            RunSafely(OnProviderChangedAsync, Strings.T("Main.Tasks.Provider"));
        };

        // The SelectedIndexChanged handler is wired up in the designer. Filling the
        // list and restoring the last provider happens with events suppressed, so
        // nothing is loaded or saved twice; the first real load happens in Load.
        _suppressProviderEvents = true;

        providerCombo.DataSource = ProviderRegistry.Providers;
        providerCombo.DisplayMember = nameof(IBookProvider.DisplayName);

        // No DisplayMember: ListBox falls back to BookInfo.ToString() = "Title - Author",
        // which is what NVDA should read for every row.
        libraryList.DataSource = _booksBinding;

        ApplyLocalization();

        // Follow Windows high contrast, and keep following it while the app runs.
        SystemTheme.Follow(this, ApplyTheme);

        var saved = _settings.RememberLastProvider
            ? ProviderRegistry.Find(_settings.LastProviderId) ?? ProviderRegistry.Providers[0]
            : ProviderRegistry.Providers[0];
        providerCombo.SelectedItem = saved;

        _suppressProviderEvents = false;

        Load += async (_, _) =>
        {
            // A remembered Audible sign-in is simply used: no button to press, and no
            // sign-in page unless the saved one has stopped working.
            await ResumeAudibleSessionAsync();

            await OnProviderChangedAsync();

            if (_settings.AudibleAutoDownloadNew && _audible.IsConnected)
                RunSafely(DownloadNewPurchasesAsync, "looking for new Audible books");

            if (_settings.CheckForUpdates)
                RunSafely(() => CheckForUpdatesAsync(quiet: true), "checking for updates");
        };

        FormClosing += (_, _) =>
        {
            _providerSettle.Stop();
            _queue.Changed -= OnQueueChanged;
            _queue.QueueFinished -= OnQueueFinished;

            _audible.ConnectionChanged -= OnAudibleConnectionChanged;

            // Honour "do not remember the sign-in" by removing the stored session.
            if (!_settings.AudibleRememberSignIn)
                _audible.ForgetSavedSignIn();
        };
    }

    private void RegisterProviders()
    {
        ProviderRegistry.Reset();
        ProviderRegistry.Register(_audible);
        ProviderRegistry.Register(new LibriVoxProvider(_settings));
        ProviderRegistry.Register(new InternetArchiveProvider());
        ProviderRegistry.Register(new GutenbergProvider(_settings));
    }

    /// <summary>The source that needs the Audible sign-in.</summary>
    private static bool IsAudible(IBookProvider? provider) => provider is AudibleProvider;

    /// <summary>
    /// Menu entries added in code: sort by series, listening statistics and "back up
    /// now", plus the book list's own context menu (Applications key or Shift+F10).
    /// </summary>
    private void BuildExtraMenus()
    {
        _menuSortSeries = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.D5, Name = "menuSortSeries" };
        _menuSortSeries.Click += (_, _) => SetSortOrder("series", "Main.SortNameSeries");
        menuView.DropDownItems.Insert(menuView.DropDownItems.IndexOf(menuSortAdded) + 1, _menuSortSeries);

        _menuStats = new ToolStripMenuItem { ShortcutKeys = Keys.Control | Keys.Shift | Keys.T, Name = "menuStats" };
        _menuStats.Click += (_, _) => ShowListeningStats();
        menuView.DropDownItems.Add(new ToolStripSeparator());
        menuView.DropDownItems.Add(_menuStats);

        _menuBackupNow = new ToolStripMenuItem { Name = "menuBackupNow" };
        _menuBackupNow.Click += (_, _) => RunSafely(BackupNowAsync, "backing up");
        menuApplication.DropDownItems.Insert(menuApplication.DropDownItems.IndexOf(menuAppSeparator), _menuBackupNow);

        _bookMenu = new ContextMenuStrip { Name = "bookMenu" };
        _bookMenuDownload = new ToolStripMenuItem { ShortcutKeyDisplayString = "Enter", Name = "bookMenuDownload" };
        _bookMenuDownload.Click += (_, _) => downloadButton_Click(this, EventArgs.Empty);
        _bookMenuPlay = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl + P" };
        _bookMenuPlay.Click += (_, _) => PlaySelectedBook();
        _bookMenuDetails = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl + I" };
        _bookMenuDetails.Click += (_, _) => ShowBookDetails();
        _bookMenu.Items.AddRange([_bookMenuDownload, _bookMenuPlay, _bookMenuDetails]);
        libraryList.ContextMenuStrip = _bookMenu;
    }

    private void WireMenus()
    {
        BuildExtraMenus();
        menuSettings.Click += (_, _) => OpenSettings(SettingsForm.SectionGeneral);
        menuExit.Click += (_, _) => Close();
        menuAbout.Click += (_, _) => ShowAbout();
        menuShortcuts.Click += (_, _) => ShowKeyboardHelp();
        menuRefresh.Click += (_, _) => RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Reload"));
        menuOpenFolder.Click += (_, _) => OpenDownloadFolder();
        menuExport.Click += (_, _) => ExportBookList();
        menuPlay.Click += (_, _) => PlaySelectedBook();
        menuSortTitle.Click += (_, _) => SetSortOrder("title", "Main.SortNameTitle");
        menuSortAuthor.Click += (_, _) => SetSortOrder("author", "Main.SortNameAuthor");
        menuSortLength.Click += (_, _) => SetSortOrder("length", "Main.SortNameLength");
        menuSortAdded.Click += (_, _) => SetSortOrder("added", "Main.SortNameAdded");
        menuHideDownloaded.Click += (_, _) => ToggleHideDownloaded();
        menuCheckUpdates.Click += (_, _) => RunSafely(() => CheckForUpdatesAsync(quiet: false), "checking for updates");
    }

    // ------------------------------------------------------------------
    // Localization and accessibility wiring
    // ------------------------------------------------------------------

    /// <summary>
    /// Put every visible string and accessible name in place. Called at start-up
    /// and again whenever the interface language changes in Settings.
    /// </summary>
    private void ApplyLocalization()
    {
        Text = Strings.T("App.Title");
        bookGroup.Text = Strings.T("Main.Group.Books");
        queueGroup.Text = Strings.T("Main.Group.Queue");
        statusGroup.Text = Strings.T("Main.Group.Status");
        menuApplication.Text = Strings.T("Menu.Application");
        menuSettings.Text = Strings.T("Menu.Settings");
        menuRefresh.Text = Strings.T("Menu.Refresh");
        menuExit.Text = Strings.T("Menu.Exit");
        menuHelp.Text = Strings.T("Menu.Help");
        menuAbout.Text = Strings.T("Menu.About");
        menuShortcuts.Text = Strings.T("Menu.Shortcuts");

        providerLabel.Text = Strings.T("Main.Provider");
        providerCombo.AccessibleName = Strings.T("Main.ProviderAccessible");
        providerStatus.AccessibleName = Strings.T("Main.ProviderStatus");

        searchLabel.Text = Strings.T("Main.Search");
        searchBox.AccessibleName = Strings.T("Main.SearchAccessible");
        searchButton.Text = Strings.T("Main.SearchButton");
        searchButton.AccessibleName = Strings.Plain(Strings.T("Main.SearchButton"));

        UpdateLibraryLabel(providerCombo.SelectedItem as IBookProvider);

        downloadButton.Text = Strings.T("Main.Download");
        downloadButton.AccessibleName = Strings.Plain(Strings.T("Main.Download"));

        cancelButton.Text = Strings.T("Main.Cancel");
        cancelButton.AccessibleName = Strings.Plain(Strings.T("Main.Cancel"));

        menuOpenFolder.Text = Strings.T("Main.OpenFolder");
        menuExport.Text = Strings.T("Menu.Export");
        foreach (var (item, key) in new (ToolStripMenuItem, string)[]
        {
            (menuPlay, "Menu.Play"), (menuView, "Menu.View"), (menuSortTitle, "Menu.SortTitle"),
            (menuSortAuthor, "Menu.SortAuthor"), (menuSortLength, "Menu.SortLength"),
            (menuSortAdded, "Menu.SortAdded"), (menuHideDownloaded, "Menu.HideDownloaded"),
            (menuCheckUpdates, "Menu.CheckUpdates"),
        })
        {
            item.Text = Strings.T(key);
            item.AccessibleName = Strings.Plain(Strings.T(key)).TrimEnd('…', '.').Trim();
        }
        foreach (var (item, key) in new (ToolStripMenuItem, string)[]
        {
            (_menuSortSeries, "Menu.SortSeries"), (_menuStats, "Menu.Stats"), (_menuBackupNow, "Menu.BackupNow"),
            (_bookMenuDownload, "BookMenu.Download"), (_bookMenuPlay, "Menu.Play"), (_bookMenuDetails, "BookMenu.Details"),
        })
        {
            item.Text = Strings.T(key);
            item.AccessibleName = Strings.Plain(Strings.T(key)).TrimEnd('…', '.').Trim();
        }
        _bookMenu.AccessibleName = Strings.T("BookMenu.Name");
        UpdateViewMenu();
        Strings.ApplyDirection(this);
        menuExport.AccessibleName = Strings.Plain(Strings.T("Menu.Export")).TrimEnd('…', '.');
        BookInfo.AlreadyDownloadedText = Strings.T("Main.AlreadyDownloaded");

        progressLabel.Text = Strings.T("Main.Progress") + ":";
        progressBar.AccessibleName = Strings.T("Main.Progress");
        progressStatus.AccessibleName = Strings.T("Main.ProgressStatus");

        statusLabel.AccessibleName = Strings.T("Main.Status");


        if (providerCombo.SelectedItem is IBookProvider provider)
            UpdateAccountButton(provider);
    }

    /// <summary>
    /// The colours this window chooses for itself. Everything else uses the
    /// defaults, which already follow the Windows theme; these are re-applied
    /// whenever Windows changes its colours or accessibility settings.
    /// </summary>
    private void ApplyTheme()
    {
        providerStatus.ForeColor = SystemTheme.HintText;
        libraryHint.ForeColor = SystemTheme.HintText;
        progressStatus.ForeColor = SystemTheme.HintText;

    }

    // ------------------------------------------------------------------
    // provider ComboBox
    // ------------------------------------------------------------------

    private void providerCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressProviderEvents)
            return;

        if (providerCombo.SelectedItem is not IBookProvider provider)
            return;

        _settings.LastProviderId = _settings.RememberLastProvider ? provider.Id : null;
        _settings.Save();

        // A search typed for one catalogue means nothing to another one, and left in
        // the box it quietly filtered the next provider's list down to nothing.
        searchBox.Text = "";

        // The controls that belong to the provider follow it at once, so Tab
        // reaches the right ones; only the (slow) loading waits for the
        // selection to settle.
        ShowProviderControls(provider);
        _loadGeneration++;
        _found = [];
        RefreshBookList();
        SetLibraryHint(Strings.T("Main.Loading"));

        _providerSettle.Stop();
        _providerSettle.Start();
    }

    /// <summary>The account button, the language list, the list caption and the search history for one provider.</summary>
    private void ShowProviderControls(IBookProvider provider)
    {
        UpdateAccountButton(provider);
        UpdateLibraryLabel(provider);
        RefreshSearchHistory();
    }

    /// <summary>
    /// "Your Audible library" for the one provider that lists books you own, and
    /// "Search results" for the catalogues, so the list says what it holds.
    /// </summary>
    private void UpdateLibraryLabel(IBookProvider? provider)
    {
        var key = provider is AudibleProvider ? "Main.BooksAudible" : "Main.Books";
        libraryLabel.Text = Strings.T(key);
        libraryList.AccessibleName = Strings.Plain(Strings.T(key)).TrimEnd(':', ' ');
    }

    private async Task OnProviderChangedAsync()
    {
        if (providerCombo.SelectedItem is not IBookProvider provider)
            return;

        ShowProviderControls(provider);

        // Audible is special: without a live authenticated session there is no
        // library to show, and the app must not invent one.
        if (IsAudible(provider) && !_audible.IsConnected)
        {
            _found = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetProviderStatus(Strings.T("Audible.SignedOutShort"));
            SetStatus(Strings.T("Audible.SignedOutStatus"));
            return;
        }

        var drm = Strings.T(provider.HasDrm ? "Main.HasDrm" : "Main.NoDrm");
        SetProviderStatus(provider.RequiresLogin && !provider.IsConnected
            ? Strings.T("Main.NotSignedIn")
            : IsAudible(provider) && _audible.ActiveAccount is { } account && !string.IsNullOrWhiteSpace(account.Name)
                ? string.Format(Strings.T("Main.SignedInAs"), account.Name, Countries.Name(account.Locale)) + " " + drm
                : drm);

        // "Load my library automatically after signing in" off: wait for an explicit search.
        if (provider is AudibleProvider && !_settings.AudibleLoadLibraryAfterSignIn &&
            string.IsNullOrWhiteSpace(searchBox.Text))
        {
            _found = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.PressSearch"));
            return;
        }

        await LoadBooksAsync();
    }

    /// <summary>Fills the search-box dropdown with this provider's recent searches, newest first.</summary>
    private void RefreshSearchHistory()
    {
        if (providerCombo.SelectedItem is not IBookProvider provider)
            return;

        string? current = searchBox.Text;
        searchBox.Items.Clear();
        foreach (var s in _settings.GetSearches(provider.Id))
            searchBox.Items.Add(s);
        searchBox.Text = current; // keep what the user was typing
    }

    /// <summary>
    /// The sign-in / account button. Audible account management lives in Settings,
    /// so this opens the Audible section there instead of connecting directly.
    /// </summary>
    private void UpdateAccountButton(IBookProvider? provider)
    {
        if (!IsAudible(provider))
        {
            accountButton.Visible = false;
            return;
        }

        accountButton.Visible = true;
        var key = _audible.IsConnected ? "Main.SignOut" : "Main.SignIn";
        accountButton.Text = Strings.T(key);
        accountButton.AccessibleName = Strings.Plain(Strings.T(key));
    }

    /// <summary>
    /// The account button, right on the main window. Signed out, it starts the
    /// sign-in: country first, then Amazon's page. Signed in, it signs out. Nothing
    /// about signing in or out needs a trip into Settings.
    /// </summary>
    private void accountButton_Click(object? sender, EventArgs e)
    {
        if (_audible.IsConnected)
        {
            SignOutOfAudible();
            return;
        }

        StartAudibleSignIn();
    }

    private void SignOutOfAudible()
    {
        var answer = MessageBox.Show(this, Strings.T("Audible.SignOutConfirm"),
            Strings.T("Audible.SignOutTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        _audible.Disconnect();
        SetStatus(Strings.T("Audible.SignedOutAndForgotten"));
        accountButton.Focus();
    }

    /// <summary>
    /// Country, then sign-in, without sending the account holder through Settings.
    /// The country decides which store the account belongs to, so it is the first
    /// thing the sign-in asks for and the only thing offered until it is answered.
    /// </summary>
    private void StartAudibleSignIn()
    {
        var previous = ActiveControl;

        using (var dialog = new AudibleCountryForm(_settings, _audible, SaveChosenCountry))
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetStatus(Strings.T("Audible.SignedIn"));
        }

        // The window is back in front of the account holder, which is where a
        // finished sign-in should leave them.
        if (previous is { CanSelect: true } && previous.Visible)
            previous.Focus();
        else
            providerCombo.Focus();
    }

    /// <summary>
    /// Stores the country the moment it is chosen, so closing the sign-in dialog
    /// without signing in still remembers it — and so the sign-in that follows is
    /// sent to the store on screen.
    /// </summary>
    private void SaveChosenCountry(string code)
    {
        _settings.AudibleLocale = code;
        _settings.AudibleCountryChosen = true;
        _settings.Save();
    }

    // ------------------------------------------------------------------
    // book list
    // ------------------------------------------------------------------

    private void searchButton_Click(object? sender, EventArgs e) =>
        RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Search"));

    private void searchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Search"));
    }

    private async Task LoadBooksAsync()
    {
        if (providerCombo.SelectedItem is not IBookProvider provider)
            return;

        if (IsAudible(provider) && !_audible.IsConnected)
        {
            _found = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetStatus(Strings.T("Audible.SignedOutStatus"));
            return;
        }

        var generation = ++_loadGeneration;

        try
        {
            SetStatus(Strings.T("Main.Loading"));
            SetLibraryHint(Strings.T("Main.Loading"));
            var query = searchBox.Text.Trim();
            var found = await provider.GetBooksAsync(query);

            // The person has moved on (another provider, language or search) while
            // this one was on its way; its answer is no longer the one they want.
            if (generation != _loadGeneration || providerCombo.SelectedItem != provider)
                return;

            _found = [.. found];
            if (!string.IsNullOrWhiteSpace(query))
            {
                _settings.AddSearch(provider.Id, query);
                RefreshSearchHistory();
            }
            RefreshBookList();

            var hidden = _found.Count - _books.Count;
            SetLibraryHint(_books.Count > 0 ? ""
                : hidden > 0 ? Strings.T("Main.AllHidden")
                : Strings.T("Main.NoBooks"));
            // The book language is chosen in Settings, Language; the count says which it is.
            var language = provider is ILanguageFilter { CurrentLanguage.Length: > 0 } filter
                ? filter.SupportedLanguages.FirstOrDefault(l => l.Code == filter.CurrentLanguage)?.DisplayName
                : null;
            SetStatus(hidden > 0
                ? string.Format(Strings.T("Main.HiddenCount"), _books.Count, hidden)
                : language is null
                    ? string.Format(Strings.T("Main.BooksFound"), _books.Count)
                    : string.Format(Strings.T("Main.BooksFoundIn"), _books.Count, language));
        }
        catch (NotSignedInException ex)
        {
            if (generation != _loadGeneration)
                return;
            _found = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetStatus(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation != _loadGeneration)
                return;
            _found = [];
            RefreshBookList();
            Log.Error($"Could not list books: {ex.Message}");
            SetLibraryHint(Strings.T("Main.ListFailed"));
            SetStatus(string.Format(Strings.T("Main.ListFailedStatus"), ex.Message));
        }
    }

    private void RefreshBookList()
    {
        _found = [.. _found.Select(MarkDownloaded)];
        _books = ArrangeBooks(_found);
        _booksBinding.DataSource = _books;
        _booksBinding.ResetBindings(false);
        if (libraryList.Items.Count > 0)
            libraryList.SelectedIndex = 0;
    }

    /// <summary>The rows in the chosen order, without the downloaded ones when those are hidden.</summary>
    private List<BookInfo> ArrangeBooks(IEnumerable<BookInfo> books)
    {
        if (_settings.HideDownloaded)
            books = books.Where(book => !book.AlreadyDownloaded);

        var byTitle = StringComparer.CurrentCultureIgnoreCase;
        return (_settings.SortOrder switch
        {
            "author" => books.OrderBy(b => b.Author, byTitle).ThenBy(b => b.Series ?? "", byTitle).ThenBy(b => b.Title, byTitle),
            "length" => books.OrderByDescending(b => b.LengthMinutes).ThenBy(b => b.Title, byTitle),
            "added" => books.OrderByDescending(b => b.DateAdded ?? DateTimeOffset.MinValue).ThenBy(b => b.Title, byTitle),
            // Each series together, in reading order; books in no series after them.
            "series" => books.OrderBy(b => string.IsNullOrEmpty(b.SeriesName) ? 1 : 0)
                .ThenBy(b => b.SeriesName ?? "", byTitle)
                .ThenBy(b => b.SeriesPosition)
                .ThenBy(b => b.Title, byTitle),
            _ => books.OrderBy(b => b.Title, byTitle),
        }).ToList();
    }

    /// <summary>Re-sorts or re-filters the list in place, keeping the book the cursor was on.</summary>
    private void RearrangeBooks()
    {
        var current = libraryList.SelectedItem as BookInfo;
        _books = ArrangeBooks(_found);
        _booksBinding.DataSource = _books;
        _booksBinding.ResetBindings(false);

        if (libraryList.Items.Count == 0)
            return;

        var index = current is null ? 0 : _books.FindIndex(b => b.BookId == current.BookId && b.ProviderId == current.ProviderId);
        libraryList.ClearSelected();
        libraryList.SelectedIndex = Math.Max(0, index);
    }

    private void SetSortOrder(string order, string nameKey)
    {
        _settings.SortOrder = order;
        _settings.Save();
        UpdateViewMenu();
        RearrangeBooks();
        SetStatus(string.Format(Strings.T("Main.SortedBy"), Strings.T(nameKey)));
    }

    private void ToggleHideDownloaded()
    {
        _settings.HideDownloaded = !_settings.HideDownloaded;
        _settings.Save();
        UpdateViewMenu();
        RearrangeBooks();
        SetStatus(Strings.T(_settings.HideDownloaded ? "Main.DownloadedHidden" : "Main.DownloadedShown"));
    }

    /// <summary>The ticks in the View menu say which order is in use and whether downloaded books are hidden.</summary>
    private void UpdateViewMenu()
    {
        menuSortTitle.Checked = _settings.SortOrder is not ("author" or "length" or "added" or "series");
        menuSortAuthor.Checked = _settings.SortOrder == "author";
        menuSortLength.Checked = _settings.SortOrder == "length";
        menuSortAdded.Checked = _settings.SortOrder == "added";
        _menuSortSeries.Checked = _settings.SortOrder == "series";
        menuHideDownloaded.Checked = _settings.HideDownloaded;
    }

    /// <summary>The row says "already downloaded" when its file is in the download folder.</summary>
    private BookInfo MarkDownloaded(BookInfo book)
    {
        var downloaded = DownloadManager.FindExistingDownload(_settings, book) is not null;
        return book.AlreadyDownloaded == downloaded ? book : book with { AlreadyDownloaded = downloaded };
    }

    /// <summary>Re-checks which rows are downloaded, keeping the selection and position.</summary>
    private void RefreshDownloadedMarks()
    {
        if (_found.Count == 0)
            return;

        var selected = libraryList.SelectedItems.Cast<BookInfo>().Select(b => b.BookId).ToHashSet();
        var top = libraryList.TopIndex;

        _found = [.. _found.Select(MarkDownloaded)];
        _books = ArrangeBooks(_found);
        _booksBinding.DataSource = _books;
        _booksBinding.ResetBindings(false);

        libraryList.ClearSelected();
        for (var i = 0; i < _books.Count; i++)
            if (selected.Contains(_books[i].BookId))
                libraryList.SetSelected(i, true);
        if (top < libraryList.Items.Count)
            libraryList.TopIndex = top;
    }

    private IReadOnlyList<BookInfo> SelectedBooks =>
        libraryList.SelectedItems.Cast<BookInfo>().ToList();

    // ------------------------------------------------------------------
    // download queue
    // ------------------------------------------------------------------

    private void downloadButton_Click(object? sender, EventArgs e)
    {
        if (providerCombo.SelectedItem is not IBookProvider provider)
            return;

        if (IsAudible(provider) && !_audible.IsConnected)
        {
            SetStatus(Strings.T("Audible.SignInBeforeDownload"));
            StartAudibleSignIn();
            return;
        }

        var books = SelectedBooks;
        if (books.Count == 0)
        {
            SetStatus(Strings.T("Main.SelectFirst"));
            return;
        }

        // Every Audible book has its DRM removed. A book that was not bought (an
        // Audible Plus title) gets a notice first, and is left out unless it is accepted.
        var notBought = books.Where(b => b.Purchased == false).ToList();
        if (notBought.Count > 0)
        {
            var titles = string.Join("\n", notBought.Take(10).Select(b => "- " + b.Title)) +
                         (notBought.Count > 10 ? "\n…" : "");
            var answer = MessageBox.Show(this, string.Format(Strings.T("Drm.NotPurchased"), titles),
                Strings.T("Drm.NotPurchasedTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                books = [.. books.Where(b => b.Purchased != false)];
            if (books.Count == 0)
            {
                SetStatus(Strings.T("Main.NothingQueued"));
                return;
            }
        }

        // Downloading a book a second time only makes a " (2)" copy, so ask first.
        var already = books.Where(book => book.AlreadyDownloaded).ToList();
        if (already.Count > 0 && _settings.WarnAboutDuplicates)
        {
            var message = already.Count == 1
                ? string.Format(Strings.T("Main.DuplicateOne"), already[0].Title)
                : string.Format(Strings.T("Main.DuplicateMany"), already.Count, books.Count);
            var choice = MessageBox.Show(this, message, Strings.T("Main.DuplicateTitle"),
                books.Count > already.Count ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            // Yes downloads everything again; No skips the ones already there.
            if (choice == DialogResult.Cancel ||
                (choice == DialogResult.No && books.Count == already.Count))
            {
                SetStatus(Strings.T("Main.NothingQueued"));
                return;
            }

            if (choice == DialogResult.No)
                books = [.. books.Where(book => !book.AlreadyDownloaded)];
        }

        // Which format: asked every time, with the last choice already selected.
        if (AskFormat(provider, books) is not { } chosen)
        {
            SetStatus(Strings.T("Main.NothingQueued"));
            return;
        }

        foreach (var book in books)
            _queue.Enqueue(provider, WithFormat(book, chosen));

        _queue.Start();
        SetStatus(books.Count == 1
            ? string.Format(Strings.T("Main.DownloadStarted"), books[0].Title)
            : string.Format(Strings.T("Main.DownloadsStarted"), books.Count));

        // Focus goes to the progress bar, so the screen reader follows the download.
        progressBar.Focus();
    }

    /// <summary>
    /// The formats this source can deliver for these books, in a small window that
    /// opens when Download is pressed. Null when it is cancelled. The choice is
    /// remembered and offered first next time.
    /// </summary>
    private string? AskFormat(IBookProvider provider, IReadOnlyList<BookInfo> books)
    {
        List<(string Code, string Text)> options;
        string? preselect;

        if (provider is AudibleProvider)
        {
            options =
            [
                ("m4b", Strings.T("Format.M4b")),
                ("mp3", Strings.T("Format.Mp3")),
                ("m4b-chapters", Strings.T("Format.M4bChapters")),
                ("mp3-chapters", Strings.T("Format.Mp3Chapters")),
            ];
            preselect = (_settings.OutputFormat == "mp3" ? "mp3" : "m4b") + (_settings.SplitByChapter ? "-chapters" : "");
        }
        else if (provider is GutenbergProvider)
        {
            // Only the formats at least one of these books really has.
            var codes = new[] { "audio", "epub", "html", "txt" }
                .Where(code => books.Any(b => b.Formats?.ContainsKey(code) == true))
                .ToList();
            if (codes.Count == 0)
                codes = ["epub"];
            options = [.. codes.Select(code => (code, Strings.T(code switch
            {
                "audio" => "Format.Audio",
                "html" => "Format.Html",
                "txt" => "Format.Text",
                _ => "Format.Epub",
            })))];
            preselect = _settings.GutenbergFormat;
        }
        else if (provider is LibriVoxProvider)
        {
            options = [("zip", Strings.T("Format.LibriVoxZip"))];
            preselect = "zip";
        }
        else
        {
            options = [("zip", Strings.T("Format.ArchiveZip"))];
            preselect = "zip";
        }

        var what = books.Count == 1 ? books[0].Title : string.Format(Strings.T("DownloadFormat.Books"), books.Count);
        using var dialog = new DownloadFormatForm(what, options, preselect);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return null;

        var chosen = dialog.ChosenCode;
        if (provider is AudibleProvider)
        {
            _settings.OutputFormat = chosen.StartsWith("mp3", StringComparison.Ordinal) ? "mp3" : "m4b";
            _settings.SplitByChapter = chosen.EndsWith("-chapters", StringComparison.Ordinal);
        }
        else if (provider is GutenbergProvider)
        {
            _settings.GutenbergFormat = chosen;
        }
        _settings.Save();
        return chosen;
    }

    /// <summary>The book as it is to be downloaded: in the chosen format.</summary>
    private static BookInfo WithFormat(BookInfo book, string format) =>
        book.ProviderId == GutenbergProvider.ProviderId
            // A Gutenberg book downloads the file of the chosen format; one that does
            // not have it keeps the file it was listed with.
            ? book.Formats?.TryGetValue(format, out var url) == true
                ? book with { Format = format, DownloadUrl = url }
                : book
            : book.ProviderId == AudibleProvider.ProviderId
                ? book with { Format = format }
                : book;

    private void libraryList_DoubleClick(object? sender, EventArgs e) =>
        downloadButton_Click(sender, e);

    /// <summary>
    /// Enter downloads the selected books, as the help has always promised, and
    /// Ctrl + A selects every book in the list (a list box does not do that itself).
    /// </summary>
    private void libraryList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyData == Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            downloadButton_Click(sender, e);
        }
        else if (e.KeyData == (Keys.Control | Keys.A))
        {
            e.Handled = e.SuppressKeyPress = true;
            SelectAllBooks();
        }
    }

    private void SelectAllBooks()
    {
        if (libraryList.Items.Count == 0)
            return;

        libraryList.BeginUpdate();
        for (var i = 0; i < libraryList.Items.Count; i++)
            libraryList.SetSelected(i, true);
        libraryList.EndUpdate();

        SetStatus(string.Format(Strings.T("Main.AllSelected"), libraryList.Items.Count));
    }

    /// <summary>
    /// Everything known about the book the cursor is on, in a message box a screen
    /// reader reads in full. Ctrl + I, from anywhere in the window.
    /// </summary>
    private void ShowBookDetails()
    {
        if (libraryList.SelectedItem is not BookInfo book)
        {
            SetStatus(Strings.T("Main.SelectFirst"));
            return;
        }

        var lines = new List<string>
        {
            $"{Strings.T("Details.Title")}: {DisplayText.FirstReal(BookInfo.UnknownTitle, book.Title, book.BookId)}",
            $"{Strings.T("Details.Author")}: {DisplayText.FirstReal(BookInfo.UnknownAuthor, book.Author)}",
        };
        if (!DisplayText.IsMissing(book.Narrator))
            lines.Add($"{Strings.T("Details.Narrator")}: {book.Narrator}");
        if (book.LengthMinutes > 0)
            lines.Add($"{Strings.T("Details.Length")}: " +
                      string.Format(Strings.T("Details.LengthValue"), book.LengthMinutes / 60, book.LengthMinutes % 60));
        if (!DisplayText.IsMissing(book.Series))
            lines.Add($"{Strings.T("Details.Series")}: {book.Series}");
        if (NextInSeries(book) is { } next)
            lines.Add($"{Strings.T("Details.NextInSeries")}: {next.Title}" +
                      (next.AlreadyDownloaded ? $" ({BookInfo.AlreadyDownloadedText})" : ""));
        if (!DisplayText.IsMissing(book.Language))
            lines.Add($"{Strings.T("Details.Language")}: {book.Language}");
        if (book.DateAdded is { } added)
            lines.Add($"{Strings.T("Details.Added")}: {added.LocalDateTime:d}");
        if (ProviderRegistry.Find(book.ProviderId) is { } source)
            lines.Add($"{Strings.T("Details.Source")}: {source.DisplayName}");
        lines.Add($"{Strings.T("Details.Id")}: {book.BookId}");
        if (DownloadManager.FindExistingDownload(_settings, book) is { } file)
            lines.Add($"{Strings.T("Details.File")}: {file}");
        if (!DisplayText.IsMissing(book.Description))
            lines.Add($"{Strings.T("Details.Description")}: {book.Description}");
        else if (!DisplayText.IsMissing(book.Info))
            lines.Add(book.Info!);

        using var details = new BookDetailsForm(DisplayText.FirstReal(BookInfo.UnknownTitle, book.Title, book.BookId), lines);
        details.ShowDialog(this);
    }

    /// <summary>The next book of the same series in the list, if there is one.</summary>
    private BookInfo? NextInSeries(BookInfo book) =>
        string.IsNullOrEmpty(book.SeriesName) || book.SeriesPosition <= 0
            ? null
            : _found.Where(b => string.Equals(b.SeriesName, book.SeriesName, StringComparison.OrdinalIgnoreCase) &&
                                b.SeriesPosition > book.SeriesPosition)
                    .OrderBy(b => b.SeriesPosition)
                    .FirstOrDefault();

    // ------------------------------------------------------------------
    // backup and listening statistics
    // ------------------------------------------------------------------

    private async Task BackupNowAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.BackupFolder))
        {
            SetStatus(Strings.T("Backup.NoFolder"));
            OpenSettings(SettingsForm.SectionDownloads);
            return;
        }

        SetStatus(string.Format(Strings.T("Backup.Running"), _settings.BackupFolder));
        var copied = await new DownloadManager(_settings).BackupEverythingAsync();
        SetStatus(string.Format(Strings.T("Backup.Done"), copied, _settings.BackupFolder));
    }

    /// <summary>How much has been listened to, in a message a screen reader reads in full.</summary>
    private void ShowListeningStats()
    {
        var log = _settings.ListeningLog;
        double Sum(Func<DateTime, bool> when) => log
            .Where(e => DateTime.TryParse(e.Key, out var day) && when(day))
            .Sum(e => e.Value);

        var today = DateTime.Today;
        var weekStart = today.AddDays(-6);
        var monthStart = today.AddDays(-29);

        // Days in a row with some listening, ending today or yesterday.
        var streak = 0;
        for (var day = log.ContainsKey(today.ToString("yyyy-MM-dd")) ? today : today.AddDays(-1);
             log.TryGetValue(day.ToString("yyyy-MM-dd"), out var seconds) && seconds > 0;
             day = day.AddDays(-1))
            streak++;

        static string Hours(double seconds) =>
            string.Format(Strings.T("Stats.Duration"), (int)(seconds / 3600), (int)(seconds % 3600 / 60));

        var lines = new[]
        {
            $"{Strings.T("Stats.Today")}: {Hours(Sum(d => d == today))}",
            $"{Strings.T("Stats.Week")}: {Hours(Sum(d => d >= weekStart))}",
            $"{Strings.T("Stats.Month")}: {Hours(Sum(d => d >= monthStart))}",
            $"{Strings.T("Stats.Total")}: {Hours(Sum(_ => true))}",
            $"{Strings.T("Stats.Streak")}: {streak}",
            $"{Strings.T("Stats.Finished")}: {_settings.FinishedBooks.Count}",
            $"{Strings.T("Stats.FinishedMonth")}: {_settings.FinishedBooks.Values.Count(v => DateTime.TryParse(v, out var d) && d >= monthStart)}",
        };

        MessageBox.Show(this, string.Join(Environment.NewLine, lines), Strings.T("Stats.Title"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------
    // the built-in player
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays the selected book: from the book list, or a finished row of the download
    /// queue when that is where the cursor is. A LibriVox zip is unzipped first; an
    /// e-book opens in the program Windows uses for it. Ctrl + P.
    /// </summary>
    private void PlaySelectedBook() => RunSafely(PlaySelectedBookAsync, "opening the player");

    private async Task PlaySelectedBookAsync()
    {
        string? path = null;
        string title;

        if (libraryList.SelectedItem is BookInfo book)
        {
            path = DownloadManager.FindExistingDownload(_settings, book);
            title = book.Title;
            if (path is null)
            {
                SetStatus(string.Format(Strings.T("Main.NotDownloadedYet"), book.Title));
                return;
            }
        }
        else
        {
            SetStatus(Strings.T("Main.SelectFirst"));
            return;
        }

        if (path is null || (!File.Exists(path) && !Directory.Exists(path)))
        {
            SetStatus(string.Format(Strings.T("Main.NotDownloadedYet"), title));
            return;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".zip")
        {
            var folder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
            if (PlayerForm.TracksIn(folder).Count == 0)
            {
                SetStatus(string.Format(Strings.T("Main.Unzipping"), title));
                try
                {
                    await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(path, folder, overwriteFiles: true));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Log.Error($"Could not unzip {path}: {ex.Message}");
                    SetStatus(string.Format(Strings.T("Main.UnzipFailed"), ex.Message));
                    return;
                }
            }
            path = folder;
        }
        else if (File.Exists(path) && !PlayerForm.AudioExtensions.Contains(extension))
        {
            // An e-book or a web page: open it in its own program.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            SetStatus(string.Format(Strings.T("Main.NotPlayable"), title));
            return;
        }

        var previous = ActiveControl;
        using (var player = new PlayerForm(_settings, path, title))
            player.ShowDialog(this);

        if (previous is { CanSelect: true } && previous.Visible)
            previous.Focus();
    }

    // ------------------------------------------------------------------
    // start-up: saved sign-in, new purchases, updates
    // ------------------------------------------------------------------

    private async Task ResumeAudibleSessionAsync()
    {
        if (!_settings.AudibleRememberSignIn || !_audible.HasSavedSignIn || _audible.IsConnected)
            return;

        try
        {
            if (await _audible.TryResumeSessionAsync())
                SetStatus(Strings.T("Main.ResumedSignIn"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Audible: the saved sign-in could not be used at start-up: {ex.Message}");
        }
    }

    /// <summary>Queues the Audible books bought since the last check, when that setting is on.</summary>
    private async Task DownloadNewPurchasesAsync()
    {
        var fresh = await _audible.GetNewPurchasesAsync(
            book => DownloadManager.FindExistingDownload(_settings, book) is not null);

        if (fresh.Count == 0)
            return;

        foreach (var book in fresh)
            _queue.Enqueue(_audible, book);
        _queue.Start();

        Log.Info($"Audible: {fresh.Count} new book(s) queued automatically.");
        SetStatus(string.Format(Strings.T("Main.NewPurchases"), fresh.Count));
    }

    /// <summary>
    /// Asks GitHub for a newer release. Quietly at start-up (a status line only when
    /// there is one), or out loud from the Help menu.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool quiet)
    {
        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var shown = current.ToString(3);

        UpdateInfo? update;
        try
        {
            update = await UpdateChecker.CheckAsync(current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            if (!quiet)
                SetStatus(string.Format(Strings.T("Main.UpdateFailed"), ex.Message));
            return;
        }

        if (update is null)
        {
            if (!quiet)
                SetStatus(string.Format(Strings.T("Main.UpToDate"), shown));
            return;
        }

        var latest = update.Latest.ToString(3);
        Log.Info($"Version {latest} is available (running {shown}).");

        if (quiet)
        {
            SetStatus(string.Format(Strings.T("Main.UpdateStatus"), latest));
            return;
        }

        var answer = MessageBox.Show(this,
            string.Format(Strings.T("Main.UpdateAvailable"), latest, shown),
            Strings.T("Main.UpdateAvailableTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = update.Url, UseShellExecute = true });
    }

    /// <summary>
    /// Opens File Explorer on the selected book's downloaded file, highlighted, so it
    /// can be played, copied or moved. Ctrl + Shift + O.
    /// </summary>
    private void ShowBookFile()
    {
        if (libraryList.SelectedItem is not BookInfo book)
        {
            SetStatus(Strings.T("Main.SelectFirst"));
            return;
        }

        if (DownloadManager.FindExistingDownload(_settings, book) is not { } file)
        {
            SetStatus(string.Format(Strings.T("Main.NotDownloadedYet"), book.Title));
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open File Explorer: {ex.Message}");
            SetStatus(string.Format(Strings.T("Main.OpenFolderFailed"), ex.Message));
        }
    }

    /// <summary>
    /// Save the book list as it stands (title, author, narrator, length, language,
    /// id, downloaded) to a CSV file that opens in Excel or any text editor.
    /// </summary>
    private void ExportBookList()
    {
        if (_books.Count == 0)
        {
            SetStatus(Strings.T("Main.NothingToExport"));
            return;
        }

        var provider = providerCombo.SelectedItem as IBookProvider;
        using var dialog = new SaveFileDialog
        {
            Title = Strings.Plain(Strings.T("Menu.Export")).TrimEnd('…', '.'),
            Filter = Strings.T("Export.Filter"),
            FileName = $"{provider?.Id ?? "books"}-{DateTime.Now:yyyy-MM-dd}.csv",
            InitialDirectory = Directory.Exists(_settings.OutputFolder)
                ? _settings.OutputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            static string Cell(string? value)
            {
                var text = value ?? "";
                return text.IndexOfAny([',', '"', '\n', '\r']) >= 0
                    ? "\"" + text.Replace("\"", "\"\"") + "\""
                    : text;
            }

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Title,Author,Narrator,Length (minutes),Language,Source,Id,Downloaded");
            foreach (var book in _books)
                csv.AppendLine(string.Join(",",
                    Cell(book.Title), Cell(book.Author), Cell(book.Narrator),
                    book.LengthMinutes > 0 ? book.LengthMinutes.ToString() : "",
                    Cell(book.Language), Cell(book.ProviderId), Cell(book.BookId),
                    book.AlreadyDownloaded ? "yes" : "no"));

            // UTF-8 with a byte order mark, so Excel shows Hindi and accented names properly.
            File.WriteAllText(dialog.FileName, csv.ToString(), new System.Text.UTF8Encoding(true));
            Log.Info($"Exported {_books.Count} books to {dialog.FileName}.");
            SetStatus(string.Format(Strings.T("Main.Exported"), _books.Count, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not export the book list: {ex.Message}");
            SetStatus(string.Format(Strings.T("Main.ExportFailed"), ex.Message));
        }
    }

    private void cancelButton_Click(object? sender, EventArgs e)
    {
        _queue.CancelActive();
        SetStatus(Strings.T("Main.Cancelling"));
    }

    private void OnQueueChanged()
    {
        if (!IsHandleCreated || Interlocked.Exchange(ref _queueChangePending, 1) == 1)
            return;

        BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _queueChangePending, 0);

            cancelButton.Enabled = _queue.Items.Any(i => i.Status == QueueStatus.Active);

            var active = _queue.Items.FirstOrDefault(i => i.Status == QueueStatus.Active);
            UpdateProgress(active);
            ReportFinishedDownloads();

            _lastAnnounced = active;
        });
    }

    /// <summary>
    /// Reflect the active download in the progress bar. The bar is exposed as a
    /// real progress-bar control, and milestones are announced through the status
    /// live region — never through a built-in voice.
    /// </summary>
    private void UpdateProgress(QueueItem? active)
    {
        if (active is null)
        {
            progressBar.Value = 0;
            progressStatus.Text = string.Empty;
            _lastProgressMilestone = -1;
            return;
        }

        // A new download starts counting from zero again.
        if (_lastAnnounced?.Id != active.Id)
            _lastProgressMilestone = -1;

        var percent = Math.Clamp((int)Math.Round(active.Percent), 0, 100);
        if (progressBar.Value != percent)
            progressBar.Value = percent;

        // A beep for every percent, the way NVDA sounds a progress bar. While the bar
        // has focus NVDA beeps for it itself, so the app stays quiet then.
        if (_settings.ProgressBeeps && !progressBar.Focused && _lastBeep != (active.Id, percent))
        {
            _lastBeep = (active.Id, percent);
            Sounds.ProgressBeep(percent);
        }

        // The progress live region shows where the active download has got to.
        // Changing its text alone says nothing to a screen reader; only the
        // milestones below announce, so progress never chatters.
        var text = string.Format(Strings.T("Main.ProgressStatusText"), active.Book.Title, percent);
        if (progressStatus.Text != text)
            progressStatus.Text = text;

        if (!_settings.AnnounceProgress)
            return;

        // The percentage every 10 percent; the book's name only when it starts.
        var milestone = percent / 10 * 10;
        if (milestone >= 10 && milestone != _lastProgressMilestone && percent >= milestone)
        {
            _lastProgressMilestone = milestone;
            AnnounceProgress(string.Format(Strings.T("Main.ProgressPercent"), milestone));
        }
        else if (_lastAnnounced?.Id != active.Id)
        {
            AnnounceProgress(string.Format(Strings.T("Main.ProgressAnnouncement"),
                active.Book.Title, 0));
        }
    }

    /// <summary>
    /// Each book that has just finished is reported once: a success gets the success
    /// sound and is named, and its row in the book list now says "already
    /// downloaded"; a failure is announced with its reason. Nothing else on the
    /// window changes, so the screen reader is not given a whole list to re-read.
    /// </summary>
    private void ReportFinishedDownloads()
    {
        foreach (var item in _queue.Items.Where(i => i.Status is QueueStatus.Completed or QueueStatus.Failed))
        {
            if (!_finishedDownloads.Add(item.Id))
                continue;

            if (item.Status == QueueStatus.Completed)
            {
                AnnounceDownloaded(item.Book.Title);
                MarkOneDownloaded(item.Book.BookId);
            }
            else
            {
                var message = string.Format(Strings.T("Main.BookFailed"), item.Book.Title, item.Error);
                statusLabel.Text = message;
                ScreenReader.RaiseAlert(statusLabel);
            }
        }
    }

    /// <summary>Marks one finished book in the list, touching only its own row.</summary>
    private void MarkOneDownloaded(string bookId)
    {
        var found = _found.FindIndex(b => b.BookId == bookId);
        if (found < 0)
            return;
        _found[found] = MarkDownloaded(_found[found]);

        if (_settings.HideDownloaded)
        {
            RefreshDownloadedMarks();
            return;
        }

        var shown = _books.FindIndex(b => b.BookId == bookId);
        if (shown < 0)
            return;
        _books[shown] = _found[found];
        _booksBinding.ResetItem(shown);
    }

    // ------------------------------------------------------------------
    // completion notification
    // ------------------------------------------------------------------

    private void OnQueueFinished(QueueFinishedSummary summary)
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            var message = summary.Failed == 0 && summary.Cancelled == 0
                ? string.Format(Strings.T("Main.AllFinishedOk"), summary.Completed)
                : string.Format(Strings.T("Main.AllFinishedMixed"),
                    summary.Completed, summary.Failed, summary.Cancelled);

            Log.Info(message);
            progressBar.Value = 0;
            progressStatus.Text = string.Empty;

            // Only a batch of several books is summed up; one book was already named.
            if (summary.Completed + summary.Failed + summary.Cancelled > 1)
                SetStatus(message);

            // Back to the book list, where the next book is chosen.
            if (progressBar.Focused || cancelButton.Focused)
                libraryList.Focus();

            if (_settings.OpenFolderWhenFinished)
                OpenDownloadFolder();

            // Nothing is kept: the next Download starts from an empty list.
            foreach (var item in _queue.Items.Where(i => i.Status == QueueStatus.Failed).ToList())
                _queue.Remove(item);
            _queue.ClearFinished();
        });
    }

    /// <summary>
    /// A book has downloaded: "Download successful" is said through the screen
    /// reader, and a Windows notification appears with Windows' own notification
    /// sound. Where Windows cannot show notifications, its standard sound plays instead.
    /// </summary>
    private void AnnounceDownloaded(string title)
    {
        var message = string.Format(Strings.T("Main.BookDownloaded"), title);
        progressStatus.Text = message;
        progressStatus.Announce();

        if (!_settings.NotifyEachDownload)
            return;

        try
        {
            // No audio element: Windows plays its default notification sound.
            new ToastContentBuilder()
                .AddText(Strings.T("Notify.Title"))
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Warn($"Windows notification unavailable ({ex.Message}); playing the system sound instead.");
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Remove this app's toasts from the Action Center and release the shortcut.
        try { ToastNotificationManagerCompat.Uninstall(); } catch { /* best effort */ }
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------
    // settings, about and help
    // ------------------------------------------------------------------

    /// <summary>
    /// Open the Settings dialog on a given section, then put focus back exactly
    /// where it was and refresh whatever may have changed (sign-in, folder, …).
    /// </summary>
    private void OpenSettings(int section) =>
        RunSafely(() => OpenSettingsAsync(section), Strings.T("Main.Tasks.Settings"));

    private async Task OpenSettingsAsync(int section)
    {
        var previous = ActiveControl;

        using (var dialog = new SettingsForm(_settings, _audible, section))
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetStatus(string.Format(Strings.T("Main.SettingsSaved"), _settings.OutputFolder));
        }

        ApplyLocalization();
        _settings.Save();

        if (previous is { CanSelect: true } && previous.Visible)
            previous.Focus();
        else
            providerCombo.Focus();

        await OnProviderChangedAsync();
    }

    /// <summary>
    /// The live Audible session appeared or went away — however it changed. The
    /// account button and the book list follow that one signal, so the main window
    /// cannot keep offering "Sign in…" over a session that already exists.
    /// </summary>
    private void OnAudibleConnectionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
            return;

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(new Action(() => OnAudibleConnectionChanged(sender, e)));
            return;
        }

        OnAudibleAccountChanged();
    }

    /// <summary>Re-reads everything that depends on the Audible session.</summary>
    private void OnAudibleAccountChanged()
    {
        if (IsDisposed)
            return;

        // The button belongs to whatever source is on screen: an automatic sign-in
        // while LibriVox is chosen must not put an Audible button next to it.
        UpdateAccountButton(providerCombo.SelectedItem as IBookProvider);

        if (!IsAudible(providerCombo.SelectedItem as IBookProvider))
            return;

        if (!_audible.IsConnected)
        {
            _found = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetProviderStatus(Strings.T("Audible.SignedOutShort"));
            return;
        }

        // Signed in: carry straight on into the library, so the list is already
        // there when the Settings dialog closes.
        if (_settings.AudibleLoadLibraryAfterSignIn)
            RunSafely(OnProviderChangedAsync, Strings.T("Main.Tasks.Search"));
    }

    private void ShowAbout()
    {
        var previous = ActiveControl;
        using (var dialog = new AboutForm())
            dialog.ShowDialog(this);

        if (previous is { CanSelect: true } && previous.Visible)
            previous.Focus();
        else
            providerCombo.Focus();
    }

    private void ShowKeyboardHelp()
    {
        if (_helpOpen)
            return;

        _helpOpen = true;
        try
        {
            var previous = ActiveControl;
            // The window is handed over so the help can list the Alt keys its own
            // labels offer, instead of a list written by hand that outlives them.
            using (var dialog = new KeyboardHelpForm(this))
                dialog.ShowDialog(this);

            if (previous is { CanSelect: true } && previous.Visible)
                previous.Focus();
            else
                providerCombo.Focus();
        }
        finally
        {
            _helpOpen = false;
        }
    }

    /// <summary>F1 opens the shortcut list, wherever focus happens to be.</summary>
    private void MainForm_HelpRequested(object? sender, HelpEventArgs hevent)
    {
        hevent.Handled = true;
        ShowKeyboardHelp();
    }

    private void OpenDownloadFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settings.OutputFolder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open the download folder: {ex.Message}");
            SetStatus(string.Format(Strings.T("Main.OpenFolderFailed"), ex.Message));
        }
    }

    // ------------------------------------------------------------------
    // Audible sign-in window (must run on the UI thread)
    // ------------------------------------------------------------------

    /// <summary>
    /// The Audible library calls this from a background thread when the account
    /// holder has to sign in. The window shows Amazon's own page, takes the
    /// redirect that carries the authorization code and closes itself, so the
    /// sign-in finishes here instead of on a web page. Marshalling to the UI
    /// thread is what makes the window appear reliably instead of silently
    /// failing.
    /// </summary>
    public string? ShowSignIn(AudibleSignInRequest request)
    {
        if (!IsHandleCreated || IsDisposed)
            return null;

        if (InvokeRequired)
            return (string?)Invoke(new Func<string?>(() => ShowSignIn(request)));

        // The Settings dialog is what asks for the sign-in, so it stays in front
        // of the main window while the browser is open.
        using var dialog = new AudibleSignInForm(request);
        var responseUrl = dialog.ShowAndGetResponseUrl(ActiveForm ?? this);

        // A window that could not be used is not a cancellation: the account
        // holder has to read why, or they will press Sign in again and again.
        if (responseUrl is null && dialog.FailureMessage is { } failure)
            throw new InvalidOperationException(failure);

        return responseUrl;
    }

    // ------------------------------------------------------------------
    // keyboard shortcuts
    // ------------------------------------------------------------------

    /// <summary>
    /// What the window does for each shortcut it answers. The table in
    /// <see cref="Shortcuts"/> says which keys those are, so the help screen lists
    /// exactly what is answered here rather than a list that was written once.
    /// </summary>
    /// <remarks>
    /// This is the only place a window shortcut is given something to do, so the
    /// offline checks can ask it — action by action — whether every shortcut the help
    /// promises really does something. Alt-letter keys are deliberately absent: those
    /// are the window's own mnemonics, and Windows routes them to the focused control's
    /// label. Repeating them here would fire an action twice on some controls and not
    /// at all on others.
    /// </remarks>
    private static readonly Dictionary<ShortcutAction, Action<MainForm>> _windowActions = new()
    {
        [ShortcutAction.Quit] = form => form.Close(),
        [ShortcutAction.BookDetails] = form => form.ShowBookDetails(),
        [ShortcutAction.ShowBookFile] = form => form.ShowBookFile(),
        [ShortcutAction.SearchHistory] = form =>
        {
            // the search-history dropdown, from anywhere on the form
            form.searchBox.Focus();
            form.searchBox.DroppedDown = true;
        },
    };

    /// <summary>The actions the window answers, for the offline checks to compare.</summary>
    internal static IReadOnlyCollection<ShortcutAction> AnsweredActions => _windowActions.Keys;

    /// <summary>
    /// Answer the window's own shortcuts. Which keys those are is the table's
    /// business, and what they do is <see cref="_windowActions"/>'s; this only connects
    /// the two to the key that was pressed.
    /// </summary>
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        var shortcut = Shortcuts.FindForWindow(e.KeyData);

        if (shortcut is null || !_windowActions.TryGetValue(shortcut.Action, out var action))
            return;

        action(this);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    // ------------------------------------------------------------------
    // status and announcements
    // ------------------------------------------------------------------

    /// <summary>
    /// Run work the UI started, so an unexpected failure is reported instead of
    /// bringing the application down. Every event handler goes through here.
    /// </summary>
    private async void RunSafely(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // A deliberate cancellation is not an error.
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex, what);
        }
    }

    /// <summary>Tell the user, and the log, about something the app did not expect.</summary>
    private void ReportUnexpected(Exception ex, string what)
    {
        Log.Error($"Unexpected problem while {what}: {ex}");

        if (IsDisposed || Disposing)
            return;

        var summary = string.Format(Strings.T("Main.UnexpectedError"), what, ex.Message);
        statusLabel.Text = summary;
        ScreenReader.RaiseAlert(statusLabel);

        MessageBox.Show(this,
            string.Format(Strings.T("Main.UnexpectedErrorDetail"), what, ex.Message, Log.FilePath),
            Strings.T("Main.UnexpectedErrorTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SetProviderStatus(string message) => providerStatus.Text = message;

    private void SetLibraryHint(string message) => libraryHint.Text = message;

    /// <summary>Update the status line and let the screen reader read it.</summary>
    private void SetStatus(string message)
    {
        statusLabel.Text = message;

        if (!string.IsNullOrWhiteSpace(message) && _settings.AnnounceStatusChanges)
            statusLabel.Announce();
    }

    /// <summary>
    /// Announce a progress milestone on the progress live region. Progress has a
    /// region of its own so that a download finishing, or starting, cannot wipe
    /// out the last status message — the status region keeps status text only.
    /// </summary>
    private void AnnounceProgress(string message)
    {
        if (!_settings.AnnounceProgress)
            return;

        progressStatus.Text = message;
        progressStatus.Announce();
    }

}
