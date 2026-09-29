using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace OmniPadServer.Core;

public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    bool IsUpdateAvailable,
    string ReleaseTitle,
    string ReleaseNotes,
    string? DownloadUrl,
    DateTimeOffset? PublishedAt
);

public static class UpdateChecker
{
    public const string DefaultCurrentVersion = "1.2.0";
    public const string GitHubRepoOwner = "WizardOfXerox";
    public const string GitHubRepoName = "OmniPad";
    public const string GitHubReleasesApiUrl = $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";
    public const string GitHubRawVersionUrl = $"https://raw.githubusercontent.com/{GitHubRepoOwner}/{GitHubRepoName}/main/version.json";

    public static string GetInstalledVersion(string appDirectory)
    {
        try
        {
            string versionFile = Path.Combine(appDirectory, "version.json");
            if (File.Exists(versionFile))
            {
                string json = File.ReadAllText(versionFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    return v.GetString() ?? DefaultCurrentVersion;
                }
            }
        }
        catch { }

        return DefaultCurrentVersion;
    }

    public static async Task<UpdateInfo> CheckForUpdatesAsync(string appDirectory, string? customManifestUrl = null)
    {
        string currentVersion = GetInstalledVersion(appDirectory);

        // 1. Check if there is a local staged update package or local manifest
        string localUpdateZip = Path.Combine(appDirectory, "OmniPad-Update.zip");
        string localManifest = Path.Combine(appDirectory, "update_manifest.json");

        if (File.Exists(localManifest))
        {
            try
            {
                string json = await File.ReadAllTextAsync(localManifest);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string newVer = root.GetProperty("version").GetString() ?? currentVersion;
                string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Local Update" : "Local Update";
                string notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                string? url = root.TryGetProperty("downloadUrl", out var u) ? u.GetString() : null;

                bool available = IsNewer(newVer, currentVersion);
                return new UpdateInfo(currentVersion, newVer, available, title, notes, url ?? localUpdateZip, DateTimeOffset.UtcNow);
            }
            catch { }
        }

        if (File.Exists(localUpdateZip))
        {
            return new UpdateInfo(
                currentVersion,
                "1.3.0",
                true,
                "Local Staged Update Bundle",
                "Ready to install from local package.",
                localUpdateZip,
                DateTimeOffset.UtcNow
            );
        }

        // 2. Check remote manifest or GitHub Releases
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OmniPadUpdater", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string targetUrl = !string.IsNullOrEmpty(customManifestUrl) ? customManifestUrl : GitHubReleasesApiUrl;

            try
            {
                var response = await client.GetAsync(targetUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // If querying GitHub Releases API
                    if (root.TryGetProperty("tag_name", out var tagProp))
                    {
                        string latestVer = tagProp.GetString() ?? currentVersion;
                        string title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? $"Release {latestVer}" : $"Release {latestVer}";
                        string notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                        DateTimeOffset? pubAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var dt) ? dt : DateTimeOffset.UtcNow;

                        string? downloadUrl = null;
                        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (asset.TryGetProperty("name", out var aName) && aName.GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    if (asset.TryGetProperty("browser_download_url", out var dl))
                                    {
                                        downloadUrl = dl.GetString();
                                        break;
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(downloadUrl) && root.TryGetProperty("html_url", out var htmlUrl))
                        {
                            downloadUrl = htmlUrl.GetString();
                        }

                        bool isNewer = IsNewer(latestVer, currentVersion);
                        return new UpdateInfo(currentVersion, latestVer, isNewer, title, notes, downloadUrl, pubAt);
                    }
                    else if (root.TryGetProperty("version", out var verProp))
                    {
                        // Direct manifest JSON format (e.g. version.json)
                        string latest = verProp.GetString() ?? currentVersion;
                        string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "Remote Update";
                        string notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                        string? url = root.TryGetProperty("downloadUrl", out var u) ? u.GetString() : null;

                        return new UpdateInfo(currentVersion, latest, IsNewer(latest, currentVersion), title, notes, url, DateTimeOffset.UtcNow);
                    }
                }
            }
            catch { }

            // 3. Fallback: if customManifestUrl was null and GitHub Releases failed (e.g. 404 before first release), check raw version.json
            if (string.IsNullOrEmpty(customManifestUrl))
            {
                try
                {
                    var rawResp = await client.GetAsync(GitHubRawVersionUrl);
                    if (rawResp.IsSuccessStatusCode)
                    {
                        string json = await rawResp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("version", out var verProp))
                        {
                            string latest = verProp.GetString() ?? currentVersion;
                            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "OmniPad Release";
                            string notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                            string? url = root.TryGetProperty("downloadUrl", out var u) ? u.GetString() : null;

                            return new UpdateInfo(currentVersion, latest, IsNewer(latest, currentVersion), title, notes, url, DateTimeOffset.UtcNow);
                        }
                    }
                }
                catch { }
            }
        }
        catch
        {
            // Network offline or endpoint unreachable
        }

        return new UpdateInfo(
            currentVersion,
            currentVersion,
            false,
            "Up to date",
            "You are running the latest version of OmniPad.",
            null,
            DateTimeOffset.UtcNow
        );
    }

    public static async Task<bool> DownloadUpdateAsync(string downloadUrl, string destinationPath, Action<int>? onProgress = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OmniPadUpdater", "1.0"));
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    int pct = (int)((totalRead * 100) / totalBytes.Value);
                    onProgress?.Invoke(pct);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        try
        {
            var vLatest = ParseVersion(latest);
            var vCurrent = ParseVersion(current);
            return vLatest > vCurrent;
        }
        catch
        {
            return !string.Equals(latest.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Version ParseVersion(string ver)
    {
        string cleaned = ver.Trim().TrimStart('v', 'V');
        int dashIdx = cleaned.IndexOf('-');
        if (dashIdx > 0) cleaned = cleaned[..dashIdx];

        return Version.TryParse(cleaned, out var v) ? v : new Version(1, 0, 0, 0);
    }
}
