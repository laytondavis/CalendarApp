using System.Text.Json;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using Microsoft.Extensions.Options;
#if __SKIA__
using Velopack;
using Velopack.Sources;
#endif

namespace CalendarApp.Services.Update;

public class UpdateService : IUpdateService
{
    private readonly string _githubRepo;
    private readonly HttpClient _httpClient;

#if __SKIA__
    private UpdateInfo? _pendingUpdate;
    private UpdateManager? _manager;
#endif

    public bool IsUpdateAvailable { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public string? NewVersionString { get; private set; }
    public string? ReleasesPageUrl { get; private set; }

    public UpdateService(IOptions<AppConfig> appConfig, HttpClient httpClient)
    {
        _githubRepo = appConfig.Value.GithubRepo ?? string.Empty;
        _httpClient = httpClient;

        // Build the human-readable releases page URL from the API URL.
        // GithubRepo is expected to be "https://github.com/owner/repo"
        ReleasesPageUrl = string.IsNullOrWhiteSpace(_githubRepo)
            ? null
            : _githubRepo.TrimEnd('/') + "/releases/latest";
    }

    public async Task<bool> CheckAndDownloadAsync(IProgress<int>? progress = null)
    {
#if __SKIA__
        return await CheckVelopackAsync(progress);
#elif __ANDROID__
        return await CheckGitHubApiAsync();
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    public void ApplyAndRestart()
    {
#if __SKIA__
        if (_manager == null || _pendingUpdate == null)
        {
            Console.WriteLine("[UpdateService] ApplyAndRestart called but no update is pending.");
            return;
        }
        try
        {
            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] ApplyAndRestart failed: {ex.Message}");
        }
#endif
    }

#if __SKIA__
    private async Task<bool> CheckVelopackAsync(IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(_githubRepo) || _githubRepo.Contains("YOUR_USERNAME"))
        {
            Console.WriteLine("[UpdateService] GithubRepo not configured — skipping update check.");
            return false;
        }

        try
        {
            Console.WriteLine($"[UpdateService] Checking for updates from: {_githubRepo}");
            _manager = new UpdateManager(new GithubSource(_githubRepo, null, false));

            var updateInfo = await _manager.CheckForUpdatesAsync();
            Console.WriteLine($"[UpdateService] CheckForUpdatesAsync returned: {(updateInfo == null ? "null" : "UpdateInfo")}");

            if (updateInfo == null)
            {
                // Null means no update found. Log what version we think we're at.
                try
                {
                    var currentInfo = await _manager.GetInformationAsync();
                    Console.WriteLine($"[UpdateService] Current installed version: {currentInfo?.CurrentVersion?.ToString() ?? "unknown"}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UpdateService] Could not read current version: {ex.Message}");
                }
                Console.WriteLine("[UpdateService] No update available (already up to date or version check failed).");
                return false;
            }

            var currentVer = updateInfo.CurrentVersion?.ToString() ?? "unknown";
            var remoteVer = updateInfo.TargetFullRelease.Version.ToString();
            Console.WriteLine($"[UpdateService] Current={currentVer}  Remote={remoteVer}");

            NewVersionString  = remoteVer;
            IsUpdateAvailable = true;
            Console.WriteLine($"[UpdateService] Update found: v{remoteVer} — downloading...");

            await _manager.DownloadUpdatesAsync(updateInfo, p => progress?.Report(p));

            Console.WriteLine("[UpdateService] Download complete.");
            _pendingUpdate = updateInfo;
            IsUpdateReady  = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] Check/download failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
#endif

#if __ANDROID__
    /// <summary>
    /// Calls the GitHub Releases API to check if a newer version exists.
    /// Does NOT download the APK — the user taps "Download" to open the releases page.
    /// </summary>
    private async Task<bool> CheckGitHubApiAsync()
    {
        if (string.IsNullOrWhiteSpace(_githubRepo) || _githubRepo.Contains("YOUR_USERNAME"))
        {
            Console.WriteLine("[UpdateService] GithubRepo not configured — skipping update check.");
            return false;
        }

        try
        {
            // Convert "https://github.com/owner/repo" → "https://api.github.com/repos/owner/repo/releases/latest"
            var uri = new Uri(_githubRepo.TrimEnd('/'));
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
            {
                Console.WriteLine("[UpdateService] Cannot parse owner/repo from GithubRepo URL.");
                return false;
            }
            var apiUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "CalendarApp/1.0");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[UpdateService] GitHub API returned {response.StatusCode}.");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // tag_name is typically "v1.2.3"
            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl  = root.TryGetProperty("html_url",  out var urlProp)  ? urlProp.GetString()  : null;

            if (string.IsNullOrEmpty(tagName)) return false;

            var remoteVersion = tagName.TrimStart('v');
            var currentVersion = GetCurrentVersion();

            Console.WriteLine($"[UpdateService] Current={currentVersion}  Remote={remoteVersion}");

            if (!IsNewer(remoteVersion, currentVersion)) return false;

            NewVersionString  = remoteVersion;
            IsUpdateAvailable = true;
            if (!string.IsNullOrEmpty(htmlUrl)) ReleasesPageUrl = htmlUrl;
            Console.WriteLine($"[UpdateService] Update available: v{remoteVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] GitHub API check failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsNewer(string remote, string current)
    {
        return Version.TryParse(remote, out var r) &&
               Version.TryParse(current, out var c) &&
               r > c;
    }

    private static string GetCurrentVersion()
    {
        try
        {
            var ctx  = Android.App.Application.Context;
            var info = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0);
            return info?.VersionName ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
#endif
}
