using CalendarApp;
using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using Google.Apis.Calendar.v3;
using Microsoft.Extensions.Logging;

namespace CalendarApp.Services.Google;

/// <summary>
/// Two-way sync service between local SQLite storage and Google Calendar.
/// Uses sync tokens for incremental sync and ETag-based conflict detection.
/// </summary>
public class SyncService : ISyncService
{
    private readonly IGoogleAuthService _authService;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IEventRepository _eventRepository;
    private readonly CalendarDbContext _dbContext;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        IGoogleAuthService authService,
        IGoogleCalendarService googleCalendarService,
        IEventRepository eventRepository,
        CalendarDbContext dbContext,
        ILogger<SyncService> logger)
    {
        _authService = authService;
        _googleCalendarService = googleCalendarService;
        _eventRepository = eventRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public bool IsSyncing { get; private set; }
    public DateTime? LastSyncTimeUtc { get; private set; }

    public event EventHandler<SyncStatusChangedEventArgs>? SyncStatusChanged;

    /// <summary>
    /// Performs a full two-way sync:
    /// 1. Push local changes to Google
    /// 2. Pull remote changes from Google
    /// 3. Detect and flag conflicts
    /// </summary>
    public async Task<SyncResult> SyncAsync(string calendarId = "primary")
    {
        if (IsSyncing) return new SyncResult { Success = false, Errors = { "Sync already in progress" } };

        var hasReadOnly = _authService.ReadOnlyAccountAliases.Count > 0;
        if (!_authService.IsSignedIn && !hasReadOnly)
            return new SyncResult { Success = false, Errors = { "Not signed in" } };

        IsSyncing = true;
        var result = new SyncResult();

        try
        {
            await _dbContext.InitializeAsync();

            // ── Primary account: two-way sync ────────────────────────────────
            if (_authService.IsSignedIn)
            {
                OnStatusChanged("Pushing local changes...");
                var pushResult = await PushLocalChangesAsync(calendarId);
                result.EventsUploaded = pushResult.EventsUploaded;
                result.Errors.AddRange(pushResult.Errors);

                OnStatusChanged("Pulling remote changes...");
                var pullResult = await PullRemoteChangesAsync(calendarId);
                result.EventsDownloaded += pullResult.EventsDownloaded;
                result.EventsDeleted += pullResult.EventsDeleted;
                result.Conflicts += pullResult.Conflicts;
                result.Errors.AddRange(pullResult.Errors);

                // ── Pull enabled non-primary calendars (read-only) ────────────
                var extraCalendars = await _dbContext.Connection
                    .Table<GoogleCalendarListEntity>()
                    .Where(c => c.IsEnabled && !c.IsPrimary)
                    .ToListAsync();

                foreach (var cal in extraCalendars)
                {
                    OnStatusChanged($"Pulling '{cal.Summary}'...");
                    var extraPull = await PullRemoteChangesAsync(cal.CalendarId);
                    result.EventsDownloaded += extraPull.EventsDownloaded;
                    result.EventsDeleted    += extraPull.EventsDeleted;
                    result.Conflicts        += extraPull.Conflicts;
                    result.Errors.AddRange(extraPull.Errors);
                }
            }

            // ── Read-only accounts: pull only ─────────────────────────────────
            foreach (var alias in _authService.ReadOnlyAccountAliases)
            {
                var roService = await _authService.GetReadOnlyCalendarServiceAsync(alias);
                if (roService == null) continue;

                OnStatusChanged($"Pulling from {alias}...");
                var roPullResult = await PullFromReadOnlyAccountAsync(alias, calendarId, roService);
                result.EventsDownloaded += roPullResult.EventsDownloaded;
                result.EventsDeleted += roPullResult.EventsDeleted;
                result.Errors.AddRange(roPullResult.Errors);
            }

            result.Success = result.Errors.Count == 0;
            LastSyncTimeUtc = DateTime.UtcNow;

            OnStatusChanged($"Sync complete. {result.Summary}");
            _logger.LogInformation("Sync complete: {Summary}", result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            result.Errors.Add(ex.Message);
            OnStatusChanged($"Sync failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsSyncing = false;
        }

        return result;
    }

    /// <summary>
    /// Push local pending changes (new, updated, deleted) to Google.
    /// </summary>
    public async Task<SyncResult> PushLocalChangesAsync(string calendarId = "primary")
    {
        var result = new SyncResult();

        try
        {
            var pendingEvents = (await _eventRepository.GetPendingSyncEventsAsync()).ToList();

            foreach (var localEvent in pendingEvents)
            {
                try
                {
                    if (localEvent.IsDeleted)
                    {
                        // Delete from Google
                        if (!string.IsNullOrEmpty(localEvent.GoogleEventId))
                        {
                            var deleted = await _googleCalendarService.DeleteEventAsync(localEvent.GoogleEventId, calendarId);
                            if (deleted)
                            {
                                await _eventRepository.DeleteAsync(localEvent.Id);
                                result.EventsDeleted++;
                            }
                        }
                        else
                        {
                            // Never synced, just delete locally
                            await _eventRepository.DeleteAsync(localEvent.Id);
                        }
                    }
                    else if (string.IsNullOrEmpty(localEvent.GoogleEventId))
                    {
                        // Re-read from DB to guard against race: another code path
                        // (e.g. EventEditorViewModel direct push) may have already
                        // pushed this event and marked it Synced with a GoogleEventId.
                        var fresh = await _eventRepository.GetByIdAsync(localEvent.Id);
                        if (fresh == null || fresh.SyncStatus != SyncStatus.PendingUpload
                            || !string.IsNullOrEmpty(fresh.GoogleEventId))
                        {
                            continue;
                        }

                        // New local event - create on Google
                        // Use the event's own CalendarId if set (e.g. user picked a
                        // specific calendar in the editor), otherwise fall back to the
                        // calendarId parameter (typically "primary").
                        var targetCalId = !string.IsNullOrEmpty(localEvent.CalendarId)
                            ? localEvent.CalendarId : calendarId;
                        var createResult = await _googleCalendarService.CreateEventAsync(localEvent, targetCalId);
                        if (createResult.Success)
                        {
                            localEvent.GoogleEventId = createResult.GoogleEventId!;
                            localEvent.ETag = createResult.ETag!;
                            localEvent.SyncStatus = SyncStatus.Synced;
                            localEvent.CalendarId = targetCalId;
                            await _eventRepository.UpdateAsync(localEvent);
                            result.EventsUploaded++;
                        }
                        else
                        {
                            await _eventRepository.UpdateSyncStatusAsync(localEvent.Id, SyncStatus.Error);
                            result.Errors.Add($"Failed to create '{localEvent.Title}': {createResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        // Updated local event - update on Google
                        var updateResult = await _googleCalendarService.UpdateEventAsync(localEvent, calendarId);
                        if (updateResult.Success)
                        {
                            localEvent.ETag = updateResult.ETag!;
                            localEvent.SyncStatus = SyncStatus.Synced;
                            await _eventRepository.UpdateAsync(localEvent);
                            result.EventsUploaded++;
                        }
                        else if (updateResult.ErrorMessage?.Contains("Conflict") == true)
                        {
                            await _eventRepository.UpdateSyncStatusAsync(localEvent.Id, SyncStatus.Conflict);
                            result.Conflicts++;
                        }
                        else
                        {
                            await _eventRepository.UpdateSyncStatusAsync(localEvent.Id, SyncStatus.Error);
                            result.Errors.Add($"Failed to update '{localEvent.Title}': {updateResult.ErrorMessage}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error pushing event {EventId}", localEvent.Id);
                    result.Errors.Add($"Error syncing '{localEvent.Title}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing local changes");
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Pull changes from Google using incremental sync (sync tokens).
    /// </summary>
    public Task<SyncResult> PullRemoteChangesAsync(string calendarId = "primary")
        => PullRemoteChangesAsyncCore(calendarId, forceOverwrite: false);

    private async Task<SyncResult> PullRemoteChangesAsyncCore(string calendarId, bool forceOverwrite)
    {
        var result = new SyncResult();

        try
        {
            // Get stored sync token
            var syncState = await GetSyncStateAsync(calendarId);
            var syncToken = syncState?.SyncToken;

            SyncDiagnosticLog.Write(
                $"PullRemote [{calendarId}]: requesting Google events" +
                $" (syncToken={(!string.IsNullOrEmpty(syncToken) ? "present" : "null")}" +
                $", forceOverwrite={forceOverwrite})");

            var googleResult = await _googleCalendarService.GetEventsAsync(
                calendarId,
                syncToken: string.IsNullOrEmpty(syncToken) ? null : syncToken);

            SyncDiagnosticLog.Write(
                $"PullRemote [{calendarId}]: Google responded — success={googleResult.Success}" +
                $", events={googleResult.Events.Count()}" +
                $", deletions={googleResult.DeletedEventIds.Count}" +
                $", isFullSync={googleResult.IsFullSync}" +
                $", error={googleResult.ErrorMessage}");

            if (!googleResult.Success)
            {
                result.Errors.Add(googleResult.ErrorMessage ?? "Unknown error pulling events");
                return result;
            }

            // Process downloaded events
            foreach (var remoteEvent in googleResult.Events)
            {
                try
                {
                    var existingLocal = await _eventRepository.GetByGoogleIdAsync(remoteEvent.GoogleEventId);

                    if (existingLocal == null)
                    {
                        // New remote event - insert locally
                        remoteEvent.CalendarId = calendarId;
                        remoteEvent.SyncStatus = SyncStatus.Synced;
                        await _eventRepository.InsertAsync(remoteEvent);
                        result.EventsDownloaded++;
                        SyncDiagnosticLog.Write($"PullRemote: inserted new event '{remoteEvent.Title}' (gId={remoteEvent.GoogleEventId})");
                    }
                    else
                    {
                        // Existing event - check for conflicts
                        if (!forceOverwrite && existingLocal.SyncStatus == SyncStatus.PendingUpload)
                        {
                            // Local has unsaved changes AND remote changed - conflict
                            if (existingLocal.LastModifiedUtc < remoteEvent.LastModifiedUtc)
                            {
                                existingLocal.SyncStatus = SyncStatus.Conflict;
                                await _eventRepository.UpdateAsync(existingLocal);
                                result.Conflicts++;
                            }
                            // else local is newer, will be pushed next cycle
                        }
                        else
                        {
                            // No local changes (or force overwrite) - safe to overwrite with remote data
                            remoteEvent.Id = existingLocal.Id;
                            remoteEvent.CalendarId = calendarId;
                            remoteEvent.SyncStatus = SyncStatus.Synced;
                            await _eventRepository.UpdateAsync(remoteEvent);
                            result.EventsDownloaded++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing remote event {GoogleEventId}", remoteEvent.GoogleEventId);
                    result.Errors.Add($"Error processing '{remoteEvent.Title}': {ex.Message}");
                }
            }

            // Process deleted events
            foreach (var deletedId in googleResult.DeletedEventIds)
            {
                try
                {
                    var existingLocal = await _eventRepository.GetByGoogleIdAsync(deletedId);
                    SyncDiagnosticLog.Write(
                        $"PullRemote: deletion gId={deletedId}" +
                        $" — foundLocally={existingLocal != null}" +
                        $" status={existingLocal?.SyncStatus}");

                    if (existingLocal != null)
                    {
                        if (!forceOverwrite && existingLocal.SyncStatus == SyncStatus.PendingUpload)
                        {
                            // Conflict: locally modified but remotely deleted
                            existingLocal.SyncStatus = SyncStatus.Conflict;
                            await _eventRepository.UpdateAsync(existingLocal);
                            result.Conflicts++;
                            SyncDiagnosticLog.Write($"PullRemote: deletion conflict for '{existingLocal.Title}'");
                        }
                        else
                        {
                            await _eventRepository.DeleteAsync(existingLocal.Id);
                            result.EventsDeleted++;
                            SyncDiagnosticLog.Write($"PullRemote: deleted '{existingLocal.Title}' locally");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing deleted event {GoogleEventId}", deletedId);
                    SyncDiagnosticLog.Write($"PullRemote: exception processing deletion gId={deletedId} — {ex.Message}");
                }
            }

            // Save sync token for next incremental sync
            if (!string.IsNullOrEmpty(googleResult.NextSyncToken))
            {
                await SaveSyncStateAsync(calendarId, googleResult.NextSyncToken);
            }

            result.Success = result.Errors.Count == 0;
            SyncDiagnosticLog.Write(
                $"PullRemote [{calendarId}]: finished — downloaded={result.EventsDownloaded}" +
                $", deleted={result.EventsDeleted}, conflicts={result.Conflicts}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling remote changes");
            SyncDiagnosticLog.Write($"PullRemote [{calendarId}]: exception — {ex.Message}");
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Resolve a sync conflict for a specific event.
    /// </summary>
    public async Task ResolveConflictAsync(int localEventId, ConflictResolution resolution)
    {
        var localEvent = await _eventRepository.GetByIdAsync(localEventId);
        if (localEvent == null || localEvent.SyncStatus != SyncStatus.Conflict) return;

        switch (resolution)
        {
            case ConflictResolution.KeepLocal:
                // Mark as pending upload to push local version
                localEvent.SyncStatus = SyncStatus.PendingUpload;
                await _eventRepository.UpdateAsync(localEvent);
                break;

            case ConflictResolution.KeepRemote:
                // Re-download from Google
                if (!string.IsNullOrEmpty(localEvent.GoogleEventId))
                {
                    var googleResult = await _googleCalendarService.GetEventsAsync(
                        localEvent.CalendarId);
                    var remoteVersion = googleResult.Events
                        .FirstOrDefault(e => e.GoogleEventId == localEvent.GoogleEventId);

                    if (remoteVersion != null)
                    {
                        remoteVersion.Id = localEvent.Id;
                        remoteVersion.SyncStatus = SyncStatus.Synced;
                        await _eventRepository.UpdateAsync(remoteVersion);
                    }
                }
                break;

            case ConflictResolution.KeepNewest:
                // Compare timestamps and keep whichever is newer
                var service = await _authService.GetCalendarServiceAsync();
                if (service != null && !string.IsNullOrEmpty(localEvent.GoogleEventId))
                {
                    try
                    {
                        var remoteEvent = await service.Events
                            .Get(localEvent.CalendarId, localEvent.GoogleEventId)
                            .ExecuteAsync();

                        var remoteModified = remoteEvent.Updated ?? DateTime.MinValue;
                        if (remoteModified > localEvent.LastModifiedUtc)
                        {
                            await ResolveConflictAsync(localEventId, ConflictResolution.KeepRemote);
                        }
                        else
                        {
                            await ResolveConflictAsync(localEventId, ConflictResolution.KeepLocal);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error resolving conflict for event {EventId}", localEventId);
                        // Default to keeping local
                        await ResolveConflictAsync(localEventId, ConflictResolution.KeepLocal);
                    }
                }
                break;
        }
    }

    // ========== Calendar List Refresh ==========

    /// <summary>
    /// Fetches the list of Google calendars for the signed-in user and upserts
    /// them into the local database. Existing IsEnabled choices are preserved.
    /// Newly discovered calendars default to enabled=true for primary, false for all others.
    /// </summary>
    public async Task RefreshCalendarListAsync()
    {
        if (!_authService.IsSignedIn) return;

        try
        {
            await _dbContext.InitializeAsync();
            var calendars = (await _googleCalendarService.GetCalendarListAsync()).ToList();

            foreach (var cal in calendars)
            {
                var existing = await _dbContext.Connection
                    .Table<GoogleCalendarListEntity>()
                    .Where(e => e.CalendarId == cal.Id)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    await _dbContext.Connection.InsertAsync(new GoogleCalendarListEntity
                    {
                        CalendarId = cal.Id,
                        Summary    = cal.Summary,
                        ColorHex   = cal.ColorHex,
                        IsPrimary  = cal.IsPrimary,
                        AccessRole = cal.AccessRole,
                        IsEnabled  = cal.IsPrimary   // only primary enabled by default
                    });
                }
                else
                {
                    // Refresh metadata but preserve the user's IsEnabled choice
                    existing.Summary    = cal.Summary;
                    existing.ColorHex   = cal.ColorHex;
                    existing.IsPrimary  = cal.IsPrimary;
                    existing.AccessRole = cal.AccessRole;
                    await _dbContext.Connection.UpdateAsync(existing);
                }
            }

            _logger.LogInformation("Calendar list refreshed: {Count} calendars", calendars.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing calendar list");
        }
    }

    // ========== Read-Only Account Pull ==========

    /// <summary>
    /// Pulls events from a read-only secondary Google account (download only, no push).
    /// Uses a separate sync-state key of the form "alias:calendarId" to keep tokens
    /// isolated from the primary account.
    /// Events are tagged with GoogleAccountAlias so they can be identified later.
    /// </summary>
    private async Task<SyncResult> PullFromReadOnlyAccountAsync(
        string alias,
        string calendarId,
        CalendarService roService)
    {
        var result = new SyncResult();

        try
        {
            var syncStateKey = $"{alias}:{calendarId}";
            var syncState = await GetSyncStateAsync(syncStateKey);
            var syncToken = syncState?.SyncToken;

            var googleResult = await _googleCalendarService.GetEventsFromServiceAsync(
                roService, calendarId,
                syncToken: string.IsNullOrEmpty(syncToken) ? null : syncToken);

            if (!googleResult.Success)
            {
                result.Errors.Add($"[{alias}] {googleResult.ErrorMessage ?? "Unknown error"}");
                return result;
            }

            foreach (var remoteEvent in googleResult.Events)
            {
                try
                {
                    remoteEvent.GoogleAccountAlias = alias;

                    var existingLocal = await _eventRepository.GetByGoogleIdAsync(remoteEvent.GoogleEventId);
                    if (existingLocal == null)
                    {
                        remoteEvent.CalendarId = calendarId;
                        remoteEvent.SyncStatus = SyncStatus.Synced;
                        await _eventRepository.InsertAsync(remoteEvent);
                        result.EventsDownloaded++;
                    }
                    else if (existingLocal.SyncStatus != SyncStatus.PendingUpload)
                    {
                        // Safe to overwrite — no local pending changes
                        remoteEvent.Id = existingLocal.Id;
                        remoteEvent.CalendarId = calendarId;
                        remoteEvent.SyncStatus = SyncStatus.Synced;
                        await _eventRepository.UpdateAsync(remoteEvent);
                        result.EventsDownloaded++;
                    }
                    // If local has pending upload: skip — we never push to read-only accounts
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Alias}] Error processing remote event {GoogleEventId}", alias, remoteEvent.GoogleEventId);
                    result.Errors.Add($"[{alias}] Error processing '{remoteEvent.Title}': {ex.Message}");
                }
            }

            foreach (var deletedId in googleResult.DeletedEventIds)
            {
                try
                {
                    var existingLocal = await _eventRepository.GetByGoogleIdAsync(deletedId);
                    if (existingLocal != null && existingLocal.GoogleAccountAlias == alias)
                    {
                        await _eventRepository.DeleteAsync(existingLocal.Id);
                        result.EventsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Alias}] Error processing deleted event {GoogleEventId}", alias, deletedId);
                }
            }

            if (!string.IsNullOrEmpty(googleResult.NextSyncToken))
                await SaveSyncStateAsync(syncStateKey, googleResult.NextSyncToken);

            result.Success = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling from read-only account '{Alias}'", alias);
            result.Errors.Add($"[{alias}] {ex.Message}");
        }

        return result;
    }

    // ========== Force Full Pull ==========

    public async Task<SyncResult> ForceFullPullAsync(string calendarId = "primary")
    {
        if (IsSyncing) return new SyncResult { Success = false, Errors = { "Sync already in progress" } };

        IsSyncing = true;
        var result = new SyncResult();

        try
        {
            await _dbContext.InitializeAsync();

            // Delete all Google-sourced events so stale occurrence records don't persist.
            await _eventRepository.DeleteGoogleEventsAsync();

            // Clear ALL stored sync tokens so every calendar gets a full (non-incremental) pull.
            var allSyncStates = await _dbContext.Connection.Table<SyncStateEntity>().ToListAsync();
            foreach (var ss in allSyncStates)
                await _dbContext.Connection.DeleteAsync(ss);

            OnStatusChanged("Starting full re-pull from Google…");

            if (_authService.IsSignedIn)
            {
                // Pull primary calendar
                var primaryResult = await PullRemoteChangesAsyncCore(calendarId, forceOverwrite: true);
                result.EventsDownloaded += primaryResult.EventsDownloaded;
                result.EventsDeleted    += primaryResult.EventsDeleted;
                result.Errors.AddRange(primaryResult.Errors);

                // Pull all enabled non-primary calendars
                var extraCalendars = await _dbContext.Connection
                    .Table<GoogleCalendarListEntity>()
                    .Where(c => c.IsEnabled && !c.IsPrimary)
                    .ToListAsync();

                foreach (var cal in extraCalendars)
                {
                    OnStatusChanged($"Pulling '{cal.Summary}'…");
                    var extraResult = await PullRemoteChangesAsyncCore(cal.CalendarId, forceOverwrite: true);
                    result.EventsDownloaded += extraResult.EventsDownloaded;
                    result.EventsDeleted    += extraResult.EventsDeleted;
                    result.Errors.AddRange(extraResult.Errors);
                }
            }

            // Pull from read-only accounts
            foreach (var alias in _authService.ReadOnlyAccountAliases)
            {
                var roService = await _authService.GetReadOnlyCalendarServiceAsync(alias);
                if (roService == null) continue;

                OnStatusChanged($"Pulling from {alias}…");
                var roResult = await PullFromReadOnlyAccountAsync(alias, calendarId, roService);
                result.EventsDownloaded += roResult.EventsDownloaded;
                result.EventsDeleted    += roResult.EventsDeleted;
                result.Errors.AddRange(roResult.Errors);
            }

            result.Success = result.Errors.Count == 0;
            LastSyncTimeUtc = DateTime.UtcNow;
            OnStatusChanged($"Full re-pull complete. {result.Summary}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Force full pull failed");
            result.Errors.Add(ex.Message);
            OnStatusChanged($"Full re-pull failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsSyncing = false;
        }

        return result;
    }

    // ========== Sync State Persistence ==========

    private async Task<SyncStateEntity?> GetSyncStateAsync(string calendarId)
    {
        await _dbContext.InitializeAsync();
        return await _dbContext.Connection
            .Table<SyncStateEntity>()
            .Where(s => s.CalendarId == calendarId)
            .FirstOrDefaultAsync();
    }

    private async Task SaveSyncStateAsync(string calendarId, string syncToken)
    {
        await _dbContext.InitializeAsync();
        var existing = await GetSyncStateAsync(calendarId);

        if (existing != null)
        {
            existing.SyncToken = syncToken;
            existing.LastSyncUtc = DateTime.UtcNow;
            await _dbContext.Connection.UpdateAsync(existing);
        }
        else
        {
            await _dbContext.Connection.InsertAsync(new SyncStateEntity
            {
                CalendarId = calendarId,
                SyncToken = syncToken,
                LastSyncUtc = DateTime.UtcNow,
                Status = SyncStatus.Synced
            });
        }
    }

    /// <summary>
    /// Returns the cached Google calendar list from the local database (no network call).
    /// </summary>
    public async Task<IEnumerable<GoogleCalendarInfo>> GetCachedCalendarListAsync()
    {
        await _dbContext.InitializeAsync();
        var entities = await _dbContext.Connection.Table<GoogleCalendarListEntity>().ToListAsync();
        return entities.Select(e => new GoogleCalendarInfo
        {
            Id = e.CalendarId,
            Summary = !string.IsNullOrEmpty(e.Summary) ? e.Summary : e.CalendarId,
            ColorHex = !string.IsNullOrEmpty(e.UserColorHex) ? e.UserColorHex : e.ColorHex,
            IsPrimary = e.IsPrimary,
            AccessRole = e.AccessRole
        });
    }

    private void OnStatusChanged(string message, bool isError = false)
    {
        SyncStatusChanged?.Invoke(this, new SyncStatusChangedEventArgs(message, isError));
    }
}
