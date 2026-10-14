using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The one place every option lives, in five tabs: General, Downloads, Audible,
/// Language and Accessibility. Each tab is built in its own file
/// (SettingsForm.General.cs and so on) out of named groups; this file holds the
/// dialog itself and what the tabs share.
/// </summary>
/// <remarks>
/// Built in code rather than with the designer so every control gets an
/// accessible name, role and tab position in one place. A live region at the foot
/// of the dialog echoes value changes for screen readers.
/// </remarks>
internal sealed partial class SettingsForm : Form
{
    public const int SectionGeneral = 0;
    public const int SectionDownloads = 1;
    public const int SectionAudible = 2;
    public const int SectionLanguage = 3;
    public const int SectionAccessibility = 4;

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
        _tabs.TabPages.Add(BuildLanguagePage());
        _tabs.TabPages.Add(BuildAccessibilityPage());

        _tabs.SelectedIndex = initialSection >= 0 && initialSection < _tabs.TabPages.Count
            ? initialSection
            : SectionGeneral;
        // The arrow keys on the section tabs only change the section: focus stays on
        // the tabs, so the next arrow press goes to the next section. It used to jump
        // into the page, and the next arrow then moved around inside it. Any other
        // change (Ctrl + Tab, a click) still takes focus to the new page's first control.
        var arrowOnTabs = false;
        _tabs.KeyDown += (_, e) =>
            arrowOnTabs = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End;
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (arrowOnTabs)
            {
                arrowOnTabs = false;
                return;
            }
            FocusFirstControlOnSelectedPage();
        };

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

    private static TabPage NewPage(string title) => new(title)
    {
        AccessibleName = title,
        AccessibleRole = AccessibleRole.PageTab,
        UseVisualStyleBackColor = true,
        Padding = new Padding(0),
        TabIndex = 0,
    };

    private static TextBox NewTextBox(string value, string accessibleName) => new()
    {
        Text = value,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        AccessibleName = accessibleName,
        AccessibleRole = AccessibleRole.Text,
    };

    private static ComboBox NewCombo(string[] items, string? selected, string accessibleName)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
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

    /// <summary>"Browse…" next to a folder box: picks a folder and puts it in the box.</summary>
    private Button BrowseButton(string name, string accessibleName, TextBox target, bool newFolder)
    {
        var button = new Button
        {
            Text = Strings.T("Common.Browse"),
            AutoSize = true,
            Name = name,
            AccessibleName = accessibleName,
            AccessibleRole = AccessibleRole.PushButton,
        };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = accessibleName,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = newFolder,
                SelectedPath = Directory.Exists(target.Text) ? target.Text : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                target.Text = dialog.SelectedPath;
        };
        return button;
    }
}
