namespace AudiobookDownloader.Core;

/// <summary>
/// One selectable language: a short code ("en", "hi") plus the name shown to the
/// user. Providers that can filter their catalogue expose a list of these.
/// </summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    /// <summary>Shown in the language selector, e.g. "Hindi (hi)".</summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Code) ? DisplayName : $"{DisplayName} ({Code})";
}

/// <summary>
/// Implemented by providers that can restrict their search to one language
/// (Project Gutenberg, for example). The UI shows a language selector only for
/// these providers.
/// </summary>
public interface ILanguageFilter
{
    /// <summary>Every language the provider can search in.</summary>
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    /// <summary>The language the provider is currently searching in; empty means all languages.</summary>
    string CurrentLanguage { get; set; }
}
