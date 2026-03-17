using CalendarApp.Services.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace CalendarApp.Services.Google;

/// <summary>
/// Google OAuth 2.0 authentication service.
/// Manages one primary (read-write) account and zero or more read-only secondary accounts.
/// </summary>
public class GoogleAuthService : IGoogleAuthService
{
    private static readonly string[] FullScopes = { CalendarService.Scope.Calendar };
    private static readonly string[] ReadOnlyScopes = { CalendarService.Scope.CalendarReadonly };
    private const string ApplicationName = "CalendarApp";

    private readonly ILogger<GoogleAuthService> _logger;
    private UserCredential? _credential;
    private CalendarService? _calendarService;

    // Read-only secondary accounts keyed by alias
    private readonly Dictionary<string, CalendarService> _readOnlyServices = new(StringComparer.OrdinalIgnoreCase);

    public GoogleAuthService(ILogger<GoogleAuthService> logger)
    {
        _logger = logger;
    }

    // ── Primary account ─────────────────────────────────────────────────────

    public bool IsSignedIn => _credential?.Token != null && !IsTokenExpired();

    public string? UserEmail { get; private set; }

    /// <summary>
    /// Attempts to silently restore a previous sign-in from stored tokens.
    /// Does not open a browser. Returns false if no stored token exists or refresh fails.
    /// </summary>
    public async Task<bool> TryRestoreSignInAsync()
    {
        if (IsSignedIn) return true;

        // Only try if the token store directory exists from a prior session.
        var tokenPath = GetTokenStorePath("primary");
        if (!Directory.Exists(tokenPath)) return false;
        if (!Directory.GetFiles(tokenPath, "*", SearchOption.AllDirectories).Any()) return false;

        try
        {
            var clientSecrets = GetClientSecrets();
            if (clientSecrets == null) return false;

            var tokenStore = new FileDataStore(tokenPath, true);
            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                FullScopes,
                "user",
                CancellationToken.None,
                tokenStore);

            if (_credential.Token.IsStale)
            {
                var refreshed = await _credential.RefreshTokenAsync(CancellationToken.None);
                if (!refreshed)
                {
                    _credential = null;
                    _calendarService = null;
                    return false;
                }
            }

            _calendarService = CreateCalendarService(_credential);
            UserEmail = await GetUserEmailAsync();
            _logger.LogInformation("Silently restored Google sign-in as {Email}", UserEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Silent Google sign-in restore failed; browser sign-in required");
            _credential = null;
            _calendarService = null;
            return false;
        }
    }

    public async Task<bool> SignInAsync()
    {
        try
        {
            var clientSecrets = GetClientSecrets();
            if (clientSecrets == null)
            {
                _logger.LogWarning("Google client secrets not configured. " +
                    "Place credentials.json in the app data folder or set environment variables " +
                    "GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET.");
                return false;
            }

            var tokenStore = new FileDataStore(GetTokenStorePath("primary"), true);

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                FullScopes,
                "user",
                CancellationToken.None,
                tokenStore);

            if (_credential.Token.IsStale)
            {
                var refreshed = await _credential.RefreshTokenAsync(CancellationToken.None);
                if (!refreshed)
                {
                    _logger.LogWarning("Failed to refresh Google token");
                    return false;
                }
            }

            _calendarService = CreateCalendarService(_credential);
            UserEmail = await GetUserEmailAsync();

            _logger.LogInformation("Signed in to Google Calendar as {Email}", UserEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign in to Google Calendar");
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (_credential != null)
            {
                await _credential.RevokeTokenAsync(CancellationToken.None);
            }

            var tokenPath = GetTokenStorePath("primary");
            if (Directory.Exists(tokenPath))
            {
                Directory.Delete(tokenPath, true);
            }

            _credential = null;
            _calendarService = null;
            UserEmail = null;

            _logger.LogInformation("Signed out of Google Calendar");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google sign out");
            _credential = null;
            _calendarService = null;
            UserEmail = null;
        }
    }

