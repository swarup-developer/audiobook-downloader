namespace AudiobookDownloader.WinForms;

/// <summary>Who answers a shortcut when it is pressed.</summary>
internal enum ShortcutOwner
{
    /// <summary>The main window answers it, in its own key handling.</summary>
    Window,

    /// <summary>The application menu answers it, through the item's own shortcut key.</summary>
    Menu,

    /// <summary>
    /// Something else answers it — a dialog, or Windows itself. Listed so the help
    /// stays complete; the main window never dispatches it.
    /// </summary>
    Elsewhere,
}

/// <summary>What a shortcut does.</summary>
/// <remarks>
/// One value per action, so that "is this shortcut really answered?" is a question
/// with an answer: the window keeps a list of the actions it answers, and the offline
/// checks compare the two. A shortcut added here without something to do, or an action
/// dropped without being removed from here, is caught rather than left to lie in the
/// help screen.
/// </remarks>
internal enum ShortcutAction
{
    ShortcutList,
    Settings,
    ReloadBooks,
    Quit,
    DecryptionKeys,
    OpenDownloadFolder,
    ToggleActivityLog,
    GoToActivityLog,
    RetryQueueItem,
    RemoveQueueItem,
    ClearFinishedItems,
    SearchHistory,
    BookLanguage,
    BookDetails,
    ExportBookList,
    ShowBookFile,

    // Answered somewhere the main window does not reach.
    MoveFocus,
    ApplicationMenu,
    NextSettingsSection,
    PreviousSettingsSection,
    CloseDialog,
    ConfirmSearch,
    SelectSeveral,
    SelectAllBooks,
    OpenBook,
}

/// <summary>One keyboard shortcut: the keys, what it does, and who answers it.</summary>
/// <param name="KeysLabel">
/// How the keys read, when the keys are not a combination this program can express —
/// "Shift + arrows, or Ctrl + click" is one gesture a person makes and not a key with
/// modifiers. Null means the keys above are read out, which is the usual case.
/// </param>
internal sealed record Shortcut(
    ShortcutAction Action,
    Keys Keys,
    string DescriptionKey,
    ShortcutOwner Owner,
    string? KeysLabel = null);

/// <summary>
/// Every keyboard shortcut the application has.
/// </summary>
/// <remarks>
/// This is the one place a key is decided. The help screen is built from this table,
/// the main window answers the window-owned entries by looking them up here, and the
/// offline checks compare the table against the real menu and against the letters the
/// window itself offers — so a key cannot be moved, and an action cannot be taken off
/// the window, without the documentation following it.
///
/// The application has no user-remappable keys, so there is nothing else for this
/// table to respect; if bindings ever become something the user can change, this is
/// the place to read them from.
/// </remarks>
internal static class Shortcuts
{
    /// <summary>Every shortcut, in the order the help lists them.</summary>
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        // Held by the application menu, which answers them itself.
        new(ShortcutAction.ShortcutList, Keys.F1, "Shortcut.ShortcutList", ShortcutOwner.Menu),
        new(ShortcutAction.Settings, Keys.Control | Keys.Oemcomma, "Shortcut.Settings", ShortcutOwner.Menu),
        new(ShortcutAction.ReloadBooks, Keys.F5, "Shortcut.ReloadBooks", ShortcutOwner.Menu),
        new(ShortcutAction.DecryptionKeys, Keys.Control | Keys.K, "Shortcut.Keys", ShortcutOwner.Menu),
        new(ShortcutAction.OpenDownloadFolder, Keys.Control | Keys.O, "Shortcut.Folder", ShortcutOwner.Menu),
        new(ShortcutAction.ExportBookList, Keys.Control | Keys.E, "Shortcut.Export", ShortcutOwner.Menu),
        new(ShortcutAction.Quit, Keys.Alt | Keys.F4, "Shortcut.Quit", ShortcutOwner.Menu),

        // Answered by the main window's own key handling.
        new(ShortcutAction.Quit, Keys.Control | Keys.Q, "Shortcut.Quit", ShortcutOwner.Window),
        new(ShortcutAction.ToggleActivityLog, Keys.Control | Keys.L, "Shortcut.Log", ShortcutOwner.Window),
        new(ShortcutAction.GoToActivityLog, Keys.Alt | Keys.L, "Shortcut.GoToLog", ShortcutOwner.Window),
        new(ShortcutAction.RetryQueueItem, Keys.Control | Keys.R, "Shortcut.Retry", ShortcutOwner.Window),
        new(ShortcutAction.RemoveQueueItem, Keys.Control | Keys.Delete, "Shortcut.Remove", ShortcutOwner.Window),
        new(ShortcutAction.ClearFinishedItems, Keys.Control | Keys.Shift | Keys.Delete, "Shortcut.ClearFinished", ShortcutOwner.Window),
        new(ShortcutAction.SearchHistory, Keys.Alt | Keys.Down, "Shortcut.SearchHistory", ShortcutOwner.Window),
        new(ShortcutAction.BookLanguage, Keys.Alt | Keys.U, "Shortcut.BookLanguage", ShortcutOwner.Window),
        new(ShortcutAction.BookDetails, Keys.Control | Keys.I, "Shortcut.BookDetails", ShortcutOwner.Window),
        new(ShortcutAction.ShowBookFile, Keys.Control | Keys.Shift | Keys.O, "Shortcut.ShowFile", ShortcutOwner.Window),

