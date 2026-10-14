using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>Settings, Audible: the accounts, signing in and out, and what the library shows.</summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildAudiblePage()
    {
        var page = NewPage(Strings.T("Settings.Audible"));
        var section = new SettingsSection(page, Announce);

        section.Group(Strings.T("Group.Account"));
        _audibleStatus = new LiveRegionLabel
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            AccessibleName = Strings.T("Set.AccountStatus"),
            AccessibleRole = AccessibleRole.StaticText,
        };
        section.AddControl(_audibleStatus);

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
        section.AddLabeled(Strings.T("Set.Accounts"), accounts);
        accounts.SelectionChangeCommitted += (_, _) => RunSafely(async () =>
        {
            if (accounts.SelectedItem is not AccountChoice choice || choice.Account == _audible.ActiveAccount && _audible.IsConnected)
                return;
            SetAudibleStatus(Strings.T("Audible.SigningIn"));
            var ok = await _audible.SwitchAccountAsync(choice.Account.Id);
            SetAudibleStatus(ok ? string.Format(Strings.T("Set.SwitchedTo"), choice) : Strings.T("Audible.SignedOutSavedSignIn"));
            UpdateAudibleState(announce: false);
        }, Strings.T("Audible.SignInTask"));

        var countryCombo = NewCountryCombo();
        section.AddLabeled(Strings.T("Set.Country"), countryCombo);

        var accountButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true };

        _signInButton = new Button
        {
            Text = Strings.T("Set.SignIn"),
            AutoSize = true,
            AccessibleName = Strings.T("Set.SignInAccessible"),
            TabIndex = 0,
        };
        _signInButton.Click += (_, _) => RunSafely(SignInAsync, Strings.T("Audible.SignInTask"));
        accountButtons.Controls.Add(_signInButton);

        _signOutButton = new Button
        {
            Text = Strings.T("Set.SignOut"),
            AutoSize = true,
            AccessibleName = Strings.T("Set.SignOutAccessible"),
            TabIndex = 1,
        };
        _signOutButton.Click += (_, _) => SignOut();
        accountButtons.Controls.Add(_signOutButton);

        var addAccount = new Button
        {
            Text = Strings.T("Set.AddAccount"),
            AutoSize = true,
            AccessibleName = Strings.Plain(Strings.T("Set.AddAccount")).TrimEnd('…'),
            TabIndex = 2,
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
        accountButtons.Controls.Add(addAccount);

        section.AddControl(accountButtons);

        var remember = section.AddCheck(Strings.T("Set.RememberSignIn"), _settings.AudibleRememberSignIn,
            "Remember the Audible sign-in on this computer");
        _apply.Add(() => _settings.AudibleRememberSignIn = remember.Checked);

        section.Group(Strings.T("Group.Library"));
        var load = section.AddCheck(Strings.T("Set.LoadLibrary"), _settings.AudibleLoadLibraryAfterSignIn,
            "Load the Audible library after signing in");
        _apply.Add(() => _settings.AudibleLoadLibraryAfterSignIn = load.Checked);

        var podcasts = section.AddCheck(Strings.T("Set.IncludePodcasts"), _settings.AudibleIncludePodcasts,
            "List podcast episodes from my library");
        _apply.Add(() => _settings.AudibleIncludePodcasts = podcasts.Checked);

        var auto = section.AddCheck(Strings.T("Set.AutoDownload"), _settings.AudibleAutoDownloadNew,
            "Download new purchases automatically when the app starts");
        _apply.Add(() =>
        {
            // Turning it on starts counting from now, so the whole library is not downloaded.
            if (auto.Checked && !_settings.AudibleAutoDownloadNew)
                _settings.AudibleLastSync = DateTimeOffset.UtcNow;
            _settings.AudibleAutoDownloadNew = auto.Checked;
        });

        section.AddNote(Strings.T("Audible.Note"));
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
}