    public async Task<CalendarService?> GetCalendarServiceAsync()
    {
        if (_calendarService != null && !IsTokenExpired())
            return _calendarService;

        if (_credential == null)
        {
            var signedIn = await SignInAsync();
            if (!signedIn) return null;
        }

        if (_credential != null && IsTokenExpired())
            await _credential.RefreshTokenAsync(CancellationToken.None);

        _calendarService ??= CreateCalendarService(_credential!);
        return _calendarService;
    }

    // ── Read-only secondary accounts ─────────────────────────────────────────

    public IReadOnlyList<string> ReadOnlyAccountAliases => _readOnlyServices.Keys.ToList();

    public async Task<bool> InitializeReadOnlyAccountAsync(string alias, string credentialsFilePath)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            _logger.LogWarning("Read-only account alias must not be empty");
            return false;
        }

        try
        {
            var secrets = LoadSecretsFromFile(credentialsFilePath);
            if (secrets == null)
            {
                _logger.LogWarning("Could not load credentials for read-only account '{Alias}' from {Path}",
                    alias, credentialsFilePath);
                return false;
            }

            // Each alias gets its own token store subfolder so tokens never collide.
            var tokenStore = new FileDataStore(GetTokenStorePath(alias), true);

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                ReadOnlyScopes,
                alias,
                CancellationToken.None,
                tokenStore);

            if (credential.Token.IsStale)
                await credential.RefreshTokenAsync(CancellationToken.None);

            _readOnlyServices[alias] = CreateCalendarService(credential);
            _logger.LogInformation("Initialized read-only Google account: {Alias}", alias);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize read-only Google account '{Alias}'", alias);
            return false;
        }
    }

    public Task<CalendarService?> GetReadOnlyCalendarServiceAsync(string alias)
    {
        _readOnlyServices.TryGetValue(alias, out var svc);
        return Task.FromResult(svc);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarService CreateCalendarService(UserCredential credential)
    {
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });
    }

    private bool IsTokenExpired()
    {
        if (_credential?.Token == null) return true;
        return _credential.Token.IsStale;
    }

    private async Task<string?> GetUserEmailAsync()
    {
        try
        {
            var service = _calendarService ?? CreateCalendarService(_credential!);
            var calendar = await service.CalendarList.Get("primary").ExecuteAsync();
            return calendar?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static ClientSecrets? GetClientSecrets()
    {
        // 1. Environment variables (CI / server deployments)
        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            return new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret };

        // 2. App-data folder (copied there by EnsureCredentialsInstalledAsync on startup)
        var appDataPath = GetCredentialsFilePath();
        Console.WriteLine($"[CalendarApp] GetClientSecrets: checking AppData path: {appDataPath} (exists={File.Exists(appDataPath)})");
        var secrets = LoadSecretsFromFile(appDataPath);
        if (secrets != null) return secrets;

#if __SKIA__
        // 3. Desktop fallback: read directly from the app's install/run directory.
        //    Velopack installs the app with credentials.json alongside the executable;
        //    if the AppData copy hasn't happened yet, this keeps sign-in working.
        var baseDir = AppContext.BaseDirectory;
        var baseDirPath = Path.Combine(baseDir, "credentials.json");
        Console.WriteLine($"[CalendarApp] GetClientSecrets: checking BaseDir path: {baseDirPath} (exists={File.Exists(baseDirPath)})");
        var baseDirSecrets = LoadSecretsFromFile(baseDirPath);
        if (baseDirSecrets != null) return baseDirSecrets;
#endif

#if __ANDROID__
        // 4. Android fallback: read directly from APK assets in case the file-copy step failed.
        try
        {
            using var stream = Android.App.Application.Context.Assets!.Open("credentials.json");
            return GoogleClientSecrets.FromStream(stream).Secrets;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Could not read credentials from Android assets: {ex.Message}");
        }
#endif

        return null;
    }

    private static ClientSecrets? LoadSecretsFromFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return GoogleClientSecrets.FromStream(stream).Secrets;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCredentialsFilePath()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(folder, "CalendarApp", "credentials.json");
    }

    /// <summary>
    /// Token-store folder for the given account key.
    /// Primary account = "primary"; read-only accounts use their alias.
    /// </summary>
    private static string GetTokenStorePath(string accountKey)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(folder, "CalendarApp", "tokens", accountKey);
    }
}
