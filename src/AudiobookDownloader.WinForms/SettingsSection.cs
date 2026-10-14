namespace AudiobookDownloader.WinForms;

/// <summary>
/// Lays out one Settings tab: named groups ("Files and folders", "Backup", …), each
/// holding label and control rows in reading order, with tab stops that follow that
/// order. A screen reader names the group as focus enters it, so a long tab reads as
/// a few short, labelled parts rather than one long list.
/// </summary>
internal sealed class SettingsSection
{
    private readonly TableLayoutPanel _page;
    private readonly Action<string> _announce;
    private int _pageRow;
    private int _pageTabIndex;

    /// <summary>The table rows are being added to: the current group's, or the page's.</summary>
    private TableLayoutPanel _table;
    private int _row;
    private int _tabIndex;

    public SettingsSection(TabPage page, Action<string> announce)
    {
        _announce = announce;
        _page = NewTable();
        _page.Dock = DockStyle.Fill;
        _page.AutoSize = false;
        _page.AutoScroll = true;
        _page.Padding = new Padding(12, 10, 12, 10);
        page.Controls.Add(_page);
        _table = _page;
    }

    /// <summary>Starts a named group; everything added after it goes inside, until the next.</summary>
    public void Group(string title)
    {
        var box = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
            Margin = new Padding(3, 4, 3, 8),
            TabIndex = _pageTabIndex++,
            AccessibleName = Strings.Plain(title),
            AccessibleRole = AccessibleRole.Grouping,
        };

        var inner = NewTable();
        inner.Dock = DockStyle.Top;
        box.Controls.Add(inner);

        _page.Controls.Add(box, 0, _pageRow++);
        _page.SetColumnSpan(box, 2);

        _table = inner;
        _row = 0;
        _tabIndex = 0;
    }

    public void AddLabeled(string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            UseMnemonic = true,
            TabStop = false,
            TabIndex = _tabIndex,
            Margin = new Padding(3, 9, 10, 3),
            Anchor = AnchorStyles.Left,
        };
        _table.Controls.Add(label, 0, _row);

        control.Margin = new Padding(3, 6, 3, 6);
        control.TabIndex = _tabIndex + 1;
        if (control.AccessibleName is null or "")
            control.AccessibleName = Strings.Plain(labelText).TrimEnd(':');
        _table.Controls.Add(control, 1, _row);

        NextRow(2);
    }

    public void AddControl(Control control)
    {
        control.Margin = new Padding(3, 6, 3, 6);
        control.TabIndex = _tabIndex;
        _table.Controls.Add(control, 0, _row);
        _table.SetColumnSpan(control, 2);
        NextRow(1);
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
            Margin = new Padding(3, 6, 3, 3),
        };
        checkBox.CheckedChanged += (_, _) => _announce($"{accessibleName}: {(checkBox.Checked ? "checked" : "not checked")}");
        AddControl(checkBox);
        return checkBox;
    }

    /// <summary>An explanation at the foot of the tab, outside every group.</summary>
    public void AddNote(string text)
    {
        var note = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            AccessibleName = text,
            AccessibleRole = AccessibleRole.StaticText,
            Margin = new Padding(6, 10, 3, 3),
            TabStop = false,
            TabIndex = _pageTabIndex++,
        };
        _page.Controls.Add(note, 0, _pageRow++);
        _page.SetColumnSpan(note, 2);
        _table = _page;
        _row = _pageRow;
        _tabIndex = _pageTabIndex;
    }

    private void NextRow(int tabStops)
    {
        _row++;
        _tabIndex += tabStops;
        if (_table == _page)
        {
            _pageRow = _row;
            _pageTabIndex = _tabIndex;
        }
    }

    private static TableLayoutPanel NewTable()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return table;
    }
}
