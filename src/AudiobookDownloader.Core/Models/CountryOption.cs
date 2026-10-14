namespace AudiobookDownloader.Core;

/// <summary>
/// One Audible store the user can sign in to: the store's own code ("us", "in")
/// together with the country name shown to the user.
/// </summary>
/// <remarks>
/// The name is the whole of <see cref="ToString"/>, deliberately — "India", never
/// "India (in)". The code is an Audible detail: it is what the sign-in is sent with,
/// and it must never be what a screen reader reads out. A selector built from the
/// codes announces "in" where a person should hear "India", and nothing about the
/// screen looks wrong to anybody testing it by eye.
/// </remarks>
public sealed record CountryOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Audible stores the app offers, in the order they are shown.
/// </summary>
/// <remarks>
/// Only the codes live here. A country's name is part of the interface language and
/// belongs in the string table, so the same store is "India", "भारत" or "India"
/// depending on the language the user reads.
/// </remarks>
public static class AudibleStores
{
    /// <summary>The store codes, the same ones the Audible library knows.</summary>
    public static readonly string[] Codes =
        ["us", "uk", "ca", "au", "in", "de", "fr", "es", "it", "jp", "br"];

    /// <summary>The string-table key holding the name of one store.</summary>
    public static string NameKey(string code) => $"Country.{code}";

    /// <summary>True when this is one of the stores above.</summary>
    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        Codes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);
}
