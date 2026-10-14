using System.Text.Json;

namespace AudiobookDownloader.Core;

/// <summary>Decryption keys remembered for one local file so the user is not asked twice.</summary>
public sealed record CachedKeys(string? FileName, string? Key, string? Iv, string? ActivationBytes);

/// <summary>
/// Everything the user can configure. Grouped the same way the Settings dialog
/// presents it: application, downloads, Audible account, Project Gutenberg,
/// language and accessibility.
/// </summary>
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

    public bool RemoveDrm { get; set; } = true;
    public bool DeleteIntermediateFile { get; set; } = true;

    /// <summary>Save each book in a folder named after its author, inside the download folder.</summary>
    public bool OrganizeByAuthor { get; set; } = false;

    /// <summary>Ask before downloading a book whose file is already in the download folder.</summary>
    public bool WarnAboutDuplicates { get; set; } = true;

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
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings could not be saved: {ex.Message}");
        }
    }

    public CachedKeys? FindKeys(string fileName) =>
        KeyCache.FirstOrDefault(k => string.Equals(k.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    public void CacheKeys(string fileName, CachedKeys keys)
    {
        KeyCache.RemoveAll(k => string.Equals(k.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        KeyCache.Add(keys with { FileName = fileName });
        Save();
    }
}
