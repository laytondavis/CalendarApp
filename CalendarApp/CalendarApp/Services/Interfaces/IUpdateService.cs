namespace CalendarApp.Services.Interfaces;

public interface IUpdateService
{
    /// <summary>
    /// True when a newer version has been found (on any platform).
    /// </summary>
    bool IsUpdateAvailable { get; }

    /// <summary>
    /// True when the update has been downloaded and is ready to install.
    /// On desktop: Velopack has downloaded the update.
    /// On Android: the APK has been downloaded to local storage.
    /// </summary>
    bool IsUpdateReady { get; }

    /// <summary>The version string of the newer release (e.g. "1.2.3"), or null if none found.</summary>
    string? NewVersionString { get; }

    /// <summary>
    /// The GitHub releases page URL for this app (fallback for manual download).
    /// </summary>
    string? ReleasesPageUrl { get; }

    /// <summary>
    /// True when the app was installed from an app store (Google Play, etc.)
    /// and in-app update checking should be disabled.
    /// </summary>
    bool IsStoreInstall { get; }

    /// <summary>
    /// Checks for a newer version and, if found, downloads it.
    /// On desktop: Velopack downloads the update.
    /// On Android: downloads the APK file in the background.
    /// Reports download progress (0–100) via <paramref name="progress"/>.
    /// Returns true if an update was found and downloaded.
    /// </summary>
    Task<bool> CheckAndDownloadAsync(IProgress<int>? progress = null);

    /// <summary>
    /// Applies the downloaded update.
    /// On desktop: restarts the application with the new version.
    /// On Android: launches the system package installer for the downloaded APK.
    /// </summary>
    void ApplyAndRestart();

    /// <summary>
    /// Starts the periodic background update checker.
    /// First check after <paramref name="initialDelay"/>, then every <paramref name="interval"/>.
    /// No-op if the app was installed from a store.
    /// </summary>
    void StartPeriodicChecks(TimeSpan initialDelay, TimeSpan interval);

    /// <summary>
    /// Stops the periodic background update checker.
    /// </summary>
    void StopPeriodicChecks();
}
