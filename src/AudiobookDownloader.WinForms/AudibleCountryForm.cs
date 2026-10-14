using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The sign-in as it begins: choose the country, then sign in.
/// </summary>
/// <remarks>
/// The country comes first, and the Sign in button is not on screen until one has
/// been chosen, because the country decides which store the account, the library and
/// the prices come from — signing in and only then discovering the country is wrong
/// means signing in twice.
///
/// Nothing here sends the user into Settings: choosing a country *is* part of signing
/// in, and the same choice can be changed afterwards in Settings on its own, without
/// signing in again.
///
/// The country can be picked from the list or typed into it, which is how somebody
/// who knows the country they want avoids reading through eleven of them. Typing and
/// picking are the same choice by the time it arrives here: either way the store is
/// set and the sign-in comes on screen.
/// </remarks>
internal sealed class AudibleCountryForm : Form
{
    private readonly AppSettings _settings;
    private readonly AudibleProvider _audible;
    private readonly Action<string>? _onCountryChosen;

    private readonly CountryComboBox _country;
    private readonly Button _signIn;
    private readonly LiveRegionLabel _status;

    /// <summary>Two buttons, laid out left to right; kept here so both are named.</summary>
    private readonly Button _cancel;

    /// <summary>
    /// The country list, the sign-in button and the cancel button, for the self-test,
    /// which drives this dialog without a person to click it.
    /// </summary>
    internal CountryComboBox CountryList => _country;

    internal Button SignInButton => _signIn;

    internal Button CancelControl => _cancel;

    /// <summary>True once a country has been chosen, so the sign-in is on screen.</summary>
    internal bool SignInOffered { get; private set; }

    /// <summary>What the dialog is saying, for the self-test.</summary>
    internal string Status => _status.Text;

