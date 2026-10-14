using AudiobookDownloader.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AudiobookDownloader.WinForms;

public partial class MainForm : Form, IKeyPrompt, IAudibleSignInWindow
{
    private readonly AppSettings _settings;
    private readonly DownloadQueue _queue;
    private readonly AudibleProvider _audible;
    private readonly BindingSource _booksBinding = new();
    private readonly BindingSource _queueBinding = new();

    /// <summary>What the provider returned for the last search.</summary>
    private List<BookInfo> _found = [];

    /// <summary>What the list shows: <see cref="_found"/>, sorted, without hidden rows.</summary>
    private List<BookInfo> _books = [];
    private QueueItem? _lastAnnounced;
    private Panel _notifyPanel = null!;
    private Label _notifyLabel = null!;
    private Button _notifyDismiss = null!;

    private bool _suppressProviderEvents;
    private bool _suppressLanguageEvents;
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
    private ToolStripMenuItem _bookMenuPlay = null!;
    private ToolStripMenuItem _bookMenuDetails = null!;
    private ToolStripMenuItem _bookMenuStore = null!;
    private ToolStripMenuItem _bookMenuAddWish = null!;
    private ToolStripMenuItem _bookMenuRemoveWish = null!;

    private readonly System.Windows.Forms.Timer _providerSettle = new() { Interval = 450 };
    private readonly System.Windows.Forms.Timer _languageSettle = new() { Interval = 450 };

    /// <summary>The row of the status group that holds the activity log.</summary>
    private const int LogRowIndex = 1;

    /// <summary>Its height when shown, matching the designer.</summary>
    private const float LogRowHeight = 100F;

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

        // The queue is kept in a file beside the settings, so a batch that is
        // interrupted by closing the app is picked up again by the next run.
        _queue = new DownloadQueue(new DownloadManager(_settings), QueueStore.Default())
        {
            Parallelism = () => _settings.MaxParallelDownloads,
        };
        Log.MessageLogged += OnLogMessage;
        _queue.Changed += OnQueueChanged;
        _queue.QueueFinished += OnQueueFinished;

        RegisterProviders();
        BuildNotificationPanel();
        WireMenus();

        _providerSettle.Tick += (_, _) =>
        {
            _providerSettle.Stop();
            RunSafely(OnProviderChangedAsync, Strings.T("Main.Tasks.Provider"));
        };
        _languageSettle.Tick += (_, _) =>
        {
            _languageSettle.Stop();
            RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Language"));
        };

        // The language list reads "Hindi", not "Hindi (hi)": the code is how the
        // search is sent, never how a language is named to a person.
        languageCombo.DisplayMember = nameof(LanguageOption.DisplayName);

        // The SelectedIndexChanged handler is wired up in the designer. Filling the
        // list and restoring the last provider happens with events suppressed, so
        // nothing is loaded or saved twice; the first real load happens in Load.
        _suppressProviderEvents = true;

        providerCombo.DataSource = ProviderRegistry.Providers;
        providerCombo.DisplayMember = nameof(IBookProvider.DisplayName);

        // No DisplayMember: ListBox falls back to BookInfo.ToString() = "Title - Author",
        // which is what NVDA should read for every row.
        libraryList.DataSource = _booksBinding;

        // No DisplayMember either: QueueItem.ToString() reads as
        // "Title - Author — active, 45 percent" etc.
        queueList.DataSource = _queueBinding;

        ApplyLocalization();
        ApplyAccessibilityPreferences();

        // Follow Windows high contrast, and keep following it while the app runs.
        SystemTheme.Follow(this, ApplyTheme);

        var saved = _settings.RememberLastProvider
            ? ProviderRegistry.Find(_settings.LastProviderId) ?? ProviderRegistry.Providers[0]
            : ProviderRegistry.Providers[0];
        providerCombo.SelectedItem = saved;

        _suppressProviderEvents = false;

