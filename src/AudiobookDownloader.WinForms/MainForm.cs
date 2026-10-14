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
        _queue = new DownloadQueue(new DownloadManager(_settings), QueueStore.Default());
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

            await OnProviderChangedAsync();

            AnnounceUnfinishedDownloads(unfinished);
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
        ProviderRegistry.Register(new LibriVoxProvider(_settings));
        ProviderRegistry.Register(new InternetArchiveProvider());
        ProviderRegistry.Register(new GutenbergProvider(_settings));
        ProviderRegistry.Register(new LocalFilesProvider(_settings, this));
    }

    private void WireMenus()
    {
        menuSettings.Click += (_, _) => OpenSettings(SettingsForm.SectionGeneral);
        menuExit.Click += (_, _) => Close();
        menuAbout.Click += (_, _) => ShowAbout();
        menuShortcuts.Click += (_, _) => ShowKeyboardHelp();
        menuRefresh.Click += (_, _) => RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Reload"));
        menuOpenFolder.Click += (_, _) => OpenDownloadFolder();
        menuKeys.Click += (_, _) => ShowDecryptionKeyHelp();
        menuExport.Click += (_, _) => ExportBookList();

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
        _books = [];
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
        if (provider is AudibleProvider audible && !audible.IsConnected)
        {
            _books = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetProviderStatus(Strings.T("Audible.SignedOutShort"));
            SetStatus(Strings.T("Audible.SignedOutStatus"));
            return;
        }

        SetProviderStatus(provider.RequiresLogin && !provider.IsConnected
            ? Strings.T("Main.NotSignedIn")
            : provider.HasDrm
                ? Strings.T("Main.HasDrm")
                : Strings.T("Main.NoDrm"));

        // "Load my library automatically after signing in" off: wait for an explicit search.
        if (provider is AudibleProvider && !_settings.AudibleLoadLibraryAfterSignIn &&
            string.IsNullOrWhiteSpace(searchBox.Text))
        {
            _books = [];
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
    private void UpdateAccountButton(IBookProvider provider)
    {
        if (provider is not AudibleProvider audible)
        {
            accountButton.Visible = false;
            return;
        }

        accountButton.Visible = true;
        var key = audible.IsConnected ? "Main.SignOut" : "Main.SignIn";
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

        if (provider is AudibleProvider audible && !audible.IsConnected)
        {
            _books = [];
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

            _books = [.. found];
            if (!string.IsNullOrWhiteSpace(query))
            {
                _settings.AddSearch(provider.Id, query);
                RefreshSearchHistory();
            }
            RefreshBookList();

            SetLibraryHint(_books.Count == 0 ? Strings.T("Main.NoBooks") : "");
            var language = provider is ILanguageFilter { CurrentLanguage.Length: > 0 } &&
                           languageCombo.SelectedItem is LanguageOption { Code.Length: > 0 } chosen
                ? chosen.DisplayName
                : null;
            SetStatus(language is null
                ? string.Format(Strings.T("Main.BooksFound"), _books.Count)
                : string.Format(Strings.T("Main.BooksFoundIn"), _books.Count, language));
        }
        catch (NotSignedInException ex)
        {
            if (generation != _loadGeneration)
                return;
            _books = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetStatus(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation != _loadGeneration)
                return;
            _books = [];
            RefreshBookList();
            Log.Error($"Could not list books: {ex.Message}");
            SetLibraryHint(Strings.T("Main.ListFailed"));
            SetStatus(string.Format(Strings.T("Main.ListFailedStatus"), ex.Message));
        }
    }

    private void RefreshBookList()
    {
        _books = [.. _books.Select(MarkDownloaded)];
        _booksBinding.DataSource = _books;
        _booksBinding.ResetBindings(false);
        if (libraryList.Items.Count > 0)
            libraryList.SelectedIndex = 0;
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
        if (_books.Count == 0)
            return;

        var selected = libraryList.SelectedIndices.Cast<int>().ToList();
        var top = libraryList.TopIndex;

        _books = [.. _books.Select(MarkDownloaded)];
        _booksBinding.DataSource = _books;
        _booksBinding.ResetBindings(false);

        libraryList.ClearSelected();
        foreach (var index in selected.Where(i => i < libraryList.Items.Count))
            libraryList.SetSelected(index, true);
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

        if (provider is AudibleProvider audible && !audible.IsConnected)
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
        if (!DisplayText.IsMissing(book.Language))
            lines.Add($"{Strings.T("Details.Language")}: {book.Language}");
        if (ProviderRegistry.Find(book.ProviderId) is { } source)
            lines.Add($"{Strings.T("Details.Source")}: {source.DisplayName}");
        lines.Add($"{Strings.T("Details.Id")}: {book.BookId}");
        if (DownloadManager.FindExistingDownload(_settings, book) is { } file)
            lines.Add($"{Strings.T("Details.File")}: {file}");
        if (!DisplayText.IsMissing(book.Info))
            lines.Add("" + Environment.NewLine + book.Info);

        MessageBox.Show(this, string.Join(Environment.NewLine, lines), Strings.T("Details.WindowTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        if (item.Status is not QueueStatus.Pending)
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
                                  p.Status == QueueStatus.Pending;
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

        UpdateAccountButton(_audible);

        if (providerCombo.SelectedItem is AudibleProvider && !_audible.IsConnected)
        {
            _books = [];
            RefreshBookList();
            SetLibraryHint(Strings.T("Audible.SignedOutHint"));
            SetProviderStatus(Strings.T("Audible.SignedOutShort"));
            return;
        }

        // Signed in: carry straight on into the library, so the list is already
        // there when the Settings dialog closes.
        if (_audible.IsConnected && _settings.AudibleLoadLibraryAfterSignIn)
            RunSafely(LoadBooksAsync, Strings.T("Main.Tasks.Search"));
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
