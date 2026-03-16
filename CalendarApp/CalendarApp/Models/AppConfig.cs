namespace CalendarApp.Models;

public record AppConfig
{
    public string? Environment { get; init; }

    /// <summary>
    /// GitHub repository URL used for auto-update checks (Velopack).
    /// Format: https://github.com/owner/repo
    /// Leave empty to disable update checking.
    /// </summary>
    public string? GithubRepo { get; init; }

    /// <summary>
    /// Additional Google accounts to connect to in read-only mode.
    /// The primary account (credentials.json / env vars) is always the writable account.
    /// Each entry here adds a read-only pull-only connection.
    /// </summary>
    public IReadOnlyList<GoogleAccountConfig> GoogleAccounts { get; init; } = Array.Empty<GoogleAccountConfig>();
}

/// <summary>
/// Configuration for a secondary Google Calendar account.
/// </summary>
public record GoogleAccountConfig
{
    /// <summary>
    /// Short label used to distinguish this account in logs and token storage.
    /// Must be unique across all configured accounts.
    /// </summary>
    public string Alias { get; init; } = string.Empty;

    /// <summary>
    /// Path to the OAuth credentials JSON file for this account.
    /// Relative paths are resolved from the app's LocalApplicationData/CalendarApp folder.
    /// </summary>
    public string? CredentialsFile { get; init; }

    /// <summary>
    /// When true (default) the account is only used for downloading events.
    /// Local events are never pushed to read-only accounts.
    /// </summary>
    public bool ReadOnly { get; init; } = true;
}
