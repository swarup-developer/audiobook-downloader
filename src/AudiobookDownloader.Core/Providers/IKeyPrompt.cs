namespace AudiobookDownloader.Core;

/// <summary>
/// Ask the user for decryption keys (used for local AAX/AAXC files).
/// Implemented by the UI so Core stays UI-free.
/// </summary>
public interface IKeyPrompt
{
    /// <summary>Show a dialog. Return null when the user cancels.</summary>
    CachedKeys? Prompt(string fileName, bool askKeyIv, bool askActivationBytes);
}