        // Answered by a dialog, by Windows, or by a list: documented, never dispatched
        // here. The list entries are the ones that are a gesture rather than a key, so
        // they carry their own label.
        new(ShortcutAction.MoveFocus, Keys.Tab, "Shortcut.MoveFocus", ShortcutOwner.Elsewhere),
        new(ShortcutAction.ApplicationMenu, Keys.Alt, "Shortcut.AppMenu", ShortcutOwner.Elsewhere),
        new(ShortcutAction.ConfirmSearch, Keys.None, "Shortcut.SearchBoxEnter", ShortcutOwner.Elsewhere,
            "Enter"),
        new(ShortcutAction.SelectSeveral, Keys.None, "Shortcut.SelectSeveral", ShortcutOwner.Elsewhere,
            "Shift + arrows, or Ctrl + click"),
        new(ShortcutAction.SelectAllBooks, Keys.None, "Shortcut.SelectAll", ShortcutOwner.Elsewhere,
            "Ctrl + A in the book list"),
        new(ShortcutAction.OpenBook, Keys.None, "Shortcut.OpenBook", ShortcutOwner.Elsewhere,
            "Enter in the book list"),
        new(ShortcutAction.NextSettingsSection, Keys.Control | Keys.Tab, "Shortcut.NextSection", ShortcutOwner.Elsewhere),
        new(ShortcutAction.PreviousSettingsSection, Keys.Control | Keys.Shift | Keys.Tab, "Shortcut.PreviousSection", ShortcutOwner.Elsewhere),
        new(ShortcutAction.CloseDialog, Keys.Escape, "Shortcut.CloseDialog", ShortcutOwner.Elsewhere),
    ];

    /// <summary>
    /// The keys as the help shows them: the shortcut's own label when it has one, and
    /// the keys read out otherwise.
    /// </summary>
    public static string Describe(Shortcut shortcut) =>
        shortcut.KeysLabel ?? Describe(shortcut.Keys);

    /// <summary>
    /// The shortcut the main window answers for exactly these keys, or null. The main
    /// window asks this and nobody else: the menu and the dialogs answer their own
    /// keys before the window ever sees them.
    /// </summary>
    public static Shortcut? FindForWindow(Keys keys) =>
        All.FirstOrDefault(shortcut => shortcut.Owner == ShortcutOwner.Window && shortcut.Keys == keys);

    /// <summary>
    /// The keys as a person reads them: "Ctrl + K". Key names are left alone — Ctrl,
    /// Alt and Delete are the same words on every keyboard, and translating them would
    /// make the help harder to follow, not easier.
    /// </summary>
    public static string Describe(Keys keys)
    {
        var parts = new List<string>();

        if ((keys & Keys.Control) == Keys.Control)
            parts.Add("Ctrl");

        if ((keys & Keys.Alt) == Keys.Alt)
            parts.Add("Alt");

        if ((keys & Keys.Shift) == Keys.Shift)
            parts.Add("Shift");

        // Alt on its own has no key of its own: it opens the menu, and reading it
        // as "Alt + None" would be nonsense.
        if ((keys & Keys.KeyCode) != Keys.None)
            parts.Add((keys & Keys.KeyCode) switch
            {
                Keys.Oemcomma => ",",
                Keys.Delete => "Delete",
                Keys.Down => "Down",
                Keys.Up => "Up",
                Keys.Escape => "Esc",
                Keys.Tab => "Tab",
                _ => (keys & Keys.KeyCode).ToString(),
            });

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// The mnemonic keys this window itself offers — the underlined letter on every
    /// label, button and group box — read from the live controls rather than written
    /// down anywhere.
    /// </summary>
    /// <remarks>
    /// Read, and not remembered, on purpose: a list of letters kept beside the buttons
    /// is a list that goes on describing a button after the button has gone, which is
    /// exactly what happened to this window's help when it was a hand-written list.
    /// The spoken name is the control's own accessible name, so the help says what the
    /// screen reader says.
    /// </remarks>
    public static IReadOnlyList<(string Keys, string What)> Mnemonics(Control root)
    {
        var offered = new List<(char Letter, string Keys, string What)>();

        // A window that has not been shown reports everything it holds as not visible
        // — that is how Control.Visible answers for a control whose window is not on
        // screen — and the offline checks build the window exactly that way. So a
        // control is left out only when the window itself is on screen and the control
        // is still hidden, which is the case worth leaving out: something the
        // application has taken off the window.
        var windowIsOnScreen = root.Visible;

        foreach (var control in Descendants(root))
        {
            if (windowIsOnScreen && !control.Visible)
                continue;

            if (!control.Enabled)
                continue;

            if (Letter(control.Text) is not { } letter)
                continue;

            // Windows routes a mnemonic to one control, so the help names it once.
            if (offered.Any(item => item.Letter == letter))
                continue;

            offered.Add((letter, $"Alt + {letter}", SpokenName(control)));
        }

        return [.. offered.OrderBy(item => item.Letter).Select(item => (item.Keys, item.What))];
    }

    /// <summary>The mnemonic letter of a control's text, or null when it has none.</summary>
    private static char? Letter(string? text)
    {
        var at = (text ?? string.Empty).IndexOf('&');

        // "&&" is an ampersand to show, not a mnemonic.
        return at >= 0 && at + 1 < text!.Length && text[at + 1] != '&'
            ? char.ToUpperInvariant(text[at + 1])
            : null;
    }

    /// <summary>What a screen reader would read for this control.</summary>
    private static string SpokenName(Control control)
    {
        var name = control.AccessibleName;
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return Strings.Plain(control.Text).Trim().TrimEnd(':').Trim();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }
}
