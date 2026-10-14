using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The Audible countries, named in the interface language the user reads.
/// </summary>
/// <remarks>
/// Built fresh on every call rather than cached: the interface language can change
/// while the app is running, and a country list cached in the language it was first
/// built in would go on announcing the old names.
/// </remarks>
internal static class Countries
{
    /// <summary>Every country the app offers, in the order it shows them.</summary>
    public static IReadOnlyList<CountryOption> All() =>
        [.. AudibleStores.Codes.Select(code => new CountryOption(code, Strings.T(AudibleStores.NameKey(code))))];

    /// <summary>
    /// The country name for a store code, for anywhere the store is named to the
    /// user. An unknown code is shown as itself rather than dressed up as a country.
    /// </summary>
    public static string Name(string? code) =>
        AudibleStores.IsKnown(code) ? Strings.T(AudibleStores.NameKey(code!.Trim())) : code ?? string.Empty;
}
