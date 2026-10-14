namespace AudiobookDownloader.WinForms;

/// <summary>
/// The list of keyboard shortcuts: what F1 opens, and what the Help menu opens.
/// </summary>
/// <remarks>
/// Every row comes from somewhere real. The shortcuts come from the one table that
/// says which keys the application has (<see cref="Shortcuts"/>), so a shortcut that
/// is moved and a description that was not, cannot both be right. The Alt keys at the
/// end are read from the window's own labels while this dialog is built, which is how
/// the removed buttons stopped being described here.
///
/// A two-column list view, so a screen reader reads "keys, description" for every row;
/// the rows sit in named groups, so the list says which part of the application each
/// key belongs to rather than running the whole lot together.
/// </remarks>
internal sealed class KeyboardHelpForm : Form
{
    /// <param name="window">
    /// The window whose own labels are read for the Alt keys it offers.
    /// </param>
    public KeyboardHelpForm(Control window)
    {
        Text = Strings.T("Help.Title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 586);
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AccessibleName = Strings.T("Help.Title");
        AccessibleRole = AccessibleRole.Dialog;

        var intro = new Label
        {
            Text = Strings.T("Help.Intro"),
            Location = new Point(12, 10),
            Size = new Size(652, 72),
            AccessibleName = Strings.T("Help.Intro"),
            AccessibleRole = AccessibleRole.StaticText,
            TabStop = false,
        };
        Controls.Add(intro);

        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowGroups = true,
            Location = new Point(12, 88),
            Size = new Size(652, 440),
            TabIndex = 0,
            UseCompatibleStateImageBehavior = false,
            AccessibleName = Strings.T("Help.Label"),
            AccessibleRole = AccessibleRole.List,
        };
        list.Columns.Add(Strings.T("Help.Keys"), 200);
        list.Columns.Add(Strings.T("Help.What"), 440);

        // A group per part of the application, so the rows read in the order a person
        // meets them: what the whole application answers, what this window answers, and
        // what a dialog or a list answers. Nothing here is written down twice.
        var groups = new Dictionary<ShortcutOwner, ListViewGroup>
        {
            [ShortcutOwner.Menu] = new(Strings.T("Help.Group.Application")),
            [ShortcutOwner.Window] = new(Strings.T("Help.Group.Window")),
            [ShortcutOwner.Elsewhere] = new(Strings.T("Help.Group.Dialogs")),
        };

        foreach (var group in groups.Values)
            list.Groups.Add(group);

        foreach (var shortcut in Shortcuts.All)
        {
            list.Items.Add(new ListViewItem(
                [Shortcuts.Describe(shortcut), Strings.T(shortcut.DescriptionKey)])
            {
                Group = groups[shortcut.Owner],
            });
        }

        // The window's own Alt keys, read from its live labels: they are never listed
        // by hand, so this section cannot describe a control that has been removed.
        var windowKeys = new ListViewGroup(Strings.T("Help.Group.WindowKeys"));
        list.Groups.Add(windowKeys);

        foreach (var (keys, what) in Shortcuts.Mnemonics(window))
            list.Items.Add(new ListViewItem([keys, what]) { Group = windowKeys });

        Controls.Add(list);

        var close = new Button
        {
            Text = Strings.T("Help.Close"),
            DialogResult = DialogResult.Cancel,
            Location = new Point(572, 538),
            Size = new Size(92, 30),
            TabIndex = 1,
            AccessibleName = Strings.Plain(Strings.T("Help.Close")),
            AccessibleRole = AccessibleRole.PushButton,
        };
        Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;

        // The list is the first thing to read, so it takes focus when the dialog opens.
        Shown += (_, _) => list.Focus();
    }
}
