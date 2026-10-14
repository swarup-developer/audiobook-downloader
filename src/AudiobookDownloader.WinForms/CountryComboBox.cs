using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The country list, searchable by typing the first letters of a country's name.
/// </summary>
/// <remarks>
/// The list is not editable on purpose: it holds countries, so it always shows and
/// announces a country's full name. An edit box would put the half-typed text on
/// screen, and a screen reader would read "Ind" as though that were the chosen
/// country. Typing therefore searches the list rather than filling it in, and the
/// letters typed are never what the list shows.
///
/// The search is this control's own, and the Windows list's built-in type-ahead is
/// deliberately switched off: that one matches the first letters of a name exactly,
/// so a mistyped name ("Indai") would leave the list dead with no explanation.
/// </remarks>
internal sealed class CountryComboBox : ComboBox
{
    private const int WmKeyDown = 0x0100;
    private const int WmChar = 0x0102;

    /// <summary>
    /// How long a pause means the next letter starts a new country's name rather
    /// than continuing the one being typed.
    /// </summary>
    private const int PauseMilliseconds = 1000;

    private readonly System.Windows.Forms.Timer _pause = new() { Interval = PauseMilliseconds };
    private readonly CountryTypeAhead _typeAhead = new();

    internal CountryComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        AccessibleRole = AccessibleRole.ComboBox;
        _pause.Tick += (_, _) => ForgetTyping();
    }

    /// <summary>
    /// Raised with what has been typed whenever it names no country, so that the
    /// silence can be explained instead of leaving somebody typing into a list that
    /// appears to be ignoring them. It is raised with an empty word when the last
    /// letter has been taken back, which is the moment to stop saying that.
    /// </summary>
    internal event Action<string>? NoMatch;

    /// <summary>What has been typed so far of the country being named.</summary>
    internal string SearchText => _typeAhead.Typed;

    /// <summary>The country the list is on, or null while none has been chosen.</summary>
    internal CountryOption? SelectedCountry => SelectedItem as CountryOption;

    /// <summary>
    /// Fills the list and puts the saved country on screen. A null or unknown code
    /// leaves nothing chosen, so a country nobody has ever picked is not shown as
    /// though it had been.
    /// </summary>
    internal void Fill(IEnumerable<CountryOption> countries, string? chosenCode)
    {
        Items.Clear();

        foreach (var country in countries)
            Items.Add(country);

        if (Items.Count == 0 || !AudibleStores.IsKnown(chosenCode))
            return;

        SelectedItem = Items.Cast<CountryOption>().FirstOrDefault(country => country.Code == chosenCode)
                       ?? Items[0];
    }

    /// <summary>Starts a new word: what was typed before is forgotten.</summary>
    internal void ForgetTyping()
    {
        _pause.Stop();
        _typeAhead.Clear();
    }

    /// <summary>
    /// Types a country's name, exactly as a keyboard does. What the self-test drives
    /// is therefore the search a person uses, not a chosen item put there by hand.
    /// </summary>
    internal CountryOption? TypeCountry(string typed)
    {
        CountryOption? found = null;

        foreach (var character in typed)
            found = TypeCharacter(character);

        return found;
    }

    /// <summary>
    /// Takes the typed letters out of the list's own hands, so the search is the one
    /// above: the whole word, a later word of a name, a name with a letter wrong, or
    /// nothing, all answered the same way.
    /// </summary>
    /// <remarks>
    /// The built-in type-ahead would otherwise run as well, and it answers a mistyped
    /// name by leaving the list exactly where it was, with nothing said about it.
    /// </remarks>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmChar && CountryTypeAhead.IsSearchCharacter((char)m.WParam))
        {
            TypeCharacter((char)m.WParam);
            return;
        }

        if (m.Msg == WmKeyDown && (Keys)m.WParam == Keys.Back && _typeAhead.Typed.Length > 0)
        {
            TypeCharacter('\b');
            return;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// A space is part of a country's name ("United States"), so it has to reach the
    /// search instead of being taken as a key that presses the window's buttons.
    /// </summary>
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) == Keys.Space || base.IsInputKey(keyData);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pause.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>Adds one typed character and reports what the list is on now.</summary>
    private CountryOption? TypeCharacter(char character)
    {
        var countries = Items.Cast<CountryOption>().ToArray();

        _pause.Stop();

        var found = character == '\b'
            ? _typeAhead.Backspace(countries)
            : _typeAhead.Type(character, countries);

        if (_typeAhead.Typed.Length > 0)
            _pause.Start();

        if (found is not null)
        {
            var index = Items.IndexOf(found);

            if (index >= 0 && SelectedIndex != index)
                SelectedIndex = index;
        }
        else
        {
            NoMatch?.Invoke(_typeAhead.Typed);
        }

        return found;
    }
}
