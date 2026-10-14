namespace AudiobookDownloader.Core;

public enum TicketFormat
{
    /// <summary>Modern Audible AAXC file, decrypted with a per-download key + IV.</summary>
    Aaxc,
    /// <summary>Legacy Audible AAX file, decrypted with the account's 8-digit activation bytes.</summary>
    Aax,
    /// <summary>Already DRM-free file (e.g. a LibriVox zip); just save it.</summary>
    PlainUrl,
}

/// <summary>A licence to download one book: the URL plus whatever decryption material is needed.</summary>
public sealed record DownloadTicket(
    string BookId,
    TicketFormat Format,
    Uri Url,
    string? Key,
    string? Iv,
    string? ActivationBytes,
    string SuggestedExtension = "")
{
    public bool NeedsDecryption => Format is TicketFormat.Aax or TicketFormat.Aaxc;
}
