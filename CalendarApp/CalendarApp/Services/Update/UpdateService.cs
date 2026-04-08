using System.Text.Json;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using Microsoft.Extensions.Options;
#if !__ANDROID__
using Velopack;
using Velopack.Sources;
#endif

namespace CalendarApp.Services.Update;

public class UpdateService : IUpdateService
{
    private readonly string _githubRepo;
    private readonly HttpClient _httpClient;
    private Timer? _periodicTimer;

#if !__ANDROID__
    private UpdateInfo? _pendingUpdate;
    private UpdateManager? _manager;
#endif

#if __ANDROID__
    private string? _downloadedApkPath;
#endif

    public bool IsUpdateAvailable { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public string? NewVersionString { get; private set; }
    public string? ReleasesPageUrl { get; private set; }
    public bool IsStoreInstall { get; }

    public UpdateService(IOptions<AppConfig> appConfig, HttpClient httpClient)
    {
        _githubRepo = appConfig.Value.GithubRepo ?? string.Empty;
        _httpClient = httpClient;

        ReleasesPageUrl = string.IsNullOrWhiteSpace(_githubRepo)
            ? null
            : _githubRepo.TrimEnd('/') + "/releases/latest";

#if __ANDROID__
        IsStoreInstall = DetectStoreInstall();
        Console.WriteLine($"[UpdateService] IsStoreInstall={IsStoreInstall}");
#else
        IsStoreInstall = false;
#endif
    }

    // ── Periodic checks ─────────────────────────────────────────────────────

    public void StartPeriodicChecks(TimeSpan initialDelay, TimeSpan interval)
    {
        if (IsStoreInstall)
        {
            Console.WriteLine("[UpdateService] Store install detected — periodic checks disabled.");
            return;
        }

        StopPeriodicChecks();
        Console.WriteLine($"[UpdateService] Periodic checks: first in {initialDelay.TotalMinutes:F0}m, then every {interval.TotalHours:F0}h");
        _periodicTimer = new Timer(async _ =>
        {
            try
            {
                Console.WriteLine("[UpdateService] Periodic update check starting...");
                var found = await CheckAndDownloadAsync();
                Console.WriteLine($"[UpdateService] Periodic check result: {(found ? "update found" : "up to date")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Periodic check failed: {ex.Message}");
            }
        }, null, initialDelay, interval);
    }

    public void StopPeriodicChecks()
    {
        _periodicTimer?.Dispose();
        _periodicTimer = null;
    }

    // ── Check & download ────────────────────────────────────────────────────

    public async Task<bool> CheckAndDownloadAsync(IProgress<int>? progress = null)
    {
#if __ANDROID__
        return await CheckAndDownloadApkAsync(progress);
#else
        return await CheckVelopackAsync(progress);
#endif
    }

    // ── Apply / install ─────────────────────────────────────────────────────

    public void ApplyAndRestart()
    {
#if __ANDROID__
        InstallApk();
#else
        ApplyVelopack();
#endif
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ANDROID
    // ═══════════════════════════════════════════════════════════════════════

#if __ANDROID__

    private static bool DetectStoreInstall()
    {
        try
        {
            var ctx = Android.App.Application.Context;
            string? installer;

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R) // API 30+
            {
                var sourceInfo = ctx.PackageManager!.GetInstallSourceInfo(ctx.PackageName!);
                installer = sourceInfo.InstallingPackageName;
            }
            else
            {
#pragma warning disable CA1422 // Obsolete on API 30+, but we only call it on older APIs
                installer = ctx.PackageManager?.GetInstallerPackageName(ctx.PackageName!);
#pragma warning restore CA1422
            }

            // Play Store = "com.android.vending", Samsung = "com.sec.android.app.samsungapps", etc.
            // Sideloaded APKs have null or "com.google.android.packageinstaller"
            var isStore = installer is "com.android.vending"
                                   or "com.sec.android.app.samsungapps"
                                   or "com.amazon.venezia";
            Console.WriteLine($"[UpdateService] Installer package: {installer ?? "null"} → isStore={isStore}");
            return isStore;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] DetectStoreInstall error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CheckAndDownloadApkAsync(IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(_githubRepo) || _githubRepo.Contains("YOUR_USERNAME"))
        {
            Console.WriteLine("[UpdateService] GithubRepo not configured — skipping update check.");
            return false;
        }

        try
        {
            // ── 1. Query GitHub releases API ─────────────────────────────
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

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;

            if (string.IsNullOrEmpty(tagName)) return false;

            var remoteVersion = tagName.TrimStart('v');
            var currentVersion = GetCurrentVersion();
            Console.WriteLine($"[UpdateService] Current={currentVersion}  Remote={remoteVersion}");

            if (!IsNewer(remoteVersion, currentVersion)) return false;

            NewVersionString = remoteVersion;
            IsUpdateAvailable = true;
            if (!string.IsNullOrEmpty(htmlUrl)) ReleasesPageUrl = htmlUrl;

            // ── 2. Find the APK asset download URL ───────────────────────
            string? apkUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (name != null && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    {
                        apkUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() : null;
                        Console.WriteLine($"[UpdateService] Found APK asset: {name} → {apkUrl}");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(apkUrl))
            {
                Console.WriteLine("[UpdateService] No APK asset found in release — update available but cannot auto-download.");
                return true; // update exists but no APK to download
            }

            // ── 3. Download the APK ──────────────────────────────────────
            Console.WriteLine($"[UpdateService] Downloading APK from: {apkUrl}");
            progress?.Report(0);

            using var apkResponse = await _httpClient.GetAsync(apkUrl, HttpCompletionOption.ResponseHeadersRead);
            apkResponse.EnsureSuccessStatusCode();

            var totalBytes = apkResponse.Content.Headers.ContentLength ?? -1;
            var downloadDir = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads);
            var apkPath = System.IO.Path.Combine(downloadDir!.AbsolutePath, $"CalendarApp-{remoteVersion}.apk");

            // Clean up old APK files
            foreach (var oldApk in System.IO.Directory.GetFiles(downloadDir.AbsolutePath, "CalendarApp-*.apk"))
            {
                try { System.IO.File.Delete(oldApk); }
                catch { /* ignore */ }
            }

            using (var contentStream = await apkResponse.Content.ReadAsStreamAsync())
            using (var fileStream = new System.IO.FileStream(apkPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 8192))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                    {
                        var pct = (int)(totalRead * 100 / totalBytes);
                        progress?.Report(pct);
                    }
                }
            }

            progress?.Report(100);
            Console.WriteLine($"[UpdateService] APK downloaded to: {apkPath} ({new System.IO.FileInfo(apkPath).Length} bytes)");

            _downloadedApkPath = apkPath;
            IsUpdateReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] CheckAndDownloadApk failed: {ex.Message}");
            return false;
        }
    }

    private void InstallApk()
    {
        if (string.IsNullOrEmpty(_downloadedApkPath) || !System.IO.File.Exists(_downloadedApkPath))
        {
            Console.WriteLine("[UpdateService] InstallApk: no downloaded APK found.");
            return;
        }

        try
        {
            var ctx = Android.App.Application.Context;
            var apkFile = new Java.IO.File(_downloadedApkPath);

            // Use FileProvider to get a content:// URI (required for API 24+)
            var authority = ctx.PackageName + ".fileprovider";
            var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, authority, apkFile);

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);

            Console.WriteLine($"[UpdateService] Launching installer for: {_downloadedApkPath}");
            ctx.StartActivity(intent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateService] InstallApk failed: {ex.Message}");
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
            var ctx = Android.App.Application.Context;
            var info = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0);
            return info?.VersionName ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

#endif

    // ═══════════════════════════════════════════════════════════════════════
    //  DESKTOP (Velopack)
    // ═══════════════════════════════════════════════════════════════════════

#if !__ANDROID__

    private void ApplyVelopack()
    {
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
    }

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
            Console.WriteLine($"[UpdateService] CheckForUpdatesAsync returned: {(updateInfo == null ? "null (no update)" : "UpdateInfo (update available)")}");

            if (updateInfo == null)
            {
                Console.WriteLine($"[UpdateService] No update available – you are up to date.");
                return false;
            }

            var currentVer2 = _manager.CurrentVersion?.ToString() ?? "unknown";
            var remoteVer = updateInfo.TargetFullRelease.Version.ToString();
            Console.WriteLine($"[UpdateService] Current={currentVer2}  Remote={remoteVer}");

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
            Console.WriteLine($"[UpdateService] Check/download failed: {ex.Message}");
            Console.WriteLine($"[UpdateService] Stack trace: {ex.StackTrace}");
            return false;
        }
    }

#endif
}
