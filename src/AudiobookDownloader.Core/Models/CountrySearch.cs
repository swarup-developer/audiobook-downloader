using System.Globalization;
using System.Text;

namespace AudiobookDownloader.Core;

/// <summary>
/// Finds the country a person meant from the letters they typed.
/// </summary>
/// <remarks>
/// The country list is searched by typing, and an incomplete or mistyped name has to
/// land on the right country: "Ind", "indai" and "grmany" all mean India or Germany.
/// So a typed name is matched in steps — the whole name, then the countries whose
/// name starts with what was typed, then the countries with a later word starting
/// with it ("kingdom" for the United Kingdom), then a near miss — and a name always
/// wins over a store code.
///
/// The store code ("in", "uk") is understood too, because somebody who knows their
/// account is on the "uk" store will type it, but it ranks below every name: the
/// codes are an Audible detail that the interface never shows.
/// </remarks>
public static class CountrySearch
{
    /// <summary>No country was named.</summary>
    private const int RankNothing = int.MaxValue;

    /// <summary>
    /// What a typed name is compared as: letters in lower case, without accents, and
    /// with runs of spaces collapsed to one.
    /// </summary>
    /// <remarks>
    /// "España" typed as "espana", "Japón" as "japon" and "United  Kingdom" as
    /// "united kingdom" all have to find their country, because nobody typing a
    /// country's name into a list they cannot see is going to reproduce its accents
    /// or its spacing.
    /// </remarks>
    public static string Normalize(string? text)
    {
        var normalized = new StringBuilder();

        foreach (var character in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(character))
            {
                // Leading and repeated spaces are not something a person can hear,
                // so they must not decide whether a country is found.
                if (normalized.Length > 0 && normalized[^1] != ' ')
                    normalized.Append(' ');

                continue;
            }

            normalized.Append(char.ToLowerInvariant(character));
        }

        while (normalized.Length > 0 && normalized[^1] == ' ')
            normalized.Length--;

        return normalized.ToString();
    }

    /// <summary>
    /// The country a typed name means, or null when it means no country at all.
    /// </summary>
    public static CountryOption? Resolve(IReadOnlyList<CountryOption> countries, string? typed)
    {
        var query = Normalize(typed);
        if (query.Length == 0)
            return null;

        CountryOption? found = null;
        var foundRank = RankNothing;
        var foundDistance = int.MaxValue;

        foreach (var country in countries)
        {
            var (rank, distance) = Rank(Normalize(country.Name), Normalize(country.Code), query);

            // The earliest rank wins, so a country whose name starts with what was
            // typed beats one that merely contains it; equal ranks are settled by
            // how far off the name is, and then by the order of the list.
            if (rank < foundRank || (rank == foundRank && distance < foundDistance))
            {
                found = country;
                foundRank = rank;
                foundDistance = distance;
            }
        }

        return found;
    }

    /// <summary>
    /// How well one country answers what was typed: the lower the rank the closer the
    /// match, and the distance only settles two countries that rank the same.
    /// </summary>
    private static (int Rank, int Distance) Rank(string name, string code, string query)
    {
        if (name == query)
            return (0, 0);

        if (name.StartsWith(query, StringComparison.Ordinal))
            return (1, name.Length - query.Length);

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Any(word => word.StartsWith(query, StringComparison.Ordinal)))
            return (2, name.Length - query.Length);

        if (code == query)
            return (3, 0);

        // Anything shorter than three letters is contained in half the list, so only
        // a name that actually starts with it is a match.
        if (query.Length < 3)
            return (RankNothing, int.MaxValue);

        if (name.Contains(query, StringComparison.Ordinal))
            return (4, name.Length - query.Length);

        // A name that is only a letter or two out is the country being named; a piece
        // of a name that is that close is a weaker guess, because the rest of the name
        // is unaccounted for. Keeping them apart is what stops "inida" being read as a
        // misspelling of "unido" rather than of "India".
        var nameDistance = EditDistance(query, name);
        if (nameDistance <= Tolerance(query.Length))
            return (5, nameDistance);

        var wordDistance = words.Min(word => EditDistance(query, word));
        if (wordDistance <= Tolerance(query.Length))
            return (6, wordDistance);

        return (RankNothing, int.MaxValue);
    }

    /// <summary>
    /// How wrong a typed name may be and still name a country. Two letters covers a
    /// word of the length country names run to; longer names are allowed one more.
    /// </summary>
    private static int Tolerance(int length) => length <= 2 ? 0 : length <= 7 ? 2 : 3;

    /// <summary>The letters one string has to gain, lose or change to become another.</summary>
    private static int EditDistance(string from, string to)
    {
        if (from.Length == 0)
            return to.Length;

        if (to.Length == 0)
            return from.Length;

        var previous = new int[to.Length + 1];
        var current = new int[to.Length + 1];

        for (var column = 0; column <= to.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= from.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= to.Length; column++)
            {
                var substitution = previous[column - 1] + (from[row - 1] == to[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[to.Length];
    }
}

/// <summary>
/// The word being typed into a country list.
/// </summary>
/// <remarks>
/// Typing works the way a Windows list does: every letter joins the word being typed,
/// and a word that names no country is searched again from its tail — dropping its
/// first letter, then its first two, and so on. That is what makes a wrong turn
/// recoverable: somebody who starts typing "ind" and goes on to type "spain" still
/// finds Spain, without having to work out that the search needs restarting.
///
/// What was typed is deliberately not shown anywhere: a list that holds countries
/// goes on showing and announcing a country's full name, never half a name that a
/// screen reader would read out as though a country had been chosen.
/// </remarks>
public sealed class CountryTypeAhead
{
    /// <summary>
    /// The fewest letters a word can be cut back to and still stand for a country.
    /// One letter would move the list to whichever country happens to start with it.
    /// </summary>
    private const int ShortestTail = 2;

    private readonly StringBuilder _typed = new();

    /// <summary>What has been typed so far of the country being named.</summary>
    public string Typed => _typed.ToString();

    /// <summary>True when a typed character is part of a country's name.</summary>
    public static bool IsSearchCharacter(char character) =>
        char.IsLetter(character) || character == ' ';

    /// <summary>Starts a new word: what was typed before is forgotten.</summary>
    public void Clear() => _typed.Clear();

    /// <summary>
    /// Adds a typed character and reports the country the word now names, or null
    /// when it names none.
    /// </summary>
    public CountryOption? Type(char character, IReadOnlyList<CountryOption> countries)
    {
        // A key that is no part of any country's name changes nothing.
        if (!IsSearchCharacter(character))
            return CountrySearch.Resolve(countries, _typed.ToString());

        _typed.Append(character);

        if (CountrySearch.Resolve(countries, _typed.ToString()) is { } country)
            return country;

        // The word as a whole names nothing, so its front is dropped a letter at a
        // time and what is left is tried: typing on past a wrong start still finds
        // the country being named.
        for (var drop = 1; drop <= _typed.Length - ShortestTail; drop++)
        {
            if (CountrySearch.Resolve(countries, _typed.ToString(drop, _typed.Length - drop)) is { } tail)
                return tail;
        }

        return null;
    }

    /// <summary>Removes the last typed character and reports what the word names now.</summary>
    public CountryOption? Backspace(IReadOnlyList<CountryOption> countries)
    {
        if (_typed.Length > 0)
            _typed.Length--;

        return CountrySearch.Resolve(countries, _typed.ToString());
    }
}