    public AudibleCountryForm(AppSettings settings, AudibleProvider audible, Action<string>? onCountryChosen = null)
    {
        _settings = settings;
        _audible = audible;
        _onCountryChosen = onCountryChosen;

        Text = Strings.T("Country.Title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 230);
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AccessibleName = Strings.T("Country.Title");
        AccessibleRole = AccessibleRole.Dialog;

        var instructions = new Label
        {
            Text = Strings.T("Country.Instructions"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
        };
        instructions.AccessibleName = Strings.T("Login.InstructionsAccessible");

        // The list holds country options, so it shows and announces the country's
        // name — "India" — and never the store code the sign-in is sent with. It is
        // also searchable: typing the first letters of a country's name finds it.
        _country = new CountryComboBox
        {
            Dock = DockStyle.Fill,
            TabIndex = 0,
            AccessibleName = Strings.T("Country.Label"),
        };

        // A country chosen on a previous run is shown as already chosen. One that
        // nobody has ever chosen leaves the list empty, so nothing on this dialog
        // suggests a country was picked for the user.
        _country.Fill(Countries.All(), settings.AudibleCountryChosen ? settings.AudibleLocale : null);

        _status = new LiveRegionLabel
        {
            Text = Strings.T("Country.Waiting"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            AccessibleName = Strings.T("Login.StatusAccessible"),
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Not on screen until a country has been chosen. This is the whole point of
        // the dialog, so it is a real visibility change and not a disabled button
        // left sitting there inviting a press.
        _signIn = new Button
        {
            Text = Strings.T("Country.SignIn"),
            AutoSize = true,
            TabIndex = 1,
            AccessibleName = Strings.T("Country.SignInAccessible"),
            Visible = false,
        };
        _signIn.Click += async (_, _) => await SignInAsync();

        _cancel = new Button
        {
            Text = Strings.T("Common.Cancel"),
            AutoSize = true,
            TabIndex = 2,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.T("Login.CancelAccessible"),
        };
        CancelButton = _cancel;

        var countryRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        countryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        countryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var countryLabel = new Label
        {
            Text = Strings.T("Country.Label"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
        };
        countryRow.Controls.Add(countryLabel, 0, 0);
        countryRow.Controls.Add(_country, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        buttons.Controls.Add(_signIn);
        buttons.Controls.Add(_cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(instructions, 0, 0);
        layout.Controls.Add(countryRow, 0, 1);
        layout.Controls.Add(_status, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        // A first-time user lands on the list itself, so Up and Down move through the
        // countries straight away without tabbing anywhere first.
        Shown += (_, _) => _country.Focus();

        // Attached last, so filling the list above is not mistaken for the user
        // choosing a country. Typing is not a second way of choosing: it selects the
        // country it found, so it arrives here through the same handler.
        _country.SelectedIndexChanged += (_, _) => CountryChanged();
        _country.NoMatch += typed =>
        {
            if (string.IsNullOrWhiteSpace(typed))
                ShowWaitingStatus();
            else
                SetStatus(Strings.T("Country.NoMatch", typed));
        };
        OfferSignIn();
    }

    /// <summary>
    /// Chooses a country, exactly as picking one from the list does. Used by the
    /// self-test, which drives this dialog without a person to click it.
    /// </summary>
    internal void Choose(string code)
    {
        for (var index = 0; index < _country.Items.Count; index++)
        {
            if (_country.Items[index] is CountryOption country && country.Code == code)
            {
                _country.SelectedIndex = index;
                return;
            }
        }
    }

    /// <summary>
    /// A country was picked: remember it, tell whoever asked to save it, and bring
    /// the sign-in on screen.
    /// </summary>
    private void CountryChanged()
    {
        if (_country.SelectedItem is not CountryOption country)
        {
            OfferSignIn();
            return;
        }

        // Set here and saved by the caller, so the sign-in that follows cannot use a
        // different store from the country on screen — and so the choice is kept even
        // if the dialog is closed without signing in.
        _settings.AudibleLocale = country.Code;
        _settings.AudibleCountryChosen = true;
        _onCountryChosen?.Invoke(country.Code);

        OfferSignIn();

        // Focus stays on the list. Moving it to the button here is what made the
        // arrow keys useless: the first Down arrow chose a country and sent focus
        // away, so the next one never reached the list. Enter signs in (the button
        // is the dialog's accept button) and Tab reaches it as usual.
        //
        // The status is updated silently: the screen reader already reads the
        // country the list moved to, and announcing it again talks over that.
        if (SignInOffered)
            _status.Text = Strings.T("Country.Chosen", country.Name);
    }

    /// <summary>
    /// Says again what the dialog says on its own: the country that is chosen, or the
    /// invitation to choose one. Typing that has been taken back again leaves the
    /// dialog as it was.
    /// </summary>
    private void ShowWaitingStatus() => SetStatus(_country.SelectedCountry is { } country
        ? Strings.T("Country.Chosen", country.Name)
        : Strings.T("Country.Waiting"));

    /// <summary>
    /// Puts the sign-in on screen only while a country is chosen, and makes Enter
    /// mean nothing at all until then.
    /// </summary>
    private void OfferSignIn()
    {
        SignInOffered = _country.SelectedItem is CountryOption;
        _signIn.Visible = SignInOffered;
        AcceptButton = SignInOffered ? _signIn : null;
    }

    /// <summary>
    /// Starts the sign-in for the country on screen. The sign-in window, the
    /// redirect and the session are exactly the ones the app already had; this only
    /// gives them a country to sign in to first.
    /// </summary>
    private async Task SignInAsync()
    {
        if (_country.SelectedItem is not CountryOption country)
            return;

        _signIn.Enabled = false;
        _country.Enabled = false;
        SetStatus(Strings.T("Audible.SigningIn"));

        try
        {
            await _audible.ConnectAsync();
            _settings.Save();

            SetStatus(Strings.T("Audible.SignedIn"));
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.T("Audible.SignInCancelled"));
        }
        catch (Exception ex)
        {
            SetStatus(Strings.T("Audible.SignInFailed", ex.Message));
            ScreenReader.RaiseAlert(_status);
            MessageBox.Show(this, ex.Message, Strings.T("Audible.SignInFailedTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            // A sign-in that did not finish leaves the country choice standing, so
            // the account holder can simply try again without re-choosing.
            _country.Enabled = true;
            _signIn.Enabled = true;
        }
    }

    private void SetStatus(string text)
    {
        if (IsDisposed || Disposing)
            return;

        _status.Text = text;
        _status.Announce();
    }
}
