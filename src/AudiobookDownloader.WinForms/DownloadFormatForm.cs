namespace AudiobookDownloader.WinForms;

/// <summary>
/// Asked when Download is pressed: which format to save the book in. Focus starts on
/// the format list; Enter (or Download now) starts the download, Escape cancels it.
/// The list holds only formats the chosen source can actually deliver.
/// </summary>
internal sealed class DownloadFormatForm : Form
{
    private readonly ComboBox _formats;
    private readonly IReadOnlyList<(string Code, string Text)> _options;

    public DownloadFormatForm(string what, IReadOnlyList<(string Code, string Text)> options, string? preselect)
    {
        _options = options;

        Text = string.Format(Strings.T("DownloadFormat.Title"), what);
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Font;
        Padding = new Padding(12);
        Strings.ApplyDirection(this);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = Strings.T("DownloadFormat.Label"),
            AutoSize = true,
            UseMnemonic = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
            TabIndex = 0,
        };
        _formats = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 380,
            AccessibleName = Strings.Plain(Strings.T("DownloadFormat.Label")).TrimEnd(':'),
            AccessibleRole = AccessibleRole.ComboBox,
            Margin = new Padding(3, 4, 3, 3),
            TabIndex = 1,
        };
        foreach (var (_, text) in options)
            _formats.Items.Add(text);
        var start = options.ToList().FindIndex(o => string.Equals(o.Code, preselect, StringComparison.OrdinalIgnoreCase));
        _formats.SelectedIndex = Math.Max(0, start);

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_formats, 1, 0);

        var now = new Button
        {
            Text = Strings.T("DownloadFormat.Now"),
            AutoSize = true,
            DialogResult = DialogResult.OK,
            AccessibleName = Strings.Plain(Strings.T("DownloadFormat.Now")),
            TabIndex = 0,
        };
        var cancel = new Button
        {
            Text = Strings.T("Common.Cancel"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.Plain(Strings.T("Common.Cancel")),
            TabIndex = 1,
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0),
            TabIndex = 2,
        };
        buttons.Controls.Add(now);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 1);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = now;
        CancelButton = cancel;

        Shown += (_, _) => _formats.Focus();
    }

    /// <summary>The code of the chosen format ("m4b", "mp3-chapters", "epub", …).</summary>
    public string ChosenCode => _options[Math.Max(0, _formats.SelectedIndex)].Code;
}
