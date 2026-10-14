namespace AudiobookDownloader.Core;

/// <summary>Progress phases for one book download.</summary>
public enum DownloadPhase
{
    Downloading,
    Decrypting,
    Done,
}

/// <summary>One progress update: phase + 0..100 percent.</summary>
public sealed record DownloadProgress(DownloadPhase Phase, double Percent, string Message);
