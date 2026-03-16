using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for managing Google OAuth 2.0 authentication.
/// Supports one primary (read-write) account and zero or more secondary read-only accounts
/// loaded from the AppConfig.GoogleAccounts configuration list.
/// </summary>
public interface IGoogleAuthService
{
    // ── Primary (writable) account ──────────────────────────────────────────

    /// <summary>
    /// Gets whether the primary account is currently signed in.
    /// </summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Gets the primary account's email address.
    /// </summary>
    string? UserEmail { get; }

    /// <summary>
    /// Silently restores a previous sign-in session from stored tokens without
    /// opening a browser. Returns true if the stored credential is still valid.
    /// </summary>
    Task<bool> TryRestoreSignInAsync();

    /// <summary>
    /// Signs in to Google Calendar using OAuth 2.0 for the primary account.
    /// Opens browser for consent if no stored credentials.
    /// </summary>
    Task<bool> SignInAsync();

    /// <summary>
    /// Signs out the primary account and revokes its stored credential.
    /// </summary>
    Task SignOutAsync();

    /// <summary>
    /// Gets an authenticated CalendarService instance for the primary (writable) account.
    /// </summary>
    Task<CalendarService?> GetCalendarServiceAsync();

    // ── Read-only secondary accounts ────────────────────────────────────────

    /// <summary>
    /// Gets the aliases of all successfully initialized read-only accounts.
    /// </summary>
    IReadOnlyList<string> ReadOnlyAccountAliases { get; }

    /// <summary>
    /// Loads OAuth credentials for a read-only account from a credentials file.
    /// Uses CalendarReadonly scope so it cannot modify remote events.
    /// Returns true if the account was authenticated successfully.
    /// </summary>
    Task<bool> InitializeReadOnlyAccountAsync(string alias, string credentialsFilePath);

    /// <summary>
    /// Gets a CalendarService for the specified read-only account alias, or null
    /// if the account has not been initialized.
    /// </summary>
    Task<CalendarService?> GetReadOnlyCalendarServiceAsync(string alias);
}
