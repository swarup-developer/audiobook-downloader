using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The one place every option lives: application preferences, downloads, the
/// Audible account (sign in, sign out, status, region), Project Gutenberg,
/// interface language and accessibility.
/// </summary>
/// <remarks>
/// Built in code rather than with the designer so every control gets an
/// accessible name, role, tab position and description in one place. A live
/// region at the foot of the dialog echoes value changes for screen readers.
/// </remarks>
internal sealed class SettingsForm : Form
{
    public const int SectionGeneral = 0;
    public const int SectionDownloads = 1;
    public const int SectionAudible = 2;
    public const int SectionGutenberg = 3;
    public const int SectionLanguage = 4;
    public const int SectionAccessibility = 5;

    private readonly AppSettings _settings;
    private readonly AudibleProvider _audible;

    /// <summary>One action per control, run when the user presses OK.</summary>
    private readonly List<Action> _apply = [];

    private TabControl _tabs = null!;
    private LiveRegionLabel _statusLabel = null!;
    private LiveRegionLabel _audibleStatus = null!;
    private Button _signInButton = null!;
    private Button _signOutButton = null!;


    public SettingsForm(AppSettings settings, AudibleProvider audible, int initialSection = SectionGeneral)
    {
        _settings = settings;
        _audible = audible;

        // One signal for the whole app: the moment a live Audible session appears
        // or goes away, this screen re-reads it. Nothing here decides for itself
        // when the account state might have changed.
        _audible.ConnectionChanged += OnAudibleConnectionChanged;

        Text = Strings.T("Settings.Title");
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(760, 600);
        ClientSize = new Size(820, 660);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AccessibleName = Strings.T("Settings.Title");
        AccessibleRole = AccessibleRole.Dialog;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = Strings.T("Settings.Sections"),
            AccessibleRole = AccessibleRole.PageTabList,
            TabIndex = 0,
        };

        _tabs.TabPages.Add(BuildGeneralPage());
        _tabs.TabPages.Add(BuildDownloadsPage());
        _tabs.TabPages.Add(BuildAudiblePage());
        _tabs.TabPages.Add(BuildGutenbergPage());
        _tabs.TabPages.Add(BuildLanguagePage());
        _tabs.TabPages.Add(BuildAccessibilityPage());

        _tabs.SelectedIndex = initialSection >= 0 && initialSection < _tabs.TabPages.Count
            ? initialSection
            : SectionGeneral;
        _tabs.SelectedIndexChanged += (_, _) => FocusFirstControlOnSelectedPage();

