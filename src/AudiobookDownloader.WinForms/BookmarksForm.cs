using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// A one-line question: a label, a text box, OK and Cancel. Returns the text, or
/// null when cancelled.
/// </summary>
internal sealed class TextPrompt : Form
{
    private readonly TextBox _text;

    internal TextPrompt(string title, string label, string value)
    {
        Text = title;
        AccessibleName = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var caption = new Label { Text = label, AutoSize = true, MaximumSize = new Size(440, 0) };
        _text = new TextBox
        {
            Text = value,
            Width = 440,
            TabIndex = 0,
            AccessibleName = Strings.Plain(label).TrimEnd(':'),
        };

        var ok = new Button { Text = Strings.T("Common.OK"), AutoSize = true, TabIndex = 1, DialogResult = DialogResult.OK };
        ok.AccessibleName = Strings.Plain(ok.Text);
        var cancel = new Button { Text = Strings.T("Common.Cancel"), AutoSize = true, TabIndex = 2, DialogResult = DialogResult.Cancel };
        cancel.AccessibleName = Strings.Plain(cancel.Text);
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([ok, cancel]);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        layout.Controls.Add(caption);
        layout.Controls.Add(_text);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        ActiveControl = _text;
    }

    public static string? Ask(IWin32Window owner, string title, string label, string value)
    {
        using var prompt = new TextPrompt(title, label, value);
        return prompt.ShowDialog(owner) == DialogResult.OK ? prompt._text.Text : null;
    }
}

/// <summary>
/// The bookmarks of one book: Enter (or Go to) jumps to the one selected, Delete
/// removes it. The list reads "12:30, chapter 3: the note".
/// </summary>
internal sealed class BookmarksForm : Form
{
    private readonly List<Bookmark> _bookmarks;
    private readonly IReadOnlyList<string>? _tracks;
    private readonly ListBox _list;

    /// <summary>The bookmark to go to, once the dialog closes with OK.</summary>
    public Bookmark? Chosen { get; private set; }

    public BookmarksForm(List<Bookmark> bookmarks, IReadOnlyList<string>? tracks)
    {
        _bookmarks = bookmarks;
        _tracks = tracks;

        Text = Strings.T("Bookmark.ListTitle");
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _list = new ListBox
        {
            Width = 520,
            Height = 220,
            TabIndex = 0,
            AccessibleName = Strings.T("Bookmark.ListTitle"),
            AccessibleRole = AccessibleRole.List,
        };
        _list.DoubleClick += (_, _) => GoTo();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                DeleteSelected();
            }
        };

        var go = new Button { Text = Strings.T("Bookmark.Go"), AutoSize = true, TabIndex = 1 };
        go.AccessibleName = Strings.Plain(go.Text);
        go.Click += (_, _) => GoTo();
        var delete = new Button { Text = Strings.T("Bookmark.Delete"), AutoSize = true, TabIndex = 2 };
        delete.AccessibleName = Strings.Plain(delete.Text);
        delete.Click += (_, _) => DeleteSelected();
        var close = new Button { Text = Strings.T("Common.Close"), AutoSize = true, TabIndex = 3, DialogResult = DialogResult.Cancel };
        close.AccessibleName = Strings.Plain(close.Text);
        AcceptButton = go;
        CancelButton = close;

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([go, delete, close]);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        layout.Controls.Add(_list);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Fill();
        ActiveControl = _list;
    }

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var mark in _bookmarks)
            _list.Items.Add(Describe(mark));
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
    }

    private string Describe(Bookmark mark)
    {
        var time = PlayerForm.Clock(TimeSpan.FromSeconds(mark.Seconds));
        var where = _tracks is { Count: > 1 } ? $"{time}, {string.Format(Strings.T("Bookmark.InChapter"), mark.Track + 1)}" : time;
        return string.IsNullOrWhiteSpace(mark.Note) ? where : $"{where}: {mark.Note}";
    }

    private void GoTo()
    {
        if (_list.SelectedIndex < 0)
            return;

        Chosen = _bookmarks[_list.SelectedIndex];
        DialogResult = DialogResult.OK;
    }

    private void DeleteSelected()
    {
        var index = _list.SelectedIndex;
        if (index < 0)
            return;

        _bookmarks.RemoveAt(index);
        Fill();
        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
    }
}
