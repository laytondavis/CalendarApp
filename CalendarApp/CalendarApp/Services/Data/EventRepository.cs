using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Data;

/// <summary>
/// Repository implementation for calendar events.
/// </summary>
public class EventRepository : IEventRepository
{
    private readonly CalendarDbContext _context;

    public EventRepository(CalendarDbContext context)
    {
        _context = context;
    }

    private async Task EnsureInitializedAsync()
    {
        await _context.InitializeAsync();
    }

    public async Task<CalendarEvent?> GetByIdAsync(int id)
    {
        await EnsureInitializedAsync();
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        return entity != null ? await MapToModelAsync(entity) : null;
    }

    public async Task<CalendarEvent?> GetByGoogleIdAsync(string googleEventId)
    {
        await EnsureInitializedAsync();
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.GoogleEventId == googleEventId && !e.IsDeleted);

        return entity != null ? await MapToModelAsync(entity) : null;
    }

    public async Task<IEnumerable<CalendarEvent>> GetEventsForDateRangeAsync(
        DateTime start,
        DateTime end,
        CalendarMode? primaryMode = null,
        IEnumerable<CalendarMode>? additionalVisibleModes = null)
    {
        await EnsureInitializedAsync();

        // Build scope-filter values once
        int allScope     = (int)EventScope.All;
        int primaryScope = primaryMode.HasValue ? (int)primaryMode.Value + 1 : -1;
        var additionalScopes = new HashSet<int>();
        if (additionalVisibleModes != null)
            foreach (var mode in additionalVisibleModes)
                additionalScopes.Add((int)mode + 1);

        bool MatchesScope(EventEntity e) =>
            !primaryMode.HasValue ||
            e.EventScopeValue == allScope ||
            e.EventScopeValue == primaryScope ||
            additionalScopes.Contains(e.EventScopeValue);

        // ── Non-recurring events: standard date-range query ──────────────────
        var nonRecurring = await _context.Connection.Table<EventEntity>()
            .Where(e => !e.IsDeleted && e.RecurrenceId == null)
            .Where(e => e.StartDateTimeUtc <= end && e.EndDateTimeUtc >= start)
            .ToListAsync();

        // ── Recurring event base records: fetch all, expand in memory ────────
        // We cannot date-filter here because future occurrences of a past-dated
        // base record would otherwise be silently dropped.
        var recurringBases = await _context.Connection.Table<EventEntity>()
            .Where(e => !e.IsDeleted && e.RecurrenceId != null)
            .ToListAsync();

        var events = new List<CalendarEvent>();

        foreach (var entity in nonRecurring.Where(MatchesScope))
            events.Add(await MapToModelAsync(entity));

        foreach (var entity in recurringBases.Where(MatchesScope))
        {
            var baseEvent = await MapToModelAsync(entity);
            if (baseEvent.RecurrenceRule == null)
            {
                // Recurrence entity missing — fall back to treating as one-off
                if (entity.StartDateTimeUtc <= end && entity.EndDateTimeUtc >= start)
                    events.Add(baseEvent);
            }
            else
            {
                events.AddRange(RecurrenceExpander.Expand(baseEvent, start, end));
            }
        }

        return events;
    }

    public async Task<IEnumerable<CalendarEvent>> GetPendingSyncEventsAsync()
    {
        await EnsureInitializedAsync();
        var pendingUpload = (int)SyncStatus.PendingUpload;
        var entities = await _context.Connection.Table<EventEntity>()
            .Where(e => e.SyncStatusValue == pendingUpload || e.IsDeleted)
            .ToListAsync();

        var events = new List<CalendarEvent>();
        foreach (var entity in entities)
            events.Add(await MapToModelAsync(entity));

        return events;
    }

    public async Task<IEnumerable<CalendarEvent>> GetConflictedEventsAsync()
    {
        await EnsureInitializedAsync();
        var conflict = (int)SyncStatus.Conflict;
        var entities = await _context.Connection.Table<EventEntity>()
            .Where(e => e.SyncStatusValue == conflict && !e.IsDeleted)
            .ToListAsync();

        var events = new List<CalendarEvent>();
        foreach (var entity in entities)
            events.Add(await MapToModelAsync(entity));

        return events;
    }

    public async Task<int> InsertAsync(CalendarEvent calendarEvent)
    {
        await EnsureInitializedAsync();
        var entity = MapToEntity(calendarEvent);
        entity.LastModifiedUtc = DateTime.UtcNow;

        await _context.Connection.InsertAsync(entity);

        // Insert reminders
        foreach (var reminder in calendarEvent.Reminders)
        {
            var reminderEntity = new ReminderEntity
            {
                EventId = entity.Id,
                Method = reminder.Method,
                MinutesBefore = reminder.MinutesBefore,
                NotificationId = reminder.NotificationId
            };
            await _context.Connection.InsertAsync(reminderEntity);
        }

        // Insert recurrence rule if present
        if (calendarEvent.RecurrenceRule != null)
        {
            var recurrenceEntity = MapRecurrenceToEntity(calendarEvent.RecurrenceRule, entity.Id);
            await _context.Connection.InsertAsync(recurrenceEntity);
            entity.RecurrenceId = recurrenceEntity.Id;
            await _context.Connection.UpdateAsync(entity);
        }

        return entity.Id;
    }

    public async Task UpdateAsync(CalendarEvent calendarEvent)
    {
        await EnsureInitializedAsync();

        // Read the existing entity first to preserve the RecurrenceId (which is not
        // carried on the CalendarEvent domain model — it's a DB-only foreign key).
        var existing = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == calendarEvent.Id);
        var existingRecurrenceId = existing?.RecurrenceId;

        var entity = MapToEntity(calendarEvent);
        entity.Id = calendarEvent.Id;
        entity.LastModifiedUtc = DateTime.UtcNow;
        entity.RecurrenceId = existingRecurrenceId; // preserve until recurrence logic below

        await _context.Connection.UpdateAsync(entity);

        // Update reminders - delete existing and re-insert
        await _context.Connection.Table<ReminderEntity>()
            .DeleteAsync(r => r.EventId == calendarEvent.Id);

        foreach (var reminder in calendarEvent.Reminders)
        {
            var reminderEntity = new ReminderEntity
            {
                EventId = calendarEvent.Id,
                Method = reminder.Method,
                MinutesBefore = reminder.MinutesBefore,
                NotificationId = reminder.NotificationId
            };
            await _context.Connection.InsertAsync(reminderEntity);
        }

        // Update recurrence rule
        if (calendarEvent.RecurrenceRule != null)
        {
            if (existingRecurrenceId.HasValue)
            {
                // Update the existing recurrence row in place
                var recurrenceEntity = MapRecurrenceToEntity(calendarEvent.RecurrenceRule, calendarEvent.Id);
                recurrenceEntity.Id = existingRecurrenceId.Value;
                await _context.Connection.UpdateAsync(recurrenceEntity);
            }
            else
            {
                // No recurrence row yet — insert one and link it
                var recurrenceEntity = MapRecurrenceToEntity(calendarEvent.RecurrenceRule, calendarEvent.Id);
                await _context.Connection.InsertAsync(recurrenceEntity);
                entity.RecurrenceId = recurrenceEntity.Id;
                await _context.Connection.UpdateAsync(entity);
            }
        }
        else if (existingRecurrenceId.HasValue)
        {
            // Recurrence rule was removed — delete the orphaned row and clear the FK
            await _context.Connection.Table<RecurrenceEntity>()
                .DeleteAsync(r => r.Id == existingRecurrenceId.Value);
            entity.RecurrenceId = null;
            await _context.Connection.UpdateAsync(entity);
        }
    }

    public async Task DeleteAsync(int id)
    {
        await EnsureInitializedAsync();
        // Delete reminders first
        await _context.Connection.Table<ReminderEntity>()
            .DeleteAsync(r => r.EventId == id);

        // Delete recurrence rule
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity?.RecurrenceId != null)
        {
            await _context.Connection.Table<RecurrenceEntity>()
                .DeleteAsync(r => r.Id == entity.RecurrenceId.Value);
        }

        // Delete event
        await _context.Connection.DeleteAsync<EventEntity>(id);
    }

    public async Task MarkAsDeletedAsync(int id)
    {
        await EnsureInitializedAsync();
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity != null)
        {
            entity.IsDeleted = true;
            entity.SyncStatus = SyncStatus.PendingUpload;
            entity.LastModifiedUtc = DateTime.UtcNow;
            await _context.Connection.UpdateAsync(entity);
        }
    }

    public async Task DeleteGoogleEventsAsync()
    {
        await EnsureInitializedAsync();
        var googleEvents = await _context.Connection.Table<EventEntity>()
            .Where(e => e.GoogleEventId != "")
            .ToListAsync();

        foreach (var evt in googleEvents)
        {
            await _context.Connection.Table<ReminderEntity>()
                .DeleteAsync(r => r.EventId == evt.Id);

            if (evt.RecurrenceId.HasValue)
            {
                await _context.Connection.Table<RecurrenceEntity>()
                    .DeleteAsync(r => r.Id == evt.RecurrenceId.Value);
            }

            await _context.Connection.DeleteAsync(evt);
        }
    }

    public async Task UpdateSyncStatusAsync(int id, SyncStatus status, string? etag = null)
    {
        await EnsureInitializedAsync();
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity != null)
        {
            entity.SyncStatus = status;
            if (etag != null)
            {
                entity.ETag = etag;
            }
            await _context.Connection.UpdateAsync(entity);
        }
    }

    public async Task<IEnumerable<Reminder>> GetRemindersForEventAsync(int eventId)
    {
        await EnsureInitializedAsync();
        var entities = await _context.Connection.Table<ReminderEntity>()
            .Where(r => r.EventId == eventId)
            .ToListAsync();

        return entities.Select(e => new Reminder
        {
            Id = e.Id,
            EventId = e.EventId,
            Method = e.Method,
            MinutesBefore = e.MinutesBefore,
            NotificationId = e.NotificationId
        });
    }

    public async Task<IEnumerable<CalendarEvent>> GetEventsWithUpcomingRemindersAsync(DateTime until)
    {
        var events = await GetEventsForDateRangeAsync(DateTime.UtcNow, until);
        return events.Where(e => e.Reminders.Any());
    }

    private async Task<CalendarEvent> MapToModelAsync(EventEntity entity)
    {
        var reminders = await GetRemindersForEventAsync(entity.Id);

        RecurrenceRule? recurrenceRule = null;
        if (entity.RecurrenceId.HasValue)
        {
            var recurrenceEntity = await _context.Connection.Table<RecurrenceEntity>()
                .FirstOrDefaultAsync(r => r.Id == entity.RecurrenceId.Value);

            if (recurrenceEntity != null)
            {
                recurrenceRule = MapRecurrenceToModel(recurrenceEntity);
            }
        }

        return new CalendarEvent
        {
            Id = entity.Id,
            GoogleEventId = entity.GoogleEventId,
            Title = entity.Title,
            Description = entity.Description,
            StartDateTime = entity.StartDateTimeUtc,
            EndDateTime = entity.EndDateTimeUtc,
            IsAllDay = entity.IsAllDay,
            BiblicalStartDateTime = entity.BiblicalStartDateTimeUtc,
            Location = entity.Location,
            CalendarId = entity.CalendarId,
            ColorHex = entity.ColorHex,
            CalendarMode = entity.CalendarMode,
            EventScope = entity.EventScope,
            GoogleAccountAlias = entity.GoogleAccountAlias,
            SyncStatus = entity.SyncStatus,
            ETag = entity.ETag,
            LastModifiedUtc = entity.LastModifiedUtc,
            IsDeleted = entity.IsDeleted,
            Reminders = reminders.ToList(),
            RecurrenceRule = recurrenceRule
        };
    }

    private static EventEntity MapToEntity(CalendarEvent model)
    {
        return new EventEntity
        {
            GoogleEventId = model.GoogleEventId,
            Title = model.Title,
            Description = model.Description,
            StartDateTimeUtc = model.StartDateTime,
            EndDateTimeUtc = model.EndDateTime,
            IsAllDay = model.IsAllDay,
            BiblicalStartDateTimeUtc = model.BiblicalStartDateTime,
            Location = model.Location,
            CalendarId = model.CalendarId,
            ColorHex = model.ColorHex,
            CalendarMode = model.CalendarMode,
            EventScope = model.EventScope,
            GoogleAccountAlias = model.GoogleAccountAlias,
            SyncStatus = model.SyncStatus,
            ETag = model.ETag,
            IsDeleted = model.IsDeleted
        };
    }

    private static RecurrenceEntity MapRecurrenceToEntity(RecurrenceRule model, int eventId)
    {
        return new RecurrenceEntity
        {
            EventId = eventId,
            RRule = model.RRule,
            Frequency = model.Frequency,
            Interval = model.Interval,
            Count = model.Count,
            UntilDateUtc = model.UntilDateUtc,
            ByDay = model.ByDay,
            ByMonth = model.ByMonth,
            ByMonthDay = model.ByMonthDay,
            BySetPos = model.BySetPos,
            ExceptionDates = model.ExceptionDates
        };
    }

    private static RecurrenceRule MapRecurrenceToModel(RecurrenceEntity entity)
    {
        return new RecurrenceRule
        {
            Id = entity.Id,
            EventId = entity.EventId,
            RRule = entity.RRule,
            Frequency = entity.Frequency,
            Interval = entity.Interval,
            Count = entity.Count,
            UntilDateUtc = entity.UntilDateUtc,
            ByDay = entity.ByDay,
            ByMonth = entity.ByMonth,
            ByMonthDay = entity.ByMonthDay,
            BySetPos = entity.BySetPos,
            ExceptionDates = entity.ExceptionDates
        };
    }
}
