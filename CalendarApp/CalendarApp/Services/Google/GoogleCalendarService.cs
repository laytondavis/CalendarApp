using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Logging;

namespace CalendarApp.Services.Google;

/// <summary>
/// Google Calendar API service for CRUD operations and sync.
/// </summary>
public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IGoogleAuthService _authService;
    private readonly ILogger<GoogleCalendarService> _logger;

    public GoogleCalendarService(
        IGoogleAuthService authService,
        ILogger<GoogleCalendarService> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task<IEnumerable<GoogleCalendarInfo>> GetCalendarListAsync()
    {
        var service = await _authService.GetCalendarServiceAsync();
        if (service == null) return Enumerable.Empty<GoogleCalendarInfo>();

        try
        {
            var request = service.CalendarList.List();
            var result = await request.ExecuteAsync();

            return result.Items?.Select(c => new GoogleCalendarInfo
            {
                Id = c.Id,
                Summary = c.Summary ?? string.Empty,
                Description = c.Description ?? string.Empty,
                ColorHex = c.BackgroundColor ?? "#1a73e8",
                IsPrimary = c.Primary ?? false,
                AccessRole = c.AccessRole ?? string.Empty
            }) ?? Enumerable.Empty<GoogleCalendarInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get calendar list");
            return Enumerable.Empty<GoogleCalendarInfo>();
        }
    }

    public async Task<GoogleSyncResult> GetEventsAsync(
        string calendarId = "primary",
        string? syncToken = null,
        DateTime? timeMin = null,
        DateTime? timeMax = null)
    {
        var service = await _authService.GetCalendarServiceAsync();
        if (service == null)
        {
            return new GoogleSyncResult { Success = false, ErrorMessage = "Not authenticated" };
        }

        return await GetEventsFromServiceAsync(service, calendarId, syncToken, timeMin, timeMax);
    }

    public async Task<GoogleSyncResult> GetEventsFromServiceAsync(
        CalendarService calendarService,
        string calendarId = "primary",
        string? syncToken = null,
        DateTime? timeMin = null,
        DateTime? timeMax = null)
    {
        try
        {
            var allEvents = new List<CalendarEvent>();
            var deletedIds = new List<string>();
            string? pageToken = null;
            string? newSyncToken = null;
            var isFullSync = syncToken == null;

            do
            {
                var request = calendarService.Events.List(calendarId);
                request.MaxResults = 250;
                // SingleEvents = false (default): Google returns master recurring events with
                // RRULE instead of individual occurrences. RecurrenceExpander handles expansion.

                if (syncToken != null)
                {
                    request.SyncToken = syncToken;
                }
                else
                {
                    // With SingleEvents=false, timeMin filters by the master event's DTSTART
                    // (the first occurrence). Extending to 5 years back ensures recurring series
                    // that began years ago are still returned.
                    request.TimeMinDateTimeOffset = timeMin.HasValue
                        ? new DateTimeOffset(timeMin.Value, TimeSpan.Zero)
                        : new DateTimeOffset(DateTime.UtcNow.AddYears(-5), TimeSpan.Zero);
                    request.TimeMaxDateTimeOffset = timeMax.HasValue
                        ? new DateTimeOffset(timeMax.Value, TimeSpan.Zero)
                        : new DateTimeOffset(DateTime.UtcNow.AddYears(2), TimeSpan.Zero);
                }

                if (pageToken != null)
                    request.PageToken = pageToken;

                request.ShowDeleted = true;

                Events events;
                try
                {
                    events = await request.ExecuteAsync();
                }
                catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                {
                    // Sync token expired — do a full sync
                    _logger.LogWarning("Sync token expired, performing full sync");
                    return await GetEventsFromServiceAsync(calendarService, calendarId, null, timeMin, timeMax);
                }

                if (events.Items != null)
                {
                    foreach (var gEvent in events.Items)
                    {
                        // Skip individual instances of recurring events — they are identified by
                        // RecurringEventId being set. The master event (with RRULE) covers them;
                        // RecurrenceExpander will expand occurrences in memory.
                        if (!string.IsNullOrEmpty(gEvent.RecurringEventId))
                            continue;

                        if (gEvent.Status == "cancelled")
                            deletedIds.Add(gEvent.Id);
                        else
                            allEvents.Add(ConvertToLocal(gEvent));
                    }
                }

                pageToken = events.NextPageToken;
                newSyncToken = events.NextSyncToken;

            } while (pageToken != null);

            return new GoogleSyncResult
            {
                Events = allEvents,
                DeletedEventIds = deletedIds,
                NextSyncToken = newSyncToken,
                IsFullSync = isFullSync,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get events from Google Calendar");
            return new GoogleSyncResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<GoogleEventResult> CreateEventAsync(CalendarEvent localEvent, string calendarId = "primary")
    {
        var service = await _authService.GetCalendarServiceAsync();
        if (service == null)
        {
            return new GoogleEventResult { Success = false, ErrorMessage = "Not authenticated" };
        }

        try
        {
            var gEvent = ConvertToGoogle(localEvent);
            var created = await service.Events.Insert(gEvent, calendarId).ExecuteAsync();

            return new GoogleEventResult
            {
                Success = true,
                GoogleEventId = created.Id,
                ETag = created.ETag
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create event on Google Calendar");
            return new GoogleEventResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<GoogleEventResult> UpdateEventAsync(CalendarEvent localEvent, string calendarId = "primary")
    {
        var service = await _authService.GetCalendarServiceAsync();
        if (service == null)
        {
            return new GoogleEventResult { Success = false, ErrorMessage = "Not authenticated" };
        }

        if (string.IsNullOrEmpty(localEvent.GoogleEventId))
        {
            return new GoogleEventResult { Success = false, ErrorMessage = "No Google event ID" };
        }

        try
        {
            var gEvent = ConvertToGoogle(localEvent);
            var updated = await service.Events.Update(gEvent, calendarId, localEvent.GoogleEventId).ExecuteAsync();

            return new GoogleEventResult
            {
                Success = true,
                GoogleEventId = updated.Id,
                ETag = updated.ETag
            };
        }
        catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning("ETag conflict updating event {EventId}", localEvent.GoogleEventId);
            return new GoogleEventResult { Success = false, ErrorMessage = "Conflict: event modified remotely" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update event on Google Calendar");
            return new GoogleEventResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> DeleteEventAsync(string googleEventId, string calendarId = "primary")
    {
        var service = await _authService.GetCalendarServiceAsync();
        if (service == null) return false;

        try
        {
            await service.Events.Delete(calendarId, googleEventId).ExecuteAsync();
            return true;
        }
        catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Already deleted
            _logger.LogInformation("Event {EventId} was already deleted on Google", googleEventId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete event {EventId} from Google Calendar", googleEventId);
            return false;
        }
    }

    // ========== Conversion Helpers ==========

    private static CalendarEvent ConvertToLocal(Event gEvent)
    {
        var isAllDay = gEvent.Start?.Date != null;

        DateTime startDt, endDt;
        if (isAllDay)
        {
            startDt = DateTime.Parse(gEvent.Start.Date);
            endDt = DateTime.Parse(gEvent.End?.Date ?? gEvent.Start.Date);
        }
        else
        {
            startDt = gEvent.Start?.DateTimeDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow;
            endDt = gEvent.End?.DateTimeDateTimeOffset?.UtcDateTime ?? startDt.AddHours(1);
        }

        var localEvent = new CalendarEvent
        {
            GoogleEventId = gEvent.Id,
            Title = gEvent.Summary ?? "(No title)",
            Description = gEvent.Description ?? string.Empty,
            StartDateTime = startDt,
            EndDateTime = endDt,
            IsAllDay = isAllDay,
            Location = gEvent.Location ?? string.Empty,
            ColorHex = GetColorHex(gEvent.ColorId),
            ETag = gEvent.ETag ?? string.Empty,
            LastModifiedUtc = gEvent.Updated ?? DateTime.UtcNow,
            SyncStatus = SyncStatus.Synced
        };

        // Parse recurrence
        if (gEvent.Recurrence != null && gEvent.Recurrence.Count > 0)
        {
            var rruleLine = gEvent.Recurrence.FirstOrDefault(r => r.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
            if (rruleLine != null)
            {
                localEvent.RecurrenceRule = ParseRRule(rruleLine);
            }
        }

        // Parse reminders
        if (gEvent.Reminders?.Overrides != null)
        {
            localEvent.Reminders = gEvent.Reminders.Overrides.Select(r => new Reminder
            {
                MinutesBefore = r.Minutes ?? 10,
                Method = r.Method == "email" ? ReminderMethod.Email : ReminderMethod.Notification
            }).ToList();
        }

        return localEvent;
    }

    private static Event ConvertToGoogle(CalendarEvent localEvent)
    {
        var gEvent = new Event
        {
            Summary = localEvent.Title,
            Description = localEvent.Description,
            Location = localEvent.Location
        };

        if (localEvent.IsAllDay)
        {
            gEvent.Start = new EventDateTime { Date = localEvent.StartDateTime.ToString("yyyy-MM-dd") };
            gEvent.End = new EventDateTime { Date = localEvent.EndDateTime.ToString("yyyy-MM-dd") };
        }
        else
        {
            // Use an IANA timezone ID (Google rejects Windows-format IDs like "Eastern Standard Time").
            var ianaId = GetIanaTimeZoneId(TimeZoneInfo.Local);

            gEvent.Start = new EventDateTime
            {
                DateTimeDateTimeOffset = ToLocalDateTimeOffset(localEvent.StartDateTime),
                TimeZone = ianaId
            };
            gEvent.End = new EventDateTime
            {
                DateTimeDateTimeOffset = ToLocalDateTimeOffset(localEvent.EndDateTime),
                TimeZone = ianaId
            };
        }

        // Recurrence
        if (localEvent.RecurrenceRule != null)
        {
            gEvent.Recurrence = new List<string> { localEvent.RecurrenceRule.ToRRuleString() };
        }

        // Reminders
        if (localEvent.Reminders.Count > 0)
        {
            gEvent.Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = localEvent.Reminders.Select(r => new EventReminder
                {
                    Method = r.Method == ReminderMethod.Email ? "email" : "popup",
                    Minutes = r.MinutesBefore
                }).ToList()
            };
        }

        return gEvent;
    }

    /// <summary>
    /// Normalises a <see cref="DateTime"/> to the local machine's timezone and wraps it
    /// in a <see cref="DateTimeOffset"/> with the correct local UTC-offset.
    /// <para>
    /// sqlite-net-pcl reads <c>DateTime</c> values back with <c>Kind=Utc</c>, and
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc"/> also produces <c>Kind=Utc</c>.
    /// Passing such a value directly to <c>new DateTimeOffset(utcDt)</c> yields a
    /// <c>+00:00</c> offset — inconsistent with the local timezone string we send
    /// alongside it, causing Google to reject the request.
    /// Values with <c>Kind=Unspecified</c> are treated as already being in local time
    /// (the same assumption made by the ViewModel when building the start/end from
    /// the DatePicker + TimePicker).
    /// </para>
    /// </summary>
    private static DateTimeOffset ToLocalDateTimeOffset(DateTime dt)
    {
        var localDt = dt.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(dt, TimeZoneInfo.Local) // UTC → machine local
            : DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);     // already local time

        var offset = TimeZoneInfo.Local.GetUtcOffset(localDt);
        return new DateTimeOffset(localDt, offset);
    }

    /// <summary>
    /// Returns an IANA timezone identifier for <paramref name="tz"/>.
    /// On Windows <see cref="TimeZoneInfo.Id"/> is a Windows identifier
    /// (e.g. "Eastern Standard Time"); the Google Calendar API requires an IANA
    /// identifier (e.g. "America/New_York").  On all other platforms the Id is
    /// already in IANA format.
    /// </summary>
    private static string GetIanaTimeZoneId(TimeZoneInfo tz)
    {
        if (!OperatingSystem.IsWindows())
            return tz.Id;

        // .NET 6+ ships a built-in Windows→IANA mapping.
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out string? ianaId) && ianaId != null)
            return ianaId;

        // Fallback: return the Windows ID and hope Google recognises it (unlikely but safe).
        return tz.Id;
    }

    private static RecurrenceRule? ParseRRule(string rruleLine)
    {
        // Strip "RRULE:" prefix
        var rule = rruleLine.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)
            ? rruleLine.Substring(6)
            : rruleLine;

        var parts = rule.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);

        var recurrence = new RecurrenceRule
        {
            RRule = rruleLine
        };

        if (parts.TryGetValue("FREQ", out var freq))
        {
            recurrence.Frequency = freq.ToUpperInvariant() switch
            {
                "DAILY" => RecurrenceFrequency.Daily,
                "WEEKLY" => RecurrenceFrequency.Weekly,
                "MONTHLY" => RecurrenceFrequency.Monthly,
                "YEARLY" => RecurrenceFrequency.Yearly,
                _ => RecurrenceFrequency.Daily
            };
        }

        if (parts.TryGetValue("INTERVAL", out var interval) && int.TryParse(interval, out var intervalVal))
            recurrence.Interval = intervalVal;

        if (parts.TryGetValue("COUNT", out var count) && int.TryParse(count, out var countVal))
            recurrence.Count = countVal;

        if (parts.TryGetValue("UNTIL", out var until))
        {
            // Google (iCalendar) UNTIL uses compact UTC format: yyyyMMddTHHmmssZ or yyyyMMdd.
            // DateTime.TryParse cannot handle these without separators, so use TryParseExact.
            if (DateTime.TryParseExact(
                    until,
                    new[] { "yyyyMMddTHHmmssZ", "yyyyMMddTHHmmss", "yyyyMMdd",
                            "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                    out var untilDate))
            {
                recurrence.UntilDateUtc = DateTime.SpecifyKind(untilDate, DateTimeKind.Utc);
            }
        }

        if (parts.TryGetValue("BYDAY", out var byDay))
            recurrence.ByDay = byDay;

        if (parts.TryGetValue("BYMONTH", out var byMonth))
            recurrence.ByMonth = byMonth;

        if (parts.TryGetValue("BYMONTHDAY", out var byMonthDay))
            recurrence.ByMonthDay = byMonthDay;

        if (parts.TryGetValue("BYSETPOS", out var bySetPos))
            recurrence.BySetPos = bySetPos;

        return recurrence;
    }

    private static string GetColorHex(string? colorId)
    {
        // Google Calendar color IDs mapped to hex values
        return colorId switch
        {
            "1" => "#7986cb",  // Lavender
            "2" => "#33b679",  // Sage
            "3" => "#8e24aa",  // Grape
            "4" => "#e67c73",  // Flamingo
            "5" => "#f6bf26",  // Banana
            "6" => "#f4511e",  // Tangerine
            "7" => "#039be5",  // Peacock
            "8" => "#616161",  // Graphite
            "9" => "#3f51b5",  // Blueberry
            "10" => "#0b8043", // Basil
            "11" => "#d50000", // Tomato
            _ => "#1a73e8"     // Default Google blue
        };
    }
}
