using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudiobookDownloader.Core;

/// <summary>Decryption keys remembered for one local file so the user is not asked twice.</summary>
public sealed record CachedKeys(string? FileName, string? Key, string? Iv, string? ActivationBytes);

/// <summary>
/// Everything the user can configure. Grouped the same way the Settings dialog
/// presents it: application, downloads, Audible account, Project Gutenberg,
/// language and accessibility.
/// </summary>
/// <summary>One Audible account signed in on this computer.</summary>
public sealed class AudibleAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>What the account is called on screen: its e-mail address once known.</summary>
    public string Name { get; set; } = "";

    /// <summary>The Audible store it belongs to, e.g. "in".</summary>
    public string Locale { get; set; } = "us";
}

/// <summary>A place in a book the listener wants to come back to.</summary>
public sealed class Bookmark
{
    /// <summary>The chapter file it is in, for a book split into files; 0 otherwise.</summary>
    public int Track { get; set; }

    public double Seconds { get; set; }

    public string Note { get; set; } = "";

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
}

public sealed class AppSettings
{
    // ---------------------------------------------------------------------
    // Application
    // ---------------------------------------------------------------------

    /// <summary>Default save location: the user's Desktop, as requested.</summary>
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Audiobooks");

    /// <summary>Folder scanned by the "Local files" provider for AAX/AAXC files.</summary>
    public string LocalFolder { get; set; } = "";

    /// <summary>Show a Windows notification-center toast when the queue finishes.</summary>
    public bool ToastWhenFinished { get; set; } = false;

    /// <summary>Open the download folder when the whole queue has finished.</summary>
    public bool OpenFolderWhenFinished { get; set; } = false;

    /// <summary>Ask before removing a pending item from the queue.</summary>
    public bool ConfirmBeforeQueueRemoval { get; set; } = true;

    /// <summary>Remember the provider that was selected when the app last closed.</summary>
    public bool RememberLastProvider { get; set; } = true;

    /// <summary>Provider id selected last time, when <see cref="RememberLastProvider"/> is on.</summary>
    public string? LastProviderId { get; set; }

    // ---------------------------------------------------------------------
    // Downloads
    // ---------------------------------------------------------------------

    /// <summary>
    /// Remove DRM from Audible books. Off until the person turns it on in Settings
    /// and accepts the personal-use notice; see <see cref="DrmRemovalEnabled"/>.
    /// </summary>
    public bool RemoveDrm { get; set; } = false;

    /// <summary>True once the personal-use notice for DRM removal has been accepted.</summary>
    public bool DrmRemovalConsented { get; set; } = false;

    /// <summary>
    /// Whether DRM is actually removed: only when it is switched on <em>and</em> the
    /// notice was accepted. A settings file from an older version that had it on
    /// without that consent leaves Audible books encrypted until it is confirmed.
    /// </summary>
    [JsonIgnore]
    public bool DrmRemovalEnabled => RemoveDrm && DrmRemovalConsented;
    public bool DeleteIntermediateFile { get; set; } = true;

    /// <summary>Save each book in a folder named after its author, inside the download folder.</summary>
    public bool OrganizeByAuthor { get; set; } = false;

    /// <summary>Ask before downloading a book whose file is already in the download folder.</summary>
    public bool WarnAboutDuplicates { get; set; } = true;

    /// <summary>Audio format for Audible books: "m4b" (lossless, the default) or "mp3".</summary>
    public string OutputFormat { get; set; } = "m4b";

    /// <summary>Save one file per chapter, in a folder named after the book.</summary>
    public bool SplitByChapter { get; set; } = false;

    /// <summary>Save the cover picture as a .jpg file next to each book.</summary>
    public bool SaveCoverImage { get; set; } = false;

    /// <summary>Write the description, series and cover into the Audible book file itself.</summary>
    public bool EmbedBookInfo { get; set; } = true;

    /// <summary>How many books download at the same time (1 to 4).</summary>
    public int MaxParallelDownloads { get; set; } = 1;

    /// <summary>Download speed limit in kilobytes per second; 0 means no limit.</summary>
    public int SpeedLimitKBps { get; set; } = 0;

    // ---------------------------------------------------------------------
    // Updates, list view and player
    // ---------------------------------------------------------------------

    /// <summary>Look for a newer version on GitHub when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>How the book list is sorted: title, author, length or added.</summary>
    public string SortOrder { get; set; } = "title";

    /// <summary>Hide books that are already downloaded.</summary>
    public bool HideDownloaded { get; set; } = false;

    /// <summary>Where playback stopped in each file, in seconds, so it resumes there.</summary>
    public Dictionary<string, double> PlaybackPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Playback speed used by the player, 0.5 to 3.</summary>
    public double PlaybackSpeed { get; set; } = 1.0;

    /// <summary>Say the chapter's name when the player reaches a new one.</summary>
    public bool AnnounceChapters { get; set; } = true;

    /// <summary>Jump over long silences while playing.</summary>
    public bool SkipSilence { get; set; } = false;

    /// <summary>Bookmarks per book (file or chapter folder), in the order they were made.</summary>
    public Dictionary<string, List<Bookmark>> Bookmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seconds listened per day ("2026-10-14"), for the listening statistics.</summary>
    public Dictionary<string, double> ListeningLog { get; set; } = [];

    /// <summary>Books played to the end, with the day they were finished.</summary>
    public Dictionary<string, string> FinishedBooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------
    // Backup
    // ---------------------------------------------------------------------

