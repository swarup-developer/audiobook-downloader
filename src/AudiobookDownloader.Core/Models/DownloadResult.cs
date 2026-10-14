namespace AudiobookDownloader.Core;

/// <summary>The outcome of one download.</summary>
public sealed record DownloadResult(bool Success, string? OutputFile, string? ErrorMessage)
{
    /// <summary>
    /// True when the *user* stopped the download rather than something going wrong.
    /// Any partial file is kept, so the item can be retried and resume.
    /// </summary>
    public bool Cancelled { get; init; }
}
