using System.Text.Json.Serialization;

namespace AudiobookDownloader.Core;

/// <summary>One audiobook row shown in the library list, whatever the provider.</summary>
public sealed record BookInfo(
    string ProviderId,
    string BookId,
    string Title,
    string Author,
    string Narrator,
    int LengthMinutes,
    string? CoverUrl,
    string? DownloadUrl,
    string? Info)
{
    /// <summary>Shown when a provider sends a book with neither a title nor an identifier.</summary>
    public const string UnknownTitle = "Unknown title";

    /// <summary>Shown when a provider sends a book with nobody credited.</summary>
    public const string UnknownAuthor = "Unknown author";

    /// <summary>The book's language as the provider names it, when it says; used by language filters.</summary>
    public string? Language { get; init; }

    /// <summary>The publisher's description, when the provider sends one.</summary>
    public string? Description { get; init; }

    /// <summary>Series name and position, e.g. "Harry Potter, Book 1".</summary>
    public string? Series { get; init; }

    /// <summary>When the book was added to the library (Audible), for sorting by date.</summary>
    public DateTimeOffset? DateAdded { get; init; }

    /// <summary>
    /// True when a file for this book is already in the download folder. Worked out
    /// when the list is shown, never stored with a cached catalogue.
    /// </summary>
    [JsonIgnore]
    public bool AlreadyDownloaded { get; init; }

    /// <summary>
    /// What the row adds for a book that is already downloaded. Set by the interface
    /// in its own language; the library itself has no translations.
    /// </summary>
    public static string AlreadyDownloadedText { get; set; } = "already downloaded";

    /// <summary>
    /// What the row reads, e.g. "The Hobbit - J. R. R. Tolkien".
    /// </summary>
    /// <remarks>
    /// The last line of defence before something reaches a screen reader: a
    /// provider that hands over a blank title, or a placeholder such as "[null]",
    /// still cannot make the list say "null". The row falls back to the book's
    /// identifier, and then to plain words. The same goes for a book nobody is
    /// credited on.
    /// </remarks>
    public override string ToString() =>
        $"{DisplayText.FirstReal(UnknownTitle, Title, BookId)} - " +
        $"{DisplayText.FirstReal(UnknownAuthor, Author)}" +
        (AlreadyDownloaded ? $" ({AlreadyDownloadedText})" : "");
}
