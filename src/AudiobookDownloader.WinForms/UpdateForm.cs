using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// A newer version is out: what version, what is new in it (read line by line with
/// the arrows), and two buttons, Update and Cancel. Update downloads the new version
/// with a progress bar that takes focus, then closes the app; the new version is put
/// in place and started by itself.
/// </summary>
internal sealed class UpdateForm : Form
{
    private readonly UpdateInfo _update;
    private readonly Button _updateButton;
    private readonly Button _cancelButton;
    private readonly FocusableProgressBar _progress;
    private readonly LiveRegionLabel _status;
    private CancellationTokenSource? _download;

    /// <summary>Set when the new version has been started and the app should now close.</summary>
    public bool ReadyToClose { get; private set; }

    public UpdateForm(UpdateInfo update, Version current)
    {
        _update = update;

        Text = Strings.T("Update.Title");
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 460);
        MinimumSize = new Size(460, 360);
        AutoScaleMode = AutoScaleMode.Font;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Strings.ApplyDirection(this);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headline = new Label
        {
            Text = string.Format(Strings.T("Update.Available"), update.Latest.ToString(3), current.ToString(3)),
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            Margin = new Padding(3, 3, 3, 10),
            TabIndex = 0,
        };
        var notesLabel = new Label
        {
            Text = Strings.T("Update.WhatsNew"),
            AutoSize = true,
            UseMnemonic = true,
            TabIndex = 1,
        };
        var notes = new TextBox
        {
            Text = Updater.PlainNotes(update.Notes) is { Length: > 0 } text ? text : Strings.T("Update.NoNotes"),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = SystemTheme.ReadOnlyBackground,
            AccessibleName = Strings.Plain(Strings.T("Update.WhatsNew")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.Text,
            TabIndex = 2,
        };
        _progress = new FocusableProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 18,
            Visible = false,
            AccessibleName = Strings.T("Update.Progress"),
            AccessibleRole = AccessibleRole.ProgressBar,
            Margin = new Padding(3, 8, 3, 3),
            TabIndex = 3,
        };
        _status = new LiveRegionLabel
        {
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            AccessibleName = Strings.T("Update.Status"),
            AccessibleRole = AccessibleRole.StaticText,
            TabStop = false,
            TabIndex = 4,
        };

        _updateButton = new Button
        {
            Text = Strings.T("Update.Update"),
            AutoSize = true,
            AccessibleName = Strings.Plain(Strings.T("Update.Update")),
            TabIndex = 0,
        };
        _updateButton.Click += async (_, _) => await UpdateNowAsync();
        _cancelButton = new Button
        {
            Text = Strings.T("Common.Cancel"),
            AutoSize = true,
            AccessibleName = Strings.Plain(Strings.T("Common.Cancel")),
            TabIndex = 1,
        };
        _cancelButton.Click += (_, _) =>
        {
            if (_download is { } running)
                running.Cancel();
            else
                DialogResult = DialogResult.Cancel;
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 10, 0, 0),
            TabIndex = 5,
        };
        buttons.Controls.Add(_updateButton);
        buttons.Controls.Add(_cancelButton);

        layout.Controls.Add(headline, 0, 0);
        layout.Controls.Add(notesLabel, 0, 1);
        layout.Controls.Add(notes, 0, 2);
        layout.Controls.Add(_progress, 0, 3);
        layout.Controls.Add(_status, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        AcceptButton = _updateButton;
        CancelButton = _cancelButton;

        // The headline is read with the dialog; focus starts on Update, Tab reaches the notes.
        Shown += (_, _) => _updateButton.Focus();
        FormClosing += (_, e) =>
        {
            // Closing during the download cancels it rather than leaving it running.
            _download?.Cancel();
        };
    }

    private async Task UpdateNowAsync()
    {
        var installed = Updater.IsInstalled();
        if (Updater.FileFor(_update, installed) is not { } url)
        {
            // A release without its files: the release page is the way to get it.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _update.Url, UseShellExecute = true });
            DialogResult = DialogResult.Cancel;
            return;
        }

        _updateButton.Enabled = false;
        _progress.Value = 0;
        _progress.Visible = true;
        _progress.Focus();
        Say(Strings.T("Update.Downloading"));

        _download = new CancellationTokenSource();
        try
        {
            var lastSaid = 0;
            var progress = new Progress<int>(percent =>
            {
                _progress.Value = Math.Clamp(percent, 0, 100);
                if (percent / 25 * 25 is var step && step > lastSaid && step < 100)
                {
                    lastSaid = step;
                    Say(string.Format(Strings.T("Main.ProgressPercent"), step));
                }
            });
            var file = await Updater.DownloadAsync(url, progress, _download.Token);

            Say(Strings.T("Update.Installing"));
            Updater.Apply(file, installed);
            ReadyToClose = true;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            Say(Strings.T("Update.Cancelled"));
            Reset();
        }
        catch (Exception ex)
        {
            Log.Warn($"Update failed: {ex.Message}");
            Say(string.Format(Strings.T("Update.Failed"), ex.Message));
            Reset();
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }

    private void Reset()
    {
        _progress.Visible = false;
        _updateButton.Enabled = true;
        _updateButton.Focus();
    }

    private void Say(string message)
    {
        _status.Text = message;
        _status.Announce();
    }
}