    /// <summary>Copy every finished book into <see cref="BackupFolder"/> as well.</summary>
    public bool BackupEnabled { get; set; } = false;

    /// <summary>Where finished books are copied, e.g. a OneDrive folder that syncs to the cloud.</summary>
    public string BackupFolder { get; set; } = "";

    // ---------------------------------------------------------------------
    // Audible account
    // ---------------------------------------------------------------------

    /// <summary>The Audible store the account belongs to, e.g. "us" or "uk".</summary>
    public string AudibleLocale { get; set; } = "us";

    /// <summary>
    /// True once the user has actually chosen that store.
    /// </summary>
    /// <remarks>
    /// The sign-in asks for a country first and offers the sign-in itself only once
    /// one has been chosen, so the default above must not look like a choice nobody
    /// made — otherwise a first-time user is signed in to the United States store
    /// without ever having been asked.
    /// </remarks>
    public bool AudibleCountryChosen { get; set; }

    /// <summary>Keep the Audible sign-in on disk so the next run can reuse it.</summary>
    public bool AudibleRememberSignIn { get; set; } = true;

    /// <summary>Fetch the Audible library automatically after signing in.</summary>
    public bool AudibleLoadLibraryAfterSignIn { get; set; } = true;

    /// <summary>Language the Audible library is narrowed to, e.g. "hindi"; empty means all.</summary>
    public string AudibleLanguage { get; set; } = "";

    /// <summary>List the episodes of podcasts in the library, so they can be downloaded.</summary>
    public bool AudibleIncludePodcasts { get; set; } = true;

    /// <summary>When the app starts, download books bought since the last check.</summary>
    public bool AudibleAutoDownloadNew { get; set; } = false;

    /// <summary>When the library was last checked for new books (UTC).</summary>
    public DateTimeOffset? AudibleLastSync { get; set; }

    /// <summary>Every Audible account signed in on this computer.</summary>
    public List<AudibleAccount> AudibleAccounts { get; set; } = [];

    /// <summary>The account in use; null means the first one.</summary>
    public string? ActiveAudibleAccountId { get; set; }

    // ---------------------------------------------------------------------
    // LibriVox
    // ---------------------------------------------------------------------

    /// <summary>Language LibriVox searches are narrowed to, e.g. "English"; empty means all.</summary>
    public string LibriVoxLanguage { get; set; } = "";

    // ---------------------------------------------------------------------
    // Project Gutenberg
    // ---------------------------------------------------------------------

    /// <summary>Language code used for Gutenberg searches; empty means all languages.</summary>
    public string GutenbergLanguage { get; set; } = "en";

    /// <summary>Preferred Gutenberg download format: epub, html or txt.</summary>
    public string GutenbergFormat { get; set; } = "epub";

    // ---------------------------------------------------------------------
    // Language
    // ---------------------------------------------------------------------

    /// <summary>Language of the application interface itself.</summary>
    public string UiLanguage { get; set; } = "en";

    // ---------------------------------------------------------------------
    // Accessibility
    // ---------------------------------------------------------------------

    /// <summary>
    /// Announce status changes through the operating system's accessibility
    /// events so the user's screen reader (NVDA, Narrator, …) reads them.
    /// This never uses a built-in voice.
    /// </summary>
    public bool AnnounceStatusChanges { get; set; } = true;

    /// <summary>Announce download progress milestones through the screen reader.</summary>
    public bool AnnounceProgress { get; set; } = true;

    /// <summary>Keep the activity log panel on screen at all times.</summary>
    public bool KeepActivityLogVisible { get; set; } = true;

    // ---------------------------------------------------------------------
    // Search history / key cache (not shown in Settings)
    // ---------------------------------------------------------------------

    /// <summary>Recent search text per provider id, newest first, for the search-box dropdown.</summary>
    public Dictionary<string, List<string>> SearchHistory { get; set; } = [];

    public const int MaxSearchHistoryPerProvider = 15;

    public IReadOnlyList<string> GetSearches(string providerId) =>
        SearchHistory.TryGetValue(providerId, out var list) ? list : [];

    public void AddSearch(string providerId, string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return;

        if (!SearchHistory.TryGetValue(providerId, out var list))
            SearchHistory[providerId] = list = [];

        list.RemoveAll(x => x.Equals(q, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, q);
        if (list.Count > MaxSearchHistoryPerProvider)
            list.RemoveRange(MaxSearchHistoryPerProvider, list.Count - MaxSearchHistoryPerProvider);
        Save();
    }

    public List<CachedKeys> KeyCache { get; set; } = [];

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudiobookDownloader");

    public static string SettingsFolder => Folder;

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>
    /// Where this instance saves itself instead of the real settings file. The
    /// self-test sets it, so checking the app can never overwrite your settings.
    /// </summary>
    [JsonIgnore]
    public string? SavePath { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings file could not be read, using defaults: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SavePath ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings could not be saved: {ex.Message}");
        }
    }

    public CachedKeys? FindKeys(string fileName) =>
        KeyCache.FirstOrDefault(k => string.Equals(k.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static readonly object KeyGate = new();

    public void CacheKeys(string fileName, CachedKeys keys)
    {
        lock (KeyGate)
        {
            CacheKeysUnlocked(fileName, keys);
        }
    }

    private void CacheKeysUnlocked(string fileName, CachedKeys keys)
    {
        KeyCache.RemoveAll(k => string.Equals(k.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        KeyCache.Add(keys with { FileName = fileName });
        Save();
    }
}
