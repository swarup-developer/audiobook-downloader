namespace AudiobookDownloader.Core;

/// <summary>
/// What may be shown to somebody, and what may not.
/// </summary>
/// <remarks>
/// The Audible library writes "[null]" and "[empty]" where a field was missing in
/// Amazon's answer, and other JSON libraries write "null". None of those is a
/// title, an author or a file name — they are the *absence* of one, and must be
/// treated as missing rather than passed on. The rule lives in one place so that a
/// provider and the row that displays it cannot disagree about it.
/// </remarks>
public static class DisplayText
{
    /// <summary>The placeholders a JSON library writes where a field was missing.</summary>
    private static readonly HashSet<string> Placeholders =
        new(StringComparer.OrdinalIgnoreCase) { "[null]", "[empty]", "null", "undefined", "nil" };

    /// <summary>
    /// True when this is not a value at all: empty, blank, or one of the
    /// placeholders above.
    /// </summary>
    public static bool IsMissing(string? text) =>
        string.IsNullOrWhiteSpace(text) || Placeholders.Contains(text.Trim());

    /// <summary>
    /// The first of <paramref name="candidates"/> that is a real value, trimmed, or
    /// <paramref name="whenNothing"/> when none of them is.
    /// </summary>
    public static string FirstReal(string whenNothing, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!IsMissing(candidate))
                return candidate!.Trim();
        }

        return whenNothing;
    }
}
