using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// Settings, Language: the language of the app itself, and the language of the
/// books each source lists. The book languages used to be a list on the main
/// window that appeared and disappeared with the source; they all live here now.
/// </summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildLanguagePage()
    {
        var page = NewPage(Strings.T("Settings.Language"));
        var section = new SettingsSection(page, Announce);

        section.Group(Strings.T("Group.AppLanguage"));
        var items = Strings.AvailableLanguages
            .Select(l => $"{l.DisplayName} ({l.Code})")
            .ToList();
        var current = items.FirstOrDefault(i => i.EndsWith($"({_settings.UiLanguage})", StringComparison.OrdinalIgnoreCase))
                      ?? items[0];
        var uiCombo = NewCombo([.. items], current, Strings.Plain(Strings.T("Set.UiLanguage")).TrimEnd(':'));
        section.AddLabeled(Strings.T("Set.UiLanguage"), uiCombo);
        _apply.Add(() =>
        {
            _settings.UiLanguage = LanguageCodeFrom(uiCombo.SelectedItem?.ToString());
            Strings.SetLanguage(_settings.UiLanguage);
        });

        // One list per source that can search by language. Each writes straight
        // into the settings the source reads, so the main window follows it.
        section.Group(Strings.T("Group.BookLanguages"));
        (string Label, ILanguageFilter Filter)[] sources =
        [
            (Strings.T("Set.LanguageAudible"), _audible),
            (Strings.T("Set.LanguageLibriVox"), new LibriVoxProvider(_settings)),
            (Strings.T("Set.LanguageGutenberg"), new GutenbergProvider(_settings)),
        ];
        foreach (var (label, filter) in sources)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DisplayMember = nameof(LanguageOption.DisplayName),
                AccessibleRole = AccessibleRole.ComboBox,
            };
            combo.Items.Add(new LanguageOption("", Strings.T("Main.AllLanguages")));
            foreach (var language in filter.SupportedLanguages.Where(l => !string.IsNullOrWhiteSpace(l.Code)))
                combo.Items.Add(language);
            combo.SelectedItem = combo.Items.Cast<LanguageOption>().FirstOrDefault(l => l.Code == filter.CurrentLanguage)
                                 ?? combo.Items[0];
            section.AddLabeled(label, combo);
            _apply.Add(() => filter.CurrentLanguage = (combo.SelectedItem as LanguageOption)?.Code ?? "");
        }

        section.AddNote(Strings.T("Language.Note"));
        return page;
    }

    /// <summary>"Hindi (hi)" to "hi".</summary>
    private static string LanguageCodeFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        return open >= 0 && close > open ? text[(open + 1)..close].Trim() : "";
    }
}
