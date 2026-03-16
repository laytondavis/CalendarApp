namespace CalendarApp.Services.Interfaces;

public interface IUpdateService
{
    /// <summary>
    /// True when a newer version has been found (on any platform).
    /// On desktop this also means the update has been downloaded by Velopack.
    /// On Android this means the GitHub releases page is available for the user to download from.
    /// </summary>
    bool IsUpdateAvailable { get; }

    /// <summary>
    /// True only on desktop after Velopack has fully downloaded the update and it is
    /// ready to apply immediately via <see cref="ApplyAndRestart"/>.
    /// Always false on Android.
    /// </summary>
    bool IsUpdateReady { get; }

    /// <summary>The version string of the newer release (e.g. "1.2.3"), or null if none found.</summary>
    string? NewVersionString { get; }

    /// <summary>
    /// The GitHub releases page URL for this app.
    /// Used on Android so the user can open the browser and download the APK manually.
    /// </summary>
    string? ReleasesPageUrl { get; }

    /// <summary>
    /// Checks for a newer version and, if found, downloads it (desktop) or records its
    /// availability (Android/other).
    /// Reports download progress (0–100) via <paramref name="progress"/> during the
    /// Velopack download phase on desktop.
    /// Returns true if an update was found.
    /// </summary>
    Task<bool> CheckAndDownloadAsync(IProgress<int>? progress = null);

    /// <summary>
    /// Applies the downloaded update and restarts the application immediately.
    /// Only effective on desktop when <see cref="IsUpdateReady"/> is true.
    /// No-op on Android/other platforms.
    /// </summary>
    void ApplyAndRestart();
}