        Load += async (_, _) =>
        {
            // An unfinished batch comes back the moment the window opens, so its rows
            // are on screen straight away; it is announced afterwards, so that the
            // message does not talk over the first screen's own.
            var unfinished = ResumeUnfinishedDownloads();

            // A remembered Audible sign-in is simply used: no button to press, and no
            // sign-in page unless the saved one has stopped working.
            await ResumeAudibleSessionAsync();

            await OnProviderChangedAsync();

            AnnounceUnfinishedDownloads(unfinished);

            if (_settings.AudibleAutoDownloadNew && _audible.IsConnected)
                RunSafely(DownloadNewPurchasesAsync, "looking for new Audible books");

            if (_settings.CheckForUpdates)
                RunSafely(() => CheckForUpdatesAsync(quiet: true), "checking for updates");
        };

        FormClosing += (_, _) =>
        {
            _providerSettle.Stop();
            _languageSettle.Stop();
            Log.MessageLogged -= OnLogMessage;
            _queue.Changed -= OnQueueChanged;
            _queue.QueueFinished -= OnQueueFinished;

            _audible.ConnectionChanged -= OnAudibleConnectionChanged;

            // Honour "do not remember the sign-in" by removing the stored session.
            if (!_settings.AudibleRememberSignIn)
                _audible.ForgetSavedSignIn();
        };
    }

    /// <summary>
    /// Start the downloads an earlier run left unfinished, and report how many came
    /// back. Queued work is the user's own, and closing the app in the middle of a
    /// batch is not a reason to make them find and queue every book again.
    /// </summary>
    /// <remarks>
    /// Nothing else happens here: items that failed or were cancelled come back as
    /// they were left, waiting for Retry, because a cancel is an instruction and not
    /// an accident to be undone on the next start.
    /// </remarks>
    private int ResumeUnfinishedDownloads()
    {
        var restored = _queue.RestoreSaved();

        if (restored > 0 && _queue.Items.Any(item => item.Status == QueueStatus.Pending))
            _queue.Start();

        return restored;
    }

    /// <summary>
    /// Say what the unfinished batch is doing: resuming, or waiting to be retried.
    /// Nothing is ever started in silence.
    /// </summary>
    private void AnnounceUnfinishedDownloads(int restored)
    {
        if (restored == 0)
            return;

        var pending = _queue.Items.Count(item => item.Status == QueueStatus.Pending);

        SetStatus(pending > 0
            ? string.Format(Strings.T("Main.ResumingQueue"), pending)
            : string.Format(Strings.T("Main.RestoredQueue"), restored));
    }

    private void RegisterProviders()
    {
        ProviderRegistry.Reset();
        ProviderRegistry.Register(_audible);
        ProviderRegistry.Register(new AudibleStoreProvider(_audible));
        ProviderRegistry.Register(new LibriVoxProvider(_settings));
        ProviderRegistry.Register(new InternetArchiveProvider());
        ProviderRegistry.Register(new GutenbergProvider(_settings));
        ProviderRegistry.Register(new LocalFilesProvider(_settings, this));
    }

    /// <summary>Audible itself, or its store: both need the Audible sign-in.</summary>
    private static bool IsAudible(IBookProvider? provider) => provider is AudibleProvider or AudibleStoreProvider;

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
        _bookMenuPlay = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl + P" };
        _bookMenuPlay.Click += (_, _) => PlaySelectedBook();
        _bookMenuDetails = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl + I" };
        _bookMenuDetails.Click += (_, _) => ShowBookDetails();
        _bookMenuStore = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl + Shift + S" };
        _bookMenuStore.Click += (_, _) => OpenStorePage();
        _bookMenuAddWish = new ToolStripMenuItem();
        _bookMenuAddWish.Click += (_, _) => RunSafely(() => ChangeWishlistAsync(add: true), "changing the wishlist");
        _bookMenuRemoveWish = new ToolStripMenuItem();
        _bookMenuRemoveWish.Click += (_, _) => RunSafely(() => ChangeWishlistAsync(add: false), "changing the wishlist");
        _bookMenu.Items.AddRange([_bookMenuPlay, _bookMenuDetails, new ToolStripSeparator(), _bookMenuStore, _bookMenuAddWish, _bookMenuRemoveWish]);
        _bookMenu.Opening += (_, _) =>
        {
            var audible = IsAudible(providerCombo.SelectedItem as IBookProvider);
            _bookMenuStore.Visible = _bookMenuAddWish.Visible = _bookMenuRemoveWish.Visible = audible;
        };
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
        menuKeys.Click += (_, _) => ShowDecryptionKeyHelp();
        menuExport.Click += (_, _) => ExportBookList();
        menuPlay.Click += (_, _) => PlaySelectedBook();
        menuUnlock.Click += (_, _) => RemoveDrmFromDownloads(askFirst: false);
        menuSortTitle.Click += (_, _) => SetSortOrder("title", "Main.SortNameTitle");
        menuSortAuthor.Click += (_, _) => SetSortOrder("author", "Main.SortNameAuthor");
        menuSortLength.Click += (_, _) => SetSortOrder("length", "Main.SortNameLength");
        menuSortAdded.Click += (_, _) => SetSortOrder("added", "Main.SortNameAdded");
        menuHideDownloaded.Click += (_, _) => ToggleHideDownloaded();
        menuCheckUpdates.Click += (_, _) => RunSafely(() => CheckForUpdatesAsync(quiet: false), "checking for updates");

        // The queue's actions, reachable from the queue's own menu AND from the
        // keyboard shortcuts. They call the same methods, so the two cannot drift
        // apart, and nothing has to be a button on the window to be usable.
        queueMenuRetry.Click += (_, _) => RetrySelectedQueueItem();
        queueMenuRemove.Click += (_, _) => RemoveSelectedQueueItem();
        queueMenuClearFinished.Click += (_, _) => ClearFinishedQueueItems();
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

        languageLabel.Text = Strings.T("Main.Language");
        languageCombo.AccessibleName = Strings.T("Main.LanguageAccessible");
        languageRow.Visible = languageCombo.Visible = languageLabel.Visible =
            providerCombo.SelectedItem is ILanguageFilter;

        UpdateLibraryLabel(providerCombo.SelectedItem as IBookProvider);

        downloadButton.Text = Strings.T("Main.Download");
        downloadButton.AccessibleName = Strings.Plain(Strings.T("Main.Download"));

        cancelButton.Text = Strings.T("Main.Cancel");
        cancelButton.AccessibleName = Strings.Plain(Strings.T("Main.Cancel"));

        // The queue's actions are localised where they are shown: on the queue list's
        // context menu, which is the only place they appear.
        queueMenuRetry.Text = Strings.T("QueueMenu.Retry");
        queueMenuRetry.AccessibleName = Strings.Plain(Strings.T("QueueMenu.Retry"));

        queueMenuRemove.Text = Strings.T("QueueMenu.Remove");
        queueMenuRemove.AccessibleName = Strings.Plain(Strings.T("QueueMenu.Remove"));

        queueMenuClearFinished.Text = Strings.T("QueueMenu.ClearFinished");
        queueMenuClearFinished.AccessibleName = Strings.Plain(Strings.T("QueueMenu.ClearFinished"));

        menuOpenFolder.Text = Strings.T("Main.OpenFolder");
        menuExport.Text = Strings.T("Menu.Export");
        foreach (var (item, key) in new (ToolStripMenuItem, string)[]
        {
            (menuPlay, "Menu.Play"), (menuView, "Menu.View"), (menuSortTitle, "Menu.SortTitle"),
            (menuSortAuthor, "Menu.SortAuthor"), (menuSortLength, "Menu.SortLength"),
            (menuSortAdded, "Menu.SortAdded"), (menuHideDownloaded, "Menu.HideDownloaded"),
            (menuCheckUpdates, "Menu.CheckUpdates"), (menuUnlock, "Menu.UnlockExisting"),
        })
        {
            item.Text = Strings.T(key);
            item.AccessibleName = Strings.Plain(Strings.T(key)).TrimEnd('…', '.').Trim();
        }
        foreach (var (item, key) in new (ToolStripMenuItem, string)[]
        {
            (_menuSortSeries, "Menu.SortSeries"), (_menuStats, "Menu.Stats"), (_menuBackupNow, "Menu.BackupNow"),
            (_bookMenuPlay, "Menu.Play"), (_bookMenuDetails, "BookMenu.Details"), (_bookMenuStore, "BookMenu.Store"),
            (_bookMenuAddWish, "BookMenu.AddWish"), (_bookMenuRemoveWish, "BookMenu.RemoveWish"),
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
        menuKeys.Text = Strings.T("Main.Keys");

        progressLabel.Text = Strings.T("Main.Progress") + ":";
        progressBar.AccessibleName = Strings.T("Main.Progress");
        progressStatus.AccessibleName = Strings.T("Main.ProgressStatus");

        queueList.AccessibleName = Strings.T("Main.QueueList");

        statusLabel.AccessibleName = Strings.T("Main.Status");
        logBox.AccessibleName = Strings.T("Main.Log");

        if (_notifyPanel is not null)
        {
            _notifyPanel.AccessibleName = Strings.T("Main.Notification");
            _notifyLabel.AccessibleName = Strings.T("Main.NotificationMessage");
            _notifyDismiss.Text = Strings.T("Main.GotIt");
            _notifyDismiss.AccessibleName = Strings.Plain(Strings.T("Main.GotIt"));
        }

        if (providerCombo.SelectedItem is IBookProvider provider)
            UpdateAccountButton(provider);
    }

    private void ApplyAccessibilityPreferences()
    {
        SetActivityLogVisible(_settings.KeepActivityLogVisible);
    }

    /// <summary>
    /// Show or hide the activity log. The log sits in its own row inside the
    /// status group, and a hidden control still leaves that row's height behind
    /// as an empty strip, so the row itself is collapsed and given its height
    /// back.
    /// </summary>
    internal void SetActivityLogVisible(bool visible)
    {
        logBox.Visible = visible;
        statusLayout.RowStyles[LogRowIndex].Height = visible ? LogRowHeight : 0F;
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
        logBox.BackColor = SystemTheme.ReadOnlyBackground;

        if (_notifyPanel is not null)
        {
            _notifyPanel.BackColor = SystemTheme.NoticeBackground;
            _notifyLabel.ForeColor = SystemTheme.NoticeText;
        }
    }

    /// <summary>
    /// Ctrl+L shows or hides the activity log and remembers the choice; Alt+L
    /// goes to it, showing it first if it is hidden. Without this the shortcut
    /// did nothing at all once the log had been hidden in Settings.
    /// </summary>
    private void ToggleActivityLog()
    {
        var show = !logBox.Visible;
        _settings.KeepActivityLogVisible = show;
        _settings.Save();

        SetActivityLogVisible(show);
        SetStatus(Strings.T(show ? "Main.LogShown" : "Main.LogHidden"));

        if (show)
            logBox.Focus();
    }

    private void ShowActivityLog()
    {
        if (!logBox.Visible)
        {
            _settings.KeepActivityLogVisible = true;
            _settings.Save();
            SetActivityLogVisible(true);
        }

        logBox.Focus();
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
        UpdateLanguageSelector(provider);
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
        var key = provider is AudibleProvider ? "Main.BooksAudible"
            : provider is AudibleStoreProvider ? "Main.BooksStore"
            : "Main.Books";
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

        var drm = provider.HasDrm
            ? Strings.T(_settings.DrmRemovalEnabled ? "Main.HasDrm" : "Main.DrmKept")
            : Strings.T("Main.NoDrm");
        SetProviderStatus(provider.RequiresLogin && !provider.IsConnected
            ? Strings.T("Main.NotSignedIn")
            : IsAudible(provider) && _audible.ActiveAccount is { } account && !string.IsNullOrWhiteSpace(account.Name)
                ? string.Format(Strings.T("Main.SignedInAs"), account.Name, Countries.Name(account.Locale)) + " " + drm
                : drm);

        if (provider is AudibleStoreProvider && string.IsNullOrWhiteSpace(searchBox.Text))
            SetLibraryHint(Strings.T("Store.Hint"));

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
    /// Show the book-language selector only for providers that can filter by
    /// language, and fill it with every language that provider supports.
    /// </summary>
    private void UpdateLanguageSelector(IBookProvider provider)
    {
        var filter = provider as ILanguageFilter;

        // The whole row appears for Project Gutenberg — the only source with a
        // book language — and disappears completely for every other provider.
        languageRow.Visible = filter is not null;
        languageLabel.Visible = filter is not null;
        languageCombo.Visible = filter is not null;
        languageCombo.Enabled = filter is not null;

        _suppressLanguageEvents = true;
        try
        {
            languageCombo.Items.Clear();
            if (filter is null)
                return;

            languageCombo.Items.Add(new LanguageOption("", Strings.T("Main.AllLanguages")));
            foreach (var language in filter.SupportedLanguages.Where(l => !string.IsNullOrWhiteSpace(l.Code)))
                languageCombo.Items.Add(language);

            var current = languageCombo.Items.Cast<LanguageOption>()
                              .FirstOrDefault(l => l.Code == filter.CurrentLanguage)
                          ?? languageCombo.Items[0];
            languageCombo.SelectedItem = current;
        }
        finally
        {
            _suppressLanguageEvents = false;
        }
    }

    private void languageCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressLanguageEvents)
            return;

        if (providerCombo.SelectedItem is not ILanguageFilter filter ||
            languageCombo.SelectedItem is not LanguageOption language)
            return;

        filter.CurrentLanguage = language.Code;
        _settings.Save();

        // Nothing is announced per key press (the screen reader reads the language
        // the list moved to) and the search waits until the choice has settled.
        _loadGeneration++;
        _languageSettle.Stop();
        _languageSettle.Start();
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
            var language = provider is ILanguageFilter { CurrentLanguage.Length: > 0 } &&
                           languageCombo.SelectedItem is LanguageOption { Code.Length: > 0 } chosen
                ? chosen.DisplayName
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
        var downloaded = book.ProviderId != LocalFilesProvider.ProviderId &&
                         DownloadManager.FindExistingDownload(_settings, book) is not null;
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

        // From the store, only books already in the library can be downloaded.
        if (provider is AudibleStoreProvider)
        {
            var notOwned = books.Where(b => b.InLibrary == false).ToList();
            if (notOwned.Count == books.Count)
            {
                SetStatus(string.Format(Strings.T("Store.NotOwned"), notOwned[0].Title));
                return;
            }
            books = [.. books.Where(b => b.InLibrary != false)];
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

        foreach (var book in books)
            _queue.Enqueue(provider, book);

        _queue.Start();
        SetStatus(string.Format(Strings.T("Main.AddedToQueue"), books.Count));
        queueList.Focus();
    }

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
        if (book.InLibrary is { } owned)
            lines.Add(Strings.T(owned ? "Store.InLibrary" : "Store.NotInLibrary"));
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
            lines.Add(Environment.NewLine + $"{Strings.T("Details.Description")}: {book.Description}");
        else if (!DisplayText.IsMissing(book.Info))
            lines.Add("" + Environment.NewLine + book.Info);

        MessageBox.Show(this, string.Join(Environment.NewLine, lines), Strings.T("Details.WindowTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
    // the Audible store
    // ------------------------------------------------------------------

    /// <summary>Opens the selected book's page in the Audible store. Ctrl + Shift + S.</summary>
    private void OpenStorePage()
    {
        if (!IsAudible(providerCombo.SelectedItem as IBookProvider) || libraryList.SelectedItem is not BookInfo book)
        {
            SetStatus(Strings.T("Store.ChooseAudible"));
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _audible.StorePageUrl(book.BookId),
            UseShellExecute = true,
        });
        SetStatus(string.Format(Strings.T("Store.Opened"), book.Title));
    }

    private async Task ChangeWishlistAsync(bool add)
    {
        if (!IsAudible(providerCombo.SelectedItem as IBookProvider) || libraryList.SelectedItem is not BookInfo book)
        {
            SetStatus(Strings.T("Store.ChooseAudible"));
            return;
        }

        if (add)
            await _audible.AddToWishlistAsync(book.BookId);
        else
            await _audible.RemoveFromWishlistAsync(book.BookId);

        SetStatus(string.Format(Strings.T(add ? "Store.WishAdded" : "Store.WishRemoved"), book.Title));

        // The wishlist on screen follows the change.
        if (providerCombo.SelectedItem is AudibleStoreProvider && string.IsNullOrWhiteSpace(searchBox.Text))
            await LoadBooksAsync();
    }

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

        if (ActiveControl == queueList && queueList.SelectedItem is QueueItem { Status: QueueStatus.Completed } done)
        {
            path = done.OutputFile;
            title = done.Book.Title;
        }
        else if (libraryList.SelectedItem is BookInfo book)
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

    /// <summary>Retry the selected failed or cancelled queue item. Ctrl + R, or the queue's menu.</summary>
    private void RetrySelectedQueueItem()
    {
        if (queueList.SelectedItem is not QueueItem item)
        {
            SetStatus(Strings.T("Main.SelectFailedFirst"));
            return;
        }
        if (item.Status is not (QueueStatus.Failed or QueueStatus.Cancelled))
        {
            SetStatus(Strings.T("Main.OnlyFailedRetry"));
            return;
        }
        _queue.Retry(item);
        SetStatus(string.Format(Strings.T("Main.Retrying"), item.Book.Title));
    }

    /// <summary>Remove the selected item that has not started. Ctrl + Delete, or the queue's menu.</summary>
    private void RemoveSelectedQueueItem()
    {
        if (queueList.SelectedItem is not QueueItem item)
        {
            SetStatus(Strings.T("Main.SelectPendingFirst"));
            return;
        }
        if (item.Status is not (QueueStatus.Pending or QueueStatus.Failed))
        {
            SetStatus(Strings.T("Main.OnlyPendingRemove"));
            return;
        }

        if (_settings.ConfirmBeforeQueueRemoval)
        {
            // MessageBox is announced automatically by NVDA; the wording makes clear
            // what will happen and that nothing is lost.
            var choice = MessageBox.Show(
                this,
                string.Format(Strings.T("Main.RemoveConfirm"), item.Book.Title),
                Strings.T("Main.RemoveConfirmTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (choice != DialogResult.Yes)
            {
                SetStatus(Strings.T("Main.RemovalCancelled"));
                return;
            }
        }

        _queue.Remove(item);
        SetStatus(string.Format(Strings.T("Main.Removed"), item.Book.Title));
    }

    /// <summary>Clear the finished rows. Ctrl + Shift + Delete, or the queue's menu.</summary>
    private void ClearFinishedQueueItems()
    {
        _queue.ClearFinished();
        SetStatus(Strings.T("Main.FinishedCleared"));
    }

    private void queueList_SelectedIndexChanged(object? sender, EventArgs e) =>
        UpdateQueueButtons();

    /// <summary>
    /// Enable Retry for failed/cancelled selections, Remove for pending ones.
    /// </summary>
    /// <remarks>
    /// This greys out the queue's <em>menu</em> entries, not buttons on the window:
    /// the actions live on the keyboard shortcuts and the queue's own menu, and a
    /// disabled item is how a menu says "not for this row" without a control that is
    /// there for no reason the rest of the time.
    /// </remarks>
    private void UpdateQueueButtons()
    {
        queueMenuRetry.Enabled = queueList.SelectedItem is QueueItem r &&
                                 r.Status is QueueStatus.Failed or QueueStatus.Cancelled;
        queueMenuRemove.Enabled = queueList.SelectedItem is QueueItem p &&
                                  p.Status is QueueStatus.Pending or QueueStatus.Failed;
        queueMenuClearFinished.Enabled =
            _queue.Items.Any(i => i.Status is QueueStatus.Completed or QueueStatus.Cancelled);
    }

    private void OnQueueChanged()
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            _queueBinding.DataSource = null;
            _queueBinding.DataSource = _queue.Items;
            _queueBinding.ResetBindings(false);

            cancelButton.Enabled = _queue.Items.Any(i => i.Status == QueueStatus.Active);
            UpdateQueueButtons();

            var active = _queue.Items.FirstOrDefault(i => i.Status == QueueStatus.Active);
            UpdateProgress(active);

            var done = _queue.Items.Count(i => i.Status == QueueStatus.Completed);
            var failed = _queue.Items.Count(i => i.Status == QueueStatus.Failed);
            var pending = _queue.Items.Count(i => i.Status == QueueStatus.Pending);

            statusLabel.Text = _queue.IsRunning
                ? string.Format(Strings.T("Main.QueueRunning"), done, failed)
                : string.Format(Strings.T("Main.QueueIdle"), done, failed, pending);
            if (_settings.AnnounceStatusChanges && !_queue.IsRunning)
                statusLabel.Announce();

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

        var percent = (int)Math.Round(active.Percent);
        progressBar.Value = Math.Clamp(percent, 0, 100);

        // The progress live region shows where the active download has got to.
        // Changing its text alone says nothing to a screen reader; only the
        // milestones below announce, so progress never chatters.
        progressStatus.Text = string.Format(
            Strings.T("Main.ProgressStatusText"), active.Book.Title, percent);

        if (!_settings.AnnounceProgress)
            return;

        var milestone = percent / 25 * 25;
        if (milestone >= 25 && milestone != _lastProgressMilestone && percent >= milestone)
        {
            _lastProgressMilestone = milestone;
            AnnounceProgress(string.Format(Strings.T("Main.ProgressAnnouncement"),
                active.Book.Title, milestone));
        }
        else if (_lastAnnounced?.Id != active.Id)
        {
            AnnounceProgress(string.Format(Strings.T("Main.ProgressAnnouncement"),
                active.Book.Title, 0));
        }
    }

    // ------------------------------------------------------------------
    // completion notification
    // ------------------------------------------------------------------

    /// <summary>
    /// A dismissible strip shown at the top of the window when every queued
    /// download has finished. Uses system Info colours and is fully announced.
    /// </summary>
    private void BuildNotificationPanel()
    {
        _notifyPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = SystemTheme.NoticeBackground,
            Visible = false,
            AccessibleName = Strings.T("Main.Notification"),
            AccessibleRole = AccessibleRole.Alert,
        };

        _notifyLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(10, 13),
            ForeColor = SystemTheme.NoticeText,
            AccessibleName = Strings.T("Main.NotificationMessage"),
            AccessibleRole = AccessibleRole.StaticText,
        };
        _notifyPanel.Controls.Add(_notifyLabel);

        _notifyDismiss = new Button
        {
            Text = Strings.T("Main.GotIt"),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(770, 10),
            Size = new Size(90, 28),
            AccessibleName = Strings.Plain(Strings.T("Main.GotIt")),
            AccessibleRole = AccessibleRole.PushButton,
        };
        _notifyDismiss.Click += (_, _) =>
        {
            _notifyPanel.Visible = false;
            queueList.Focus();
        };
        _notifyPanel.Controls.Add(_notifyDismiss);

        Controls.Add(_notifyPanel);
        // Docked below the menu bar: the menu is docked first, then this strip,
        // then the table fills whatever is left.
        Controls.SetChildIndex(_notifyPanel, 1);
        _notifyPanel.Visible = false;
    }

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

            _notifyLabel.Text = message;
            _notifyPanel.Visible = true;
            Log.Info(message);
            SetStatus(message);
            ScreenReader.RaiseAlert(_notifyPanel);

            if (_settings.ToastWhenFinished)
                ShowFinishToast(message);

            if (_settings.OpenFolderWhenFinished)
                OpenDownloadFolder();

            // Books that just finished now read "already downloaded" in the list.
            RefreshDownloadedMarks();
        });
    }

    /// <summary>
    /// Windows notification-center toast, in addition to the in-app strip.
    /// Skipped while the window itself has focus (the strip is already on screen).
    /// Falls back to a message box on systems without Action Center support.
    /// </summary>
    private void ShowFinishToast(string message)
    {
        if (ActiveForm == this)
            return;

        try
        {
            // No IsSupported() in 7.1.3 — on systems without Action Center
            // support this throws and the catch below shows a message box.
            new ToastContentBuilder()
                .AddText(Strings.T("App.Title"))
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Warn($"Toast notification unavailable ({ex.Message}); showing a message box instead.");
            MessageBox.Show(this, message, Strings.T("Main.AllFinishedTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var drmWasOn = _settings.DrmRemovalEnabled;

        using (var dialog = new SettingsForm(_settings, _audible, section))
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetStatus(string.Format(Strings.T("Main.SettingsSaved"), _settings.OutputFolder));
        }

        ApplyLocalization();
        ApplyAccessibilityPreferences();
        _settings.Save();

        if (previous is { CanSelect: true } && previous.Visible)
            previous.Focus();
        else
            providerCombo.Focus();

        await OnProviderChangedAsync();

        // Just switched on: offer to unlock the books that were saved with their DRM.
        if (!drmWasOn && _settings.DrmRemovalEnabled)
            RemoveDrmFromDownloads(askFirst: true);
    }

    /// <summary>
    /// Queues every encrypted Audible book in the download folder whose keys are
    /// known, to have its DRM removed. Nothing is downloaded again.
    /// </summary>
    private void RemoveDrmFromDownloads(bool askFirst)
    {
        if (!_settings.DrmRemovalEnabled)
        {
            SetStatus(Strings.T("Drm.TurnOnFirst"));
            return;
        }

        var files = DownloadManager.FindEncryptedDownloads(_settings)
            .Where(f => !_queue.Items.Any(i => i.Book.BookId == f && i.Status is QueueStatus.Pending or QueueStatus.Active))
            .ToList();

        if (files.Count == 0)
        {
            if (!askFirst)
                SetStatus(Strings.T("Drm.NoneLeft"));
            return;
        }

        var answer = MessageBox.Show(this, string.Format(Strings.T("Drm.ConvertExisting"), files.Count),
            Strings.T("Drm.ConvertTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        if (ProviderRegistry.Find(LocalFilesProvider.ProviderId) is not { } local)
            return;

        foreach (var file in files)
            _queue.Enqueue(local, new BookInfo(LocalFilesProvider.ProviderId, file,
                Path.GetFileNameWithoutExtension(file), "", "", 0, null, new Uri(file).AbsoluteUri,
                Path.GetExtension(file).ToUpperInvariant()));
        _queue.Start();

        SetStatus(string.Format(Strings.T("Drm.Queued"), files.Count));
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

    /// <summary>
    /// The decryption-key explanation, which used to be a button on the window and is
    /// now on the Help menu and on Ctrl + K.
    /// </summary>
    private void ShowDecryptionKeyHelp()
    {
        MessageBox.Show(this,
            Strings.T("Audible.KeysHelpText"),
            Strings.T("Main.Keys"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        [ShortcutAction.ToggleActivityLog] = form => form.ToggleActivityLog(),
        [ShortcutAction.GoToActivityLog] = form => form.ShowActivityLog(),
        [ShortcutAction.RetryQueueItem] = form => form.RetrySelectedQueueItem(),
        [ShortcutAction.RemoveQueueItem] = form => form.RemoveSelectedQueueItem(),
        [ShortcutAction.ClearFinishedItems] = form => form.ClearFinishedQueueItems(),
        [ShortcutAction.BookLanguage] = form =>
        {
            // the book-language list, when the current provider supports languages
            if (form.languageCombo.Visible)
                form.languageCombo.Focus();
        },
        [ShortcutAction.BookDetails] = form => form.ShowBookDetails(),
        [ShortcutAction.ShowBookFile] = form => form.ShowBookFile(),
        [ShortcutAction.StorePage] = form => form.OpenStorePage(),
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

    private void OnLogMessage(string line)
    {
        if (IsHandleCreated)
            BeginInvoke(() => logBox.AppendText(line + Environment.NewLine));
    }

    // ------------------------------------------------------------------
    // IKeyPrompt (for local AAX/AAXC files)
    // ------------------------------------------------------------------

    public CachedKeys? Prompt(string fileName, bool askKeyIv, bool askActivationBytes)
    {
        using var dialog = new KeyPromptForm(fileName, askKeyIv, askActivationBytes);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.ResultKeys : null;
    }
}
