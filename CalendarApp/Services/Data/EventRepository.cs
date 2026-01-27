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

    public async Task<CalendarEvent?> GetByIdAsync(int id)
    {
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        return entity != null ? await MapToModelAsync(entity) : null;
    }

    public async Task<CalendarEvent?> GetByGoogleIdAsync(string googleEventId)
    {
        var entity = await _context.Connection.Table<EventEntity>()
            .FirstOrDefaultAsync(e => e.GoogleEventId == googleEventId && !e.IsDeleted);

        return entity != null ? await MapToModelAsync(entity) : null;
    }

    public async Task<IEnumerable<CalendarEvent>> GetEventsForDateRangeAsync(
        DateTime start,
        DateTime end,
        CalendarMode? mode = null)
    {
        var query = _context.Connection.Table<EventEntity>()
            .Where(e => !e.IsDeleted)
            .Where(e => e.StartDateTimeUtc <= end && e.EndDateTimeUtc >= start);

        if (mode.HasValue)
        {
            var modeValue = (int)mode.Value;
            query = query.Where(e => e.CalendarModeValue == modeValue);
        }

        var entities = await query.ToListAsync();
        var events = new List<CalendarEvent>();

        foreach (var entity in entities)
        {
            events.Add(await MapToModelAsync(entity));
        }

        return events;
    }

    public async Task<IEnumerable<CalendarEvent>> GetPendingSyncEventsAsync()
    {
        var pendingUpload = (int)SyncStatus.PendingUpload;
        var entities = await _context.Connection.Table<EventEntity>()
            .Where(e => e.SyncStatusValue == pendingUpload || e.IsDeleted)
            .ToListAsync();

        var events = new List<CalendarEvent>();
        foreach (var entity in entities)
        {
            events.Add(await MapToModelAsync(entity));
        }

        return events;
    }

    public async Task<IEnumerable<CalendarEvent>> GetConflictedEventsAsync()
    {
        var conflict = (int)SyncStatus.Conflict;
        var entities = await _context.Connection.Table<EventEntity>()
            .Where(e => e.SyncStatusValue == conflict && !e.IsDeleted)
            .ToListAsync();

        var events = new List<CalendarEvent>();
        foreach (var entity in entities)
        {
            events.Add(await MapToModelAsync(entity));
        }

        return events;
    }

    public async Task<int> InsertAsync(CalendarEvent calendarEvent)
    {
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
        var entity = MapToEntity(calendarEvent);
        entity.Id = calendarEvent.Id;
        entity.LastModifiedUtc = DateTime.UtcNow;

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
            if (entity.RecurrenceId.HasValue)
            {
                var recurrenceEntity = MapRecurrenceToEntity(calendarEvent.RecurrenceRule, calendarEvent.Id);
                recurrenceEntity.Id = entity.RecurrenceId.Value;
                await _context.Connection.UpdateAsync(recurrenceEntity);
            }
            else
            {
                var recurrenceEntity = MapRecurrenceToEntity(calendarEvent.RecurrenceRule, calendarEvent.Id);
                await _context.Connection.InsertAsync(recurrenceEntity);
                entity.RecurrenceId = recurrenceEntity.Id;
                await _context.Connection.UpdateAsync(entity);
            }
        }
        else if (entity.RecurrenceId.HasValue)
        {
            // Remove recurrence rule
            await _context.Connection.Table<RecurrenceEntity>()
                .DeleteAsync(r => r.Id == entity.RecurrenceId.Value);
            entity.RecurrenceId = null;
            await _context.Connection.UpdateAsync(entity);
        }
    }

    public async Task DeleteAsync(int id)
    {
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

    public async Task UpdateSyncStatusAsync(int id, SyncStatus status, string? etag = null)
    {
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
