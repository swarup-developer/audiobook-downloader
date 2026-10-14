using System.Net.Http;
using System.Text.Json;

namespace AudiobookDownloader.Core;

/// <summary>A newer release than the one running, as GitHub describes it.</summary>
/// <param name="InstallerUrl">The release's setup program, for an installed copy.</param>
/// <param name="PortableUrl">The release's single exe, for a copy run from a folder.</param>
public sealed record UpdateInfo(Version Latest, string Url, string Notes,
    string? InstallerUrl = null, string? PortableUrl = null);

/// <summary>
/// Asks GitHub whether a newer version has been released. Only the public release
/// list is read; nothing about the person or their library is sent.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "swarup-developer/audiobook-downloader";

    /// <summary>The page people download new versions from.</summary>
    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

    /// <summary>Where the latest release is read from. Overridable so the self-test can serve its own.</summary>
    public static string ApiUrl { get; set; } = $"https://api.github.com/repos/{Repository}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub refuses API requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookDownloader-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The newer release, or null when this is the latest (or none is published yet).</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(ApiUrl, ct);

        // No release published yet is not an error.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (ParseVersion(tag) is not { } latest || latest <= Normalise(current))
            return null;

        var url = root.TryGetProperty("html_url", out var u) && u.GetString() is { Length: > 0 } page
            ? page
            : ReleasesPage;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        // The files attached to the release: the setup program and the portable exe.
        string? installer = null, portable = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var link = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (string.IsNullOrWhiteSpace(link) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                    installer ??= link;
                else
                    portable ??= link;
            }
        }

        return new UpdateInfo(latest, url, notes, installer, portable);
    }

    /// <summary>"v1.2.0", "1.2" and "1.2.0-beta" all read as versions; anything else is null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+', ' ']);
        if (dash >= 0)
            text = text[..dash];

        return Version.TryParse(text.Contains('.') ? text : text + ".0", out var version)
            ? Normalise(version)
            : null;
    }

    /// <summary>1.2 and 1.2.0 are the same release.</summary>
    private static Version Normalise(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
