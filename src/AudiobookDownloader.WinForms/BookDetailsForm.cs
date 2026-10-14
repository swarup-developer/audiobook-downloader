namespace AudiobookDownloader.WinForms;

/// <summary>
/// A book's details, one fact per line, in a read-only text box: Up and Down read
/// them line by line, Ctrl + Home goes back to the top, and the text can be selected
/// and copied. Escape or Close shuts it.
/// </summary>
internal sealed class BookDetailsForm : Form
{
    private readonly TextBox _text;

    public BookDetailsForm(string title, IEnumerable<string> lines)
    {
        Text = $"{Strings.T("Details.WindowTitle")}: {title}";
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 440);
        MinimumSize = new Size(420, 300);
        AutoScaleMode = AutoScaleMode.Font;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Strings.ApplyDirection(this);

        _text = new TextBox
        {
            Text = string.Join(Environment.NewLine, lines),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = SystemTheme.ReadOnlyBackground,
            AccessibleName = Strings.T("Details.WindowTitle"),
            AccessibleRole = AccessibleRole.Text,
            TabIndex = 0,
        };

        var close = new Button
        {
            Text = Strings.T("Common.Close"),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.Plain(Strings.T("Common.Close")),
            TabIndex = 1,
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            TabIndex = 1,
        };
        footer.Controls.Add(close);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0), TabIndex = 0 };
        body.Controls.Add(_text);

        Controls.Add(body);
        Controls.Add(footer);
        CancelButton = close;

        // Start at the first line, not with everything selected.
        Shown += (_, _) =>
        {
            _text.Focus();
            _text.Select(0, 0);
        };
    }

    /// <summary>For the self-test: the text as shown.</summary>
    internal string DetailsText => _text.Text;
}