        // Footer: live-region status on the left, buttons on the right.
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 10),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel = new LiveRegionLabel
        {
            Text = Strings.T("Settings.Ready"),
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Anchor = AnchorStyles.Left,
            AccessibleName = Strings.T("Settings.Status"),
            AccessibleRole = AccessibleRole.StaticText,
        };
        footer.Controls.Add(_statusLabel, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        var ok = new Button
        {
            Text = Strings.T("Common.OK"),
            AutoSize = true,
            AccessibleName = Strings.T("Settings.Save"),
            TabIndex = 40,
        };
        ok.Click += (_, _) =>
        {
            ApplyAll();
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(ok);

        var cancel = new Button
        {
            Text = Strings.T("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AccessibleName = Strings.T("Settings.Cancel"),
            TabIndex = 41,
        };
        buttons.Controls.Add(cancel);

        var apply = new Button
        {
            Text = Strings.T("Common.Apply"),
            AutoSize = true,
            AccessibleName = Strings.T("Settings.Apply"),
            TabIndex = 42,
        };
        apply.Click += (_, _) => ApplyAll();
        buttons.Controls.Add(apply);

        footer.Controls.Add(buttons, 1, 0);

        Controls.Add(_tabs);
        Controls.Add(footer);

        AcceptButton = ok;
        CancelButton = cancel;

        UpdateAudibleState(announce: false);

        Shown += (_, _) => FocusFirstControlOnSelectedPage();
    }

    // ==================================================================
    // General
    // ==================================================================

    private TabPage BuildGeneralPage()
    {
        var page = NewPage(Strings.T("Settings.General"), Strings.T("Settings.GeneralDescription"));
        var row = new SectionBuilder(page, Announce);

        var outputBox = NewTextBox(_settings.OutputFolder, "Download folder", Strings.T("Set.OutputFolderDescription"));
        row.AddLabeled(Strings.T("Set.OutputFolder"), outputBox);
        row.AddControl(BrowseButton("ChooseFolder", Strings.T("Set.ChooseFolder"), () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Strings.T("Set.ChooseFolder"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(outputBox.Text) ? outputBox.Text : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                outputBox.Text = dialog.SelectedPath;
        }));
        _apply.Add(() => _settings.OutputFolder = outputBox.Text.Trim());

        var localBox = NewTextBox(_settings.LocalFolder, "Folder with your AAX or AAXC files",
            Strings.T("Set.LocalFolderDescription"));
        row.AddLabeled(Strings.T("Set.LocalFolder"), localBox);
        row.AddControl(BrowseButton("ChooseLocalFolder", Strings.T("Set.ChooseLocalFolder"), () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Strings.T("Set.ChooseLocalFolder"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(localBox.Text) ? localBox.Text : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                localBox.Text = dialog.SelectedPath;
        }));
        _apply.Add(() => _settings.LocalFolder = localBox.Text.Trim());

        var open = row.AddCheck(Strings.T("Set.OpenWhenDone"), _settings.OpenFolderWhenFinished,
            "Open download folder when finished");
        _apply.Add(() => _settings.OpenFolderWhenFinished = open.Checked);

        var toast = row.AddCheck(Strings.T("Set.Toast"), _settings.ToastWhenFinished,
            "Windows notification when downloads finish");
        _apply.Add(() => _settings.ToastWhenFinished = toast.Checked);

        var confirm = row.AddCheck(Strings.T("Set.ConfirmRemove"), _settings.ConfirmBeforeQueueRemoval,
            "Confirm before removing a queue item");
        _apply.Add(() => _settings.ConfirmBeforeQueueRemoval = confirm.Checked);

        var remember = row.AddCheck(Strings.T("Set.RememberProvider"), _settings.RememberLastProvider,
            "Remember the last provider");
        _apply.Add(() => _settings.RememberLastProvider = remember.Checked);

        var updates = row.AddCheck(Strings.T("Set.CheckUpdates"), _settings.CheckForUpdates,
            "Check for updates when the app starts");
        _apply.Add(() => _settings.CheckForUpdates = updates.Checked);

        row.AddNote(string.Format(Strings.T("Set.DataFolder"), AppSettings.SettingsFolder));
        return page;
    }

    // ==================================================================
    // Downloads
    // ==================================================================

    private TabPage BuildDownloadsPage()
    {
        var page = NewPage(Strings.T("Settings.Downloads"), Strings.T("Settings.DownloadsDescription"));
        var row = new SectionBuilder(page, Announce);

        // DRM removal is the person's own decision: off until they tick it and accept
        // the personal-use notice, which is asked once.
        var drm = row.AddCheck(Strings.T("Set.RemoveDrm"), _settings.DrmRemovalEnabled,
            "Remove DRM from Audible books");
        var consented = _settings.DrmRemovalConsented;
        drm.CheckedChanged += (_, _) =>
        {
            if (!drm.Checked || consented)
                return;

            var answer = MessageBox.Show(this, Strings.T("Drm.Confirm"), Strings.T("Drm.ConfirmTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
                consented = true;
            else
                drm.Checked = false;
        };
        _apply.Add(() =>
        {
            _settings.RemoveDrm = drm.Checked;
            if (drm.Checked)
                _settings.DrmRemovalConsented = consented;
        });

        var delete = row.AddCheck(Strings.T("Set.DeleteIntermediate"), _settings.DeleteIntermediateFile,
            "Delete the encrypted original after decryption");
        _apply.Add(() => _settings.DeleteIntermediateFile = delete.Checked);

        var byAuthor = row.AddCheck(Strings.T("Set.OrganizeByAuthor"), _settings.OrganizeByAuthor,
            "Save each book in a folder named after its author");
        _apply.Add(() => _settings.OrganizeByAuthor = byAuthor.Checked);

        var duplicates = row.AddCheck(Strings.T("Set.WarnDuplicates"), _settings.WarnAboutDuplicates,
            "Ask before downloading a book that is already downloaded");
        _apply.Add(() => _settings.WarnAboutDuplicates = duplicates.Checked);

        // Audible output: lossless M4B or MP3, whole or one file per chapter.
        string[] formats = [Strings.T("Format.M4b"), Strings.T("Format.Mp3")];
        var format = NewCombo(formats,
            string.Equals(_settings.OutputFormat, "mp3", StringComparison.OrdinalIgnoreCase) ? formats[1] : formats[0],
            Strings.Plain(Strings.T("Set.OutputFormat")).TrimEnd(':'), "");
        row.AddLabeled(Strings.T("Set.OutputFormat"), format);
        _apply.Add(() => _settings.OutputFormat = format.SelectedIndex == 1 ? "mp3" : "m4b");

        var split = row.AddCheck(Strings.T("Set.SplitChapters"), _settings.SplitByChapter,
            "Save one file per chapter");
        _apply.Add(() => _settings.SplitByChapter = split.Checked);

        var embed = row.AddCheck(Strings.T("Set.EmbedInfo"), _settings.EmbedBookInfo,
            "Write the description, series and cover into the book file");
        _apply.Add(() => _settings.EmbedBookInfo = embed.Checked);

        var cover = row.AddCheck(Strings.T("Set.SaveCover"), _settings.SaveCoverImage,
            "Save the cover picture next to each book");
        _apply.Add(() => _settings.SaveCoverImage = cover.Checked);

        string[] parallel = ["1", "2", "3", "4"];
        // Backup: every finished book is copied here too. A OneDrive folder is
        // uploaded to the cloud by OneDrive itself.
        var backup = row.AddCheck(Strings.T("Set.Backup"), _settings.BackupEnabled,
            "Copy every finished book to a backup folder");
        var backupFolder = NewTextBox(
            string.IsNullOrWhiteSpace(_settings.BackupFolder) ? DownloadManager.SuggestedBackupFolder() ?? "" : _settings.BackupFolder,
            Strings.Plain(Strings.T("Set.BackupFolder")).TrimEnd(':'), "");
        row.AddLabeled(Strings.T("Set.BackupFolder"), backupFolder);
        row.AddControl(BrowseButton("ChooseBackupFolder", Strings.T("Set.ChooseBackupFolder"), () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Strings.T("Set.ChooseBackupFolder"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(backupFolder.Text) ? backupFolder.Text : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                backupFolder.Text = dialog.SelectedPath;
        }));
        _apply.Add(() =>
        {
            _settings.BackupEnabled = backup.Checked;
            _settings.BackupFolder = backupFolder.Text.Trim();
        });

        var parallelCombo = NewCombo(parallel, Math.Clamp(_settings.MaxParallelDownloads, 1, 4).ToString(),
            Strings.Plain(Strings.T("Set.Parallel")).TrimEnd(':'), "");
        row.AddLabeled(Strings.T("Set.Parallel"), parallelCombo);
        _apply.Add(() => _settings.MaxParallelDownloads = parallelCombo.SelectedIndex + 1);

        int[] limits = [0, 256, 512, 1024, 2048, 5120, 10240];
        string LimitText(int kb) => kb == 0 ? Strings.T("Speed.None")
            : kb >= 1024 ? $"{kb / 1024} MB/s" : $"{kb} KB/s";
        var current = limits.Contains(_settings.SpeedLimitKBps) ? _settings.SpeedLimitKBps : 0;
        var speedCombo = NewCombo([.. limits.Select(LimitText)], LimitText(current),
            Strings.Plain(Strings.T("Set.SpeedLimit")).TrimEnd(':'), "");
        row.AddLabeled(Strings.T("Set.SpeedLimit"), speedCombo);
        _apply.Add(() => _settings.SpeedLimitKBps = limits[Math.Max(0, speedCombo.SelectedIndex)]);

        row.AddNote(Strings.T("Downloads.Note"));
        return page;
    }

    // ==================================================================
    // Audible account
    // ==================================================================

    private TabPage BuildAudiblePage()
    {
        var page = NewPage(Strings.T("Settings.Audible"), Strings.T("Settings.AudibleDescription"));
        var row = new SectionBuilder(page, Announce);

        _audibleStatus = new LiveRegionLabel
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            AccessibleName = Strings.T("Set.AccountStatus"),
            AccessibleRole = AccessibleRole.StaticText,
        };
        row.AddControl(_audibleStatus);

        var countryCombo = NewCountryCombo();
        row.AddLabeled(Strings.T("Set.Country"), countryCombo);

        var accountButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true };

        _signInButton = new Button
        {
            Text = Strings.T("Set.SignIn"),
            AutoSize = true,
            AccessibleName = Strings.T("Set.SignInAccessible"),
        };
        _signInButton.Click += (_, _) => RunSafely(SignInAsync, Strings.T("Audible.SignInTask"));
        accountButtons.Controls.Add(_signInButton);

        _signOutButton = new Button
        {
            Text = Strings.T("Set.SignOut"),
            AutoSize = true,
            AccessibleName = Strings.T("Set.SignOutAccessible"),
        };
        _signOutButton.Click += (_, _) => SignOut();
        accountButtons.Controls.Add(_signOutButton);

        row.AddControl(accountButtons);

        var remember = row.AddCheck(Strings.T("Set.RememberSignIn"), _settings.AudibleRememberSignIn,
            "Remember the Audible sign-in on this computer");
        _apply.Add(() => _settings.AudibleRememberSignIn = remember.Checked);

        var load = row.AddCheck(Strings.T("Set.LoadLibrary"), _settings.AudibleLoadLibraryAfterSignIn,
            "Load the Audible library after signing in");
        _apply.Add(() => _settings.AudibleLoadLibraryAfterSignIn = load.Checked);

        // Several accounts: choose which one is in use, or add another.
        var accounts = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = Strings.Plain(Strings.T("Set.Accounts")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.ComboBox,
        };
        void FillAccounts()
        {
            accounts.Items.Clear();
            foreach (var account in _audible.Accounts)
                accounts.Items.Add(new AccountChoice(account));
            accounts.SelectedItem = accounts.Items.Cast<AccountChoice>()
                .FirstOrDefault(c => c.Account == _audible.ActiveAccount);
        }
        FillAccounts();
        row.AddLabeled(Strings.T("Set.Accounts"), accounts);
        accounts.SelectionChangeCommitted += (_, _) => RunSafely(async () =>
        {
            if (accounts.SelectedItem is not AccountChoice choice || choice.Account == _audible.ActiveAccount && _audible.IsConnected)
                return;
            SetAudibleStatus(Strings.T("Audible.SigningIn"));
            var ok = await _audible.SwitchAccountAsync(choice.Account.Id);
            SetAudibleStatus(ok ? string.Format(Strings.T("Set.SwitchedTo"), choice) : Strings.T("Audible.SignedOutSavedSignIn"));
            UpdateAudibleState(announce: false);
        }, Strings.T("Audible.SignInTask"));

        var addAccount = new Button
        {
            Text = Strings.T("Set.AddAccount"),
            AutoSize = true,
            AccessibleName = Strings.Plain(Strings.T("Set.AddAccount")).TrimEnd('…'),
        };
        addAccount.Click += (_, _) => RunSafely(async () =>
        {
            _audible.BeginNewAccount();
            using (var dialog = new AudibleCountryForm(_settings, _audible, _ => _settings.Save()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    await _audible.AbandonNewAccountAsync();
            }
            FillAccounts();
            UpdateAudibleState(announce: true);
        }, Strings.T("Audible.SignInTask"));
        row.AddControl(addAccount);

        var podcasts = row.AddCheck(Strings.T("Set.IncludePodcasts"), _settings.AudibleIncludePodcasts,
            "List podcast episodes from my library");
        _apply.Add(() => _settings.AudibleIncludePodcasts = podcasts.Checked);

        var auto = row.AddCheck(Strings.T("Set.AutoDownload"), _settings.AudibleAutoDownloadNew,
            "Download new purchases automatically when the app starts");
        _apply.Add(() =>
        {
            // Turning it on starts counting from now, so the whole library is not downloaded.
            if (auto.Checked && !_settings.AudibleAutoDownloadNew)
                _settings.AudibleLastSync = DateTimeOffset.UtcNow;
            _settings.AudibleAutoDownloadNew = auto.Checked;
        });

        row.AddNote(Strings.T("Audible.Note"));

        var keys = new Button
        {
            Text = Strings.T("Audible.KeysHelp"),
            AutoSize = true,
            AccessibleName = Strings.T("Audible.KeysHelp"),
        };
        keys.Click += (_, _) => MessageBox.Show(this, Strings.T("Audible.KeysHelpText"),
            Strings.T("Audible.KeysHelp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        row.AddControl(keys);

        return page;
    }

    private async Task SignInAsync()
    {
        // Already connected? Signing in again means switching accounts, which
        // starts by dropping the current session.
        if (_audible.IsConnected)
        {
            var answer = MessageBox.Show(this, Strings.T("Audible.SwitchConfirm"),
                Strings.T("Set.SwitchAccount"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;

            _audible.Disconnect();
            UpdateAudibleState(announce: false);
        }

        _signInButton.Enabled = false;
        SetAudibleStatus(Strings.T("Audible.SigningIn"));
        try
        {
            await _audible.ConnectAsync();
            _settings.Save();
            SetAudibleStatus(Strings.T("Audible.SignedIn"));
        }
        catch (OperationCanceledException)
        {
            SetAudibleStatus(Strings.T("Audible.SignInCancelled"));
        }
        catch (Exception ex)
        {
            SetAudibleStatus(string.Format(Strings.T("Audible.SignInFailed"), ex.Message));
            ScreenReader.RaiseAlert(_audibleStatus);
            MessageBox.Show(this, ex.Message, Strings.T("Audible.SignInFailedTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _signInButton.Enabled = true;
            UpdateAudibleState(announce: false);
        }
    }

    private void SignOut()
    {
        var answer = MessageBox.Show(this, Strings.T("Audible.SignOutConfirm"),
            Strings.T("Audible.SignOutTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        _audible.Disconnect();
        SetAudibleStatus(Strings.T("Audible.SignedOutAndForgotten"));
        UpdateAudibleState(announce: false);
    }

    /// <summary>
    /// Stop listening as the dialog closes: the provider outlives every dialog
    /// opened on it, and a listener left behind would hold this closed window
    /// alive and be told about sessions it can no longer show.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _audible.ConnectionChanged -= OnAudibleConnectionChanged;
        base.OnFormClosed(e);
    }

    /// <summary>
    /// A live Audible session appeared or went away, wherever it was changed from.
    /// The account controls are re-read and the change is announced, so this screen
    /// cannot go on saying the account is signed out over a session that exists.
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

        UpdateAudibleState(announce: true);
    }

    private void SetAudibleStatus(string text)
    {
        _audibleStatus.Text = text;
        Announce(text);
    }

    private void UpdateAudibleState(bool announce)
    {
        var signedIn = _audible.IsConnected;

        _signInButton.Text = signedIn ? Strings.T("Set.SwitchAccount") : Strings.T("Set.SignIn");

        // The spoken name moves with the text. Leaving it as "Sign in to Audible"
        // once the account is signed in tells a screen-reader user the opposite of
        // what is true, which is exactly what a signed-out account looks like.
        _signInButton.AccessibleName = signedIn
            ? Strings.T("Set.SwitchAccountAccessible")
            : Strings.T("Set.SignInAccessible");

        _signOutButton.Enabled = signedIn || _audible.HasSavedSignIn;

        var message = signedIn
            ? string.Format(Strings.T("Audible.SignedInRegion"), Countries.Name(_settings.AudibleLocale))
            : _audible.HasSavedSignIn
                ? Strings.T("Audible.SignedOutSavedSignIn")
                : Strings.T("Audible.SignedOut");

        _audibleStatus.Text = message;
        if (announce)
            Announce(message);
    }

    // ==================================================================
    // Project Gutenberg
    // ==================================================================

    private TabPage BuildGutenbergPage()
    {
        var page = NewPage(Strings.T("Settings.Gutenberg"), Strings.T("Settings.GutenbergDescription"));
        var row = new SectionBuilder(page, Announce);

        var languages = new List<string> { GutenbergProvider.AllLanguages.ToString() };
        languages.AddRange(GutenbergProvider.Languages.Select(l => l.ToString()));
        var currentLanguage = GutenbergProvider.Languages.FirstOrDefault(l => l.Code == _settings.GutenbergLanguage)?.ToString()
                              ?? GutenbergProvider.AllLanguages.ToString();

        var languageCombo = NewCombo([.. languages], currentLanguage,
            "Default language for Project Gutenberg searches", Strings.T("Set.DefaultLanguageDescription"));
        row.AddLabeled(Strings.T("Set.DefaultLanguage"), languageCombo);
        _apply.Add(() => _settings.GutenbergLanguage = LanguageCodeFrom(languageCombo.SelectedItem?.ToString()));

        string[] formats =
        [
            Strings.T("Format.Audio"),
            Strings.T("Format.Epub"),
            Strings.T("Format.Html"),
            Strings.T("Format.Text"),
        ];
        var formatCombo = NewCombo(formats, PreferredFormatText(_settings.GutenbergFormat),
            "Preferred Project Gutenberg format", Strings.T("Set.PreferredFormatDescription"));
        row.AddLabeled(Strings.T("Set.PreferredFormat"), formatCombo);
        _apply.Add(() => _settings.GutenbergFormat = PreferredFormatCode(formatCombo.SelectedItem?.ToString()));

        row.AddNote(Strings.T("Gutenberg.Note"));
        return page;
    }

    private static string LanguageCodeFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        return open >= 0 && close > open ? text[(open + 1)..close].Trim() : "";
    }

    private static string PreferredFormatText(string code) => code switch
    {
        "epub" => Strings.T("Format.Epub"),
        "html" => Strings.T("Format.Html"),
        "txt" => Strings.T("Format.Text"),
        _ => Strings.T("Format.Audio"),
    };

    private static string PreferredFormatCode(string? text)
    {
        if (text == Strings.T("Format.Epub")) return "epub";
        if (text == Strings.T("Format.Html")) return "html";
        if (text == Strings.T("Format.Text")) return "txt";
        return "audio";
    }

    // ==================================================================
    // Language
    // ==================================================================

    private TabPage BuildLanguagePage()
    {
        var page = NewPage(Strings.T("Settings.Language"), Strings.T("Settings.LanguageDescription"));
        var row = new SectionBuilder(page, Announce);

        var items = Strings.AvailableLanguages
            .Select(l => $"{l.DisplayName} ({l.Code})")
            .ToList();
        var current = items.FirstOrDefault(i => i.EndsWith($"({_settings.UiLanguage})", StringComparison.OrdinalIgnoreCase))
                      ?? items[0];

        var uiCombo = NewCombo([.. items], current, Strings.Plain(Strings.T("Set.UiLanguage")),
            Strings.T("Set.UiLanguageDescription"));
        row.AddLabeled(Strings.T("Set.UiLanguage"), uiCombo);
        _apply.Add(() =>
        {
            _settings.UiLanguage = LanguageCodeFrom(uiCombo.SelectedItem?.ToString());
            Strings.SetLanguage(_settings.UiLanguage);
        });

        row.AddNote(Strings.T("Language.Note"));
        return page;
    }

    // ==================================================================
    // Accessibility
    // ==================================================================

    private TabPage BuildAccessibilityPage()
    {
        var page = NewPage(Strings.T("Settings.Accessibility"), Strings.T("Settings.AccessibilityDescription"));
        var row = new SectionBuilder(page, Announce);

        var status = row.AddCheck(Strings.T("Set.AnnounceStatus"), _settings.AnnounceStatusChanges,
            "Announce status changes through the screen reader");
        _apply.Add(() => _settings.AnnounceStatusChanges = status.Checked);

        var progress = row.AddCheck(Strings.T("Set.AnnounceProgress"), _settings.AnnounceProgress,
            "Announce download progress milestones");
        _apply.Add(() => _settings.AnnounceProgress = progress.Checked);

        var chapters = row.AddCheck(Strings.T("Set.AnnounceChapters"), _settings.AnnounceChapters,
            "Say each chapter's name when the player reaches it");
        _apply.Add(() => _settings.AnnounceChapters = chapters.Checked);

        var log = row.AddCheck(Strings.T("Set.KeepLog"), _settings.KeepActivityLogVisible,
            "Keep the activity log visible");
        _apply.Add(() => _settings.KeepActivityLogVisible = log.Checked);

        row.AddNote(Strings.T("Set.AccessibilityNote"));
        return page;
    }

    // ==================================================================
    // Shared plumbing
    // ==================================================================

    /// <summary>
    /// Run work started by this dialog so an unexpected failure is reported and
    /// logged rather than closing the whole application.
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
            Log.Error($"Unexpected problem while {what}: {ex}");
            Announce(string.Format(Strings.T("Main.UnexpectedError"), what, ex.Message));

            if (IsDisposed || Disposing)
                return;

            MessageBox.Show(this,
                string.Format(Strings.T("Main.UnexpectedErrorDetail"), what, ex.Message, Log.FilePath),
                Strings.T("Main.UnexpectedErrorTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyAll()
    {
        foreach (var action in _apply)
            action();
        _settings.Save();
        Announce(Strings.T("Settings.Saved"));
    }

    /// <summary>Read the current text through the dialog's live region, when announcements are on.</summary>
    private void Announce(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !_settings.AnnounceStatusChanges)
            return;
        _statusLabel.Text = message;
        _statusLabel.Announce();
    }

    /// <summary>Move focus to the first useful control on the current tab when it is opened.</summary>
    private void FocusFirstControlOnSelectedPage()
    {
        if (_tabs.SelectedTab is not { } page)
            return;

        foreach (Control control in page.Controls.Cast<Control>().SelectMany(Descendants))
        {
            if (control.CanSelect && control.TabStop && control.Visible && control.Enabled)
            {
                control.Select();
                return;
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private TabPage NewPage(string title, string description) => new(title)
    {
        AccessibleName = title,
        AccessibleRole = AccessibleRole.PageTab,
        UseVisualStyleBackColor = true,
        Padding = new Padding(0),
        TabIndex = 0,
    };

    private TextBox NewTextBox(string value, string accessibleName, string description)
    {
        var box = new TextBox
        {
            Text = value,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = accessibleName,
            AccessibleRole = AccessibleRole.Text,
        };
        box.Enter += (_, _) => Announce(box.Text.Length == 0
            ? $"{accessibleName}: empty"
            : $"{accessibleName}: {box.Text}");
        return box;
    }

    /// <summary>
    /// The country selector.
    /// </summary>
    /// <remarks>
    /// It holds <see cref="CountryOption"/> objects rather than store codes, so the
    /// list shows and announces the country's name — "India" — instead of the code
    /// the sign-in is sent with, and it can be searched by typing the first letters
    /// of a country's name. The choice is saved the moment it is made, so it is kept
    /// even if the dialog is closed without pressing OK.
    /// </remarks>
    private CountryComboBox NewCountryCombo()
    {
        var combo = new CountryComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = Strings.T("Country.Label"),
        };

        combo.Fill(Countries.All(), _settings.AudibleLocale);

        // Settings always has a country to show. A saved value that names no store
        // falls back to the first one rather than leaving the list blank, where the
        // sign-in dialog would instead leave it empty on purpose.
        if (combo.SelectedCountry is null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;

        // Attached after the list is filled, so restoring the saved country is not
        // mistaken for the user choosing a new one.
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is not CountryOption country)
                return;

            _settings.AudibleLocale = country.Code;
            _settings.AudibleCountryChosen = true;
            _settings.Save();

            // Nothing is announced here: the screen reader already reads the country
            // the list moved to, and a second announcement on every arrow press
            // talks over it.
        };

        // Typing a name that is no country at all says so, rather than leaving the
        // person typing into a list that looks like it is ignoring them.
        combo.NoMatch += typed =>
        {
            if (!string.IsNullOrWhiteSpace(typed))
                Announce(Strings.T("Country.NoMatch", typed));
        };

        return combo;
    }

    /// <summary>An account as the account list shows it: "name@example.com (India)".</summary>
    private sealed record AccountChoice(AudibleAccount Account)
    {
        public override string ToString() =>
            $"{(string.IsNullOrWhiteSpace(Account.Name) ? Strings.T("Set.NewAccount") : Account.Name)} ({Countries.Name(Account.Locale)})";
    }

    private ComboBox NewCombo(string[] items, string? selected, string accessibleName, string description, bool editable = false)
    {
        var combo = new ComboBox
        {
            DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = accessibleName,
            AccessibleRole = AccessibleRole.ComboBox,
        };
        combo.Items.AddRange(items);
        combo.Text = selected ?? items.FirstOrDefault() ?? "";
        if (combo.SelectedIndex < 0 && items.Length > 0)
            combo.SelectedIndex = 0;
        // No announcement on change: the screen reader reads the new item itself.
        return combo;
    }

    private Button BrowseButton(string name, string accessibleName, Action onClick)
    {
        var button = new Button
        {
            Text = Strings.T("Common.Browse"),
            AutoSize = true,
            Name = name,
            AccessibleName = accessibleName,
            AccessibleRole = AccessibleRole.PushButton,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Lays a tab out as label/control rows in reading order, with sensible tab stops.</summary>
    private sealed class SectionBuilder
    {
        private readonly TableLayoutPanel _table;
        private readonly Action<string> _announce;
        private int _row;
        private int _tabIndex;

        public SectionBuilder(TabPage page, Action<string> announce)
        {
            _announce = announce;
            _table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 12),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.Controls.Add(_table);
        }

        public void AddLabeled(string labelText, Control control)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                UseMnemonic = true,
                TabStop = false,
                Margin = new Padding(6, 9, 10, 3),
                Anchor = AnchorStyles.Left,
            };
            _table.Controls.Add(label, 0, _row);

            control.Margin = new Padding(3, 6, 3, 6);
            control.TabIndex = _tabIndex + 1;
            if (control.AccessibleName is null or "")
                control.AccessibleName = labelText.Replace("&", "").TrimEnd(':');
            _table.Controls.Add(control, 1, _row);

            _row++;
            _tabIndex += 2;
        }

        public void AddControl(Control control, int tabIndex = -1)
        {
            control.Margin = new Padding(3, 6, 3, 6);
            control.TabIndex = tabIndex >= 0 ? tabIndex : _tabIndex++;
            _table.Controls.Add(control, 0, _row);
            _table.SetColumnSpan(control, 2);
            _row++;
        }

        public CheckBox AddCheck(string text, bool isChecked, string accessibleName)
        {
            var checkBox = new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                UseMnemonic = true,
                AccessibleName = accessibleName,
                AccessibleRole = AccessibleRole.CheckButton,
                Margin = new Padding(3, 8, 3, 3),
                TabIndex = _tabIndex + 1,
            };
            checkBox.CheckedChanged += (_, _) => _announce($"{accessibleName}: {(checkBox.Checked ? "checked" : "not checked")}");
            _table.Controls.Add(checkBox, 0, _row);
            _table.SetColumnSpan(checkBox, 2);
            _row++;
            _tabIndex += 2;
            return checkBox;
        }

        public void AddNote(string text)
        {
            var note = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                AccessibleName = text,
                AccessibleRole = AccessibleRole.StaticText,
                Margin = new Padding(3, 14, 3, 3),
                TabStop = false,
            };
            _table.Controls.Add(note, 0, _row);
            _table.SetColumnSpan(note, 2);
            _row++;
        }
    }
}
