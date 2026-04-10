using CalendarApp.Models;
using CalendarApp.Services.Calendar;
using CalendarApp.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;

namespace CalendarApp.Presentation;

/// <summary>
/// View model for the event editor page.
/// Supports creating new events and editing existing ones.
/// </summary>
public partial class EventEditorViewModel : ObservableObject
{
    private readonly INavigator _navigator;
    private readonly IEventRepository _eventRepository;
    private readonly ISyncService _syncService;
    private readonly IGoogleAuthService _authService;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly BiblicalCalendarService _biblicalService;

    private int? _editingEventId;
    private bool _isNewEvent;
    // Timezone used when converting UTC → local for display during this edit session.
    // Non-null only when editing a Google-sourced event.
    private TimeZoneInfo? _editingEventTimezone;
    // True when editing a read-only (secondary-account) Google event.
    private bool _isReadOnlyGoogleEvent;
    // Google CalendarId the event had when editing started (for move detection).
    private string _editingOriginalCalendarId = string.Empty;

    [ObservableProperty]
    private string _pageTitle = "New Event";

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _startDate = DateTimeOffset.Now;

    [ObservableProperty]
    private TimeSpan _startTime = DateTime.Now.TimeOfDay;

    [ObservableProperty]
    private DateTimeOffset _endDate = DateTimeOffset.Now;

    [ObservableProperty]
    private TimeSpan _endTime = DateTime.Now.AddHours(1).TimeOfDay;

    [ObservableProperty]
    private bool _isAllDay;

    [ObservableProperty]
    private string _selectedColor = "#1a73e8";

    [ObservableProperty]
    private Windows.UI.Color _selectedColorAsColor = Windows.UI.Color.FromArgb(255, 26, 115, 232);

    partial void OnSelectedColorAsColorChanged(Windows.UI.Color value)
    {
        SelectedColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    [ObservableProperty]
    private EventColorOption? _selectedColorOption;

    partial void OnSelectedColorOptionChanged(EventColorOption? value)
    {
        if (value is null) return;
        SelectedColor = value.Hex;
        // Set backing field directly to avoid re-triggering OnSelectedColorAsColorChanged
        _selectedColorAsColor = ParseHexColor(value.Hex);
        OnPropertyChanged(nameof(SelectedColorAsColor));
    }

    [ObservableProperty]
    private int _selectedReminderIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecurrenceOptions))]
    [NotifyPropertyChangedFor(nameof(ShowRecurrenceEndDatePicker))]
    private int _selectedRecurrenceIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecurrenceEndDatePicker))]
    private bool _hasRecurrenceEndDate;

    [ObservableProperty]
    private DateTimeOffset _recurrenceEndDate = DateTimeOffset.Now.AddYears(1);

    public bool ShowRecurrenceOptions => SelectedRecurrenceIndex > 0;
    public bool ShowRecurrenceEndDatePicker => SelectedRecurrenceIndex > 0 && HasRecurrenceEndDate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private int _selectedCalendarModeIndex;

    [ObservableProperty]
    private int _selectedEventScopeIndex; // 0=All Calendars, 1=Gregorian, 2=Julian, 3=Biblical

    // ── Google Calendar integration ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMakeLocalButton))]
    [NotifyPropertyChangedFor(nameof(GoogleCalendarPickerItems))]
    [NotifyPropertyChangedFor(nameof(GoogleCalendarPickerHeader))]
    [NotifyPropertyChangedFor(nameof(ShowGoogleCalendarSection))]
    private bool _isGoogleEvent;

    [ObservableProperty]
    private string _eventSourceText = string.Empty;

    [ObservableProperty]
    private int _selectedGoogleCalendarIndex;

    /// <summary>Available Google calendars for the picker.</summary>
    public ObservableCollection<GoogleCalendarInfo> AvailableGoogleCalendars { get; } = new();

    /// <summary>Items shown in the Google Calendar ComboBox.
    /// For local events the first item is "(Keep as local event)"; for Google events it is omitted.</summary>
    public string[] GoogleCalendarPickerItems =>
        IsGoogleEvent
            ? AvailableGoogleCalendars.Select(c => c.Summary).ToArray()
            : new[] { "(Keep as local event)" }.Concat(AvailableGoogleCalendars.Select(c => c.Summary)).ToArray();

    public string GoogleCalendarPickerHeader => IsGoogleEvent ? "Google Calendar" : "Add to Google Calendar";

    /// <summary>Show the Google Calendar picker when signed in and there are cached calendars,
    /// unless the event is from a read-only (secondary) account.</summary>
    public bool ShowGoogleCalendarSection =>
        _authService.IsSignedIn && AvailableGoogleCalendars.Count > 0 && !_isReadOnlyGoogleEvent;

    /// <summary>Show "Remove from Google Calendar" only for writable Google events.</summary>
    public bool ShowMakeLocalButton => IsGoogleEvent && !_isReadOnlyGoogleEvent && _authService.IsSignedIn;

    public EventEditorViewModel(
        INavigator navigator,
        IEventRepository eventRepository,
        ISyncService syncService,
        IGoogleAuthService authService,
        IGoogleCalendarService googleCalendarService,
        BiblicalCalendarService biblicalService)
    {
        _navigator = navigator;
        _eventRepository = eventRepository;
        _syncService = syncService;
        _authService = authService;
        _googleCalendarService = googleCalendarService;
        _biblicalService = biblicalService;
        _isNewEvent = true;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        CancelCommand = new AsyncRelayCommand(CancelAsync);
        MakeLocalCommand = new AsyncRelayCommand(MakeLocalAsync);
    }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand MakeLocalCommand { get; }

    /// <summary>
    /// Available event colors (Google Calendar palette).
    /// </summary>
    public ObservableCollection<EventColorOption> ColorOptions { get; } = new()
    {
        new("#1a73e8", "Default Blue"),
        new("#7986cb", "Lavender"),
        new("#33b679", "Sage"),
        new("#8e24aa", "Grape"),
        new("#e67c73", "Flamingo"),
        new("#f6bf26", "Banana"),
        new("#f4511e", "Tangerine"),
        new("#039be5", "Peacock"),
        new("#616161", "Graphite"),
        new("#3f51b5", "Blueberry"),
        new("#0b8043", "Basil"),
        new("#d50000", "Tomato"),
    };

    /// <summary>
    /// Reminder options.
    /// </summary>
    public ObservableCollection<ReminderOption> ReminderOptions { get; } = new()
    {
        new(0, "None"),
        new(0, "At time of event"),
        new(5, "5 minutes before"),
        new(10, "10 minutes before"),
        new(15, "15 minutes before"),
        new(30, "30 minutes before"),
        new(60, "1 hour before"),
        new(120, "2 hours before"),
        new(1440, "1 day before"),
        new(2880, "2 days before"),
        new(10080, "1 week before"),
    };

    // Number of fixed preset options — custom entries from Google live beyond this index.
    private const int FixedRecurrenceOptionCount = 6;

    /// <summary>
    /// Recurrence options.
    /// </summary>
    public ObservableCollection<RecurrenceOption> RecurrenceOptions { get; } = new()
    {
        new(null, "Does not repeat"),
        new("RRULE:FREQ=DAILY", "Daily"),
        new("RRULE:FREQ=WEEKLY", "Weekly"),
        new("RRULE:FREQ=MONTHLY", "Monthly"),
        new("RRULE:FREQ=YEARLY", "Yearly"),
        new("RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "Every weekday"),
    };

    public string[] CalendarModeNames => new[] { "Gregorian", "Julian", "Biblical" };

    public string[] EventScopeNames => new[] { "All Calendars", "Gregorian Only", "Julian Only", "Biblical Only" };

    public string[] ReminderDisplayNames => ReminderOptions.Select(r => r.Display).ToArray();

    public string[] RecurrenceDisplayNames => RecurrenceOptions.Select(r => r.Display).ToArray();

    private TimeZoneInfo GetDisplayTimezone()
    {
        try { return _biblicalService.GetCurrentLocation().TimeZone; }
        catch { return TimeZoneInfo.Local; }
    }

    /// <summary>
    /// Initialize for creating a new event on a given date.
    /// </summary>
    public void InitializeNewEvent(DateTime date)
    {
        _isNewEvent = true;
        _editingEventId = null;
        _editingEventTimezone = null;
        _isReadOnlyGoogleEvent = false;
        _editingOriginalCalendarId = string.Empty;
        PageTitle = "New Event";
        CanDelete = false;

        StartDate = new DateTimeOffset(date);
        EndDate = new DateTimeOffset(date);
        StartTime = new TimeSpan(9, 0, 0);
        EndTime = new TimeSpan(10, 0, 0);
        Title = string.Empty;
        Description = string.Empty;
        Location = string.Empty;
        IsAllDay = false;
        SelectedColor = "#1a73e8";
        SelectedColorAsColor = ParseHexColor(SelectedColor);
        SelectedColorOption = ColorOptions.FirstOrDefault(c => c.Hex == SelectedColor) ?? ColorOptions[0];
        SelectedReminderIndex = 3; // 10 minutes before
        // Remove any custom recurrence entry added during a previous edit session
        while (RecurrenceOptions.Count > FixedRecurrenceOptionCount)
            RecurrenceOptions.RemoveAt(RecurrenceOptions.Count - 1);
        OnPropertyChanged(nameof(RecurrenceDisplayNames));
        SelectedRecurrenceIndex = 0; // Does not repeat
        HasRecurrenceEndDate = false;
        RecurrenceEndDate = new DateTimeOffset(date.AddYears(1));
        SelectedCalendarModeIndex = 0; // Gregorian
        SelectedEventScopeIndex = 0; // All Calendars
        // Google calendar state
        IsGoogleEvent = false;
        EventSourceText = "Local event";
        SelectedGoogleCalendarIndex = 0;
        _ = LoadGoogleCalendarsAsync();
        ErrorMessage = null;
    }

    /// <summary>
    /// Initialize for editing an existing event.
    /// </summary>
    public async Task InitializeEditEventAsync(int eventId)
    {
        _isNewEvent = false;
        _editingEventId = eventId;
        _isReadOnlyGoogleEvent = false;
        _editingOriginalCalendarId = string.Empty;
        PageTitle = "Edit Event";
        CanDelete = true;

        var evt = await _eventRepository.GetByIdAsync(eventId);
        if (evt == null)
        {
            ErrorMessage = "Event not found";
            return;
        }

        Title = evt.Title;
        Description = evt.Description;
        Location = evt.Location;
        IsAllDay = evt.IsAllDay;

        // Google events are stored as UTC; convert to the location's timezone for display.
        if (!evt.IsAllDay && !string.IsNullOrEmpty(evt.GoogleEventId))
        {
            _editingEventTimezone = GetDisplayTimezone();
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(evt.StartDateTime, DateTimeKind.Utc), _editingEventTimezone);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(evt.EndDateTime, DateTimeKind.Utc), _editingEventTimezone);
            StartDate = new DateTimeOffset(localStart.Date);
            StartTime = localStart.TimeOfDay;
            EndDate = new DateTimeOffset(localEnd.Date);
            EndTime = localEnd.TimeOfDay;
        }
        else
        {
            _editingEventTimezone = null;
            StartDate = new DateTimeOffset(evt.StartDateTime.Date);
            StartTime = evt.StartDateTime.TimeOfDay;
            EndDate = new DateTimeOffset(evt.EndDateTime.Date);
            EndTime = evt.EndDateTime.TimeOfDay;
        }
        SelectedColor = evt.ColorHex;
        SelectedColorAsColor = ParseHexColor(SelectedColor);
        SelectedColorOption = ColorOptions.FirstOrDefault(c => string.Equals(c.Hex, SelectedColor, StringComparison.OrdinalIgnoreCase))
                           ?? ColorOptions[0];
        SelectedCalendarModeIndex = (int)evt.CalendarMode;
        SelectedEventScopeIndex = (int)evt.EventScope;

        // Find matching reminder
        SelectedReminderIndex = 0;
        if (evt.Reminders.Count > 0)
        {
            var minutes = evt.Reminders[0].MinutesBefore;
            for (int i = 0; i < ReminderOptions.Count; i++)
            {
                if (ReminderOptions[i].MinutesBefore == minutes)
                {
                    SelectedReminderIndex = i;
                    break;
                }
            }
        }

        // Find matching recurrence; add a custom entry when no preset matches.
        while (RecurrenceOptions.Count > FixedRecurrenceOptionCount)
            RecurrenceOptions.RemoveAt(RecurrenceOptions.Count - 1);

        SelectedRecurrenceIndex = 0;
        if (evt.RecurrenceRule != null)
        {
            var rrule = evt.RecurrenceRule.ToRRuleString();
            bool matched = false;
            for (int i = 0; i < FixedRecurrenceOptionCount; i++)
            {
                if (RecurrenceOptions[i].RRule == rrule)
                {
                    SelectedRecurrenceIndex = i;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                var label = FormatRecurrenceRule(evt.RecurrenceRule);
                RecurrenceOptions.Add(new RecurrenceOption(rrule, label));
                OnPropertyChanged(nameof(RecurrenceDisplayNames));
                SelectedRecurrenceIndex = RecurrenceOptions.Count - 1;
            }
        }

        // Load recurrence end date
        HasRecurrenceEndDate = evt.RecurrenceRule?.UntilDateUtc.HasValue == true;
        RecurrenceEndDate = HasRecurrenceEndDate
            ? new DateTimeOffset(evt.RecurrenceRule!.UntilDateUtc!.Value, TimeSpan.Zero)
            : DateTimeOffset.Now.AddYears(1);

        // Load Google calendar state
        _editingOriginalCalendarId = evt.CalendarId;
        IsGoogleEvent = !string.IsNullOrEmpty(evt.GoogleEventId);
        _isReadOnlyGoogleEvent = IsGoogleEvent && !string.IsNullOrEmpty(evt.GoogleAccountAlias);
        await LoadGoogleCalendarsAsync();
        if (IsGoogleEvent)
        {
            var idx = AvailableGoogleCalendars
                .Select((c, i) => (c, i))
                .FirstOrDefault(x => x.c.Id == evt.CalendarId).i;
            SelectedGoogleCalendarIndex = idx;
            var calName = AvailableGoogleCalendars.ElementAtOrDefault(idx)?.Summary ?? evt.CalendarId;
            EventSourceText = _isReadOnlyGoogleEvent
                ? $"Google Calendar (read-only): {calName} via {evt.GoogleAccountAlias}"
                : $"Google Calendar: {calName}";
        }
        else
        {
            SelectedGoogleCalendarIndex = 0;
            EventSourceText = "Local event";
        }
        OnPropertyChanged(nameof(ShowGoogleCalendarSection));
        OnPropertyChanged(nameof(ShowMakeLocalButton));

        ErrorMessage = null;
    }

    partial void OnIsAllDayChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTimePickers));
    }

    partial void OnStartDateChanged(DateTimeOffset value) => AdjustEndIfBeforeStart();
    partial void OnStartTimeChanged(TimeSpan value) => AdjustEndIfBeforeStart();

    private void AdjustEndIfBeforeStart()
    {
        var start = StartDate.Date + StartTime;
        var end   = EndDate.Date   + EndTime;
        if (end <= start)
        {
            EndDate = StartDate;
            if (EndTime <= StartTime)
                EndTime = StartTime.Add(TimeSpan.FromHours(1));
        }
    }

    public bool ShowTimePickers => !IsAllDay;

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var startDateTime = IsAllDay
                ? StartDate.Date
                : StartDate.Date + StartTime;
            var endDateTime = IsAllDay
                ? EndDate.Date.AddDays(1)
                : EndDate.Date + EndTime;

            if (endDateTime <= startDateTime)
            {
                ErrorMessage = "End time must be after start time";
                IsBusy = false;
                return;
            }

            var calendarMode = (CalendarMode)SelectedCalendarModeIndex;
            var eventScope = (EventScope)SelectedEventScopeIndex;

            if (_isNewEvent)
            {
                SyncDiagnosticLog.Write(
                    $"EventEditor.Save: NEW EVENT — Title='{Title.Trim()}'" +
                    $", IsGoogleEvent={IsGoogleEvent}" +
                    $", SignedIn={_authService.IsSignedIn}" +
                    $", SelectedGCalIdx={SelectedGoogleCalendarIndex}" +
                    $", AvailableGCals={AvailableGoogleCalendars.Count}");

                // Should this event be synced to a Google calendar?
                bool wantsGoogle = !IsGoogleEvent && _authService.IsSignedIn
                    && SelectedGoogleCalendarIndex > 0
                    && SelectedGoogleCalendarIndex - 1 < AvailableGoogleCalendars.Count;

                var newEvent = new CalendarEvent
                {
                    Title = Title.Trim(),
                    Description = Description.Trim(),
                    Location = Location.Trim(),
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime,
                    IsAllDay = IsAllDay,
                    ColorHex = SelectedColor,
                    CalendarMode = calendarMode,
                    EventScope = eventScope,
                    LastModifiedUtc = DateTime.UtcNow
                };

                // Reminder
                if (SelectedReminderIndex > 0)
                {
                    newEvent.Reminders.Add(new Reminder
                    {
                        MinutesBefore = ReminderOptions[SelectedReminderIndex].MinutesBefore,
                        Method = ReminderMethod.Notification
                    });
                }

                // Recurrence
                if (SelectedRecurrenceIndex > 0)
                {
                    var rrule = RecurrenceOptions[SelectedRecurrenceIndex].RRule;
                    if (rrule != null)
                    {
                        newEvent.RecurrenceRule = ParseSimpleRRule(rrule);
                        if (HasRecurrenceEndDate)
                        {
                            newEvent.RecurrenceRule.UntilDateUtc = RecurrenceEndDate.UtcDateTime;
                            newEvent.RecurrenceRule.RRule = newEvent.RecurrenceRule.ToRRuleString();
                        }
                    }
                }

                if (wantsGoogle)
                {
                    // Insert as PendingUpload with the target CalendarId, then let
                    // SyncService handle the push+pull atomically. This avoids races
                    // where both EventEditorViewModel and SyncService write to Google
                    // or where a pull interleaves with a direct push.
                    var targetCal = AvailableGoogleCalendars[SelectedGoogleCalendarIndex - 1];
                    newEvent.CalendarId = targetCal.Id;
                    newEvent.SyncStatus = SyncStatus.PendingUpload;
                    SyncDiagnosticLog.Write(
                        $"EventEditor.Save: path=wantsGoogle, targetCal={targetCal.Id}" +
                        $" ('{targetCal.Summary}'), inserting as PendingUpload");
                    await _eventRepository.InsertAsync(newEvent);
                    SyncDiagnosticLog.Write(
                        $"EventEditor.Save: inserted locally with Id={newEvent.Id}" +
                        $", GoogleEventId='{newEvent.GoogleEventId}' — triggering immediate sync");

                    // Trigger an immediate sync so the event appears on Google promptly
                    _ = _syncService.SyncAsync(targetCal.Id);
                }
                else
                {
                    // Local-only event (or signed-in but no calendar selected —
                    // SyncService will push to "primary" on the next cycle)
                    newEvent.SyncStatus = _authService.IsSignedIn
                        ? SyncStatus.PendingUpload
                        : SyncStatus.Synced;
                    SyncDiagnosticLog.Write(
                        $"EventEditor.Save: path=local-only" +
                        $", SyncStatus={newEvent.SyncStatus}, inserting");
                    await _eventRepository.InsertAsync(newEvent);
                    SyncDiagnosticLog.Write(
                        $"EventEditor.Save: inserted locally with Id={newEvent.Id}");
                }
            }
            else if (_editingEventId.HasValue)
            {
                var existing = await _eventRepository.GetByIdAsync(_editingEventId.Value);
                if (existing == null)
                {
                    ErrorMessage = "Event not found";
                    IsBusy = false;
                    return;
                }

                existing.Title = Title.Trim();
                existing.Description = Description.Trim();
                existing.Location = Location.Trim();
                // If we converted UTC→local for display, convert back to UTC for storage.
                if (_editingEventTimezone != null && !IsAllDay)
                {
                    existing.StartDateTime = TimeZoneInfo.ConvertTimeToUtc(startDateTime, _editingEventTimezone);
                    existing.EndDateTime = TimeZoneInfo.ConvertTimeToUtc(endDateTime, _editingEventTimezone);
                }
                else
                {
                    existing.StartDateTime = startDateTime;
                    existing.EndDateTime = endDateTime;
                }
                existing.IsAllDay = IsAllDay;
                existing.ColorHex = SelectedColor;
                existing.CalendarMode = calendarMode;
                existing.EventScope = eventScope;
                existing.LastModifiedUtc = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(existing.GoogleEventId))
                {
                    existing.SyncStatus = SyncStatus.PendingUpload;
                }

                // Update reminder
                existing.Reminders.Clear();
                if (SelectedReminderIndex > 0)
                {
                    existing.Reminders.Add(new Reminder
                    {
                        EventId = existing.Id,
                        MinutesBefore = ReminderOptions[SelectedReminderIndex].MinutesBefore,
                        Method = ReminderMethod.Notification
                    });
                }

                // Update recurrence
                if (SelectedRecurrenceIndex > 0)
                {
                    var rrule = RecurrenceOptions[SelectedRecurrenceIndex].RRule;
                    if (rrule != null)
                    {
                        existing.RecurrenceRule = ParseSimpleRRule(rrule);
                        existing.RecurrenceRule.EventId = existing.Id;
                        if (HasRecurrenceEndDate)
                        {
                            existing.RecurrenceRule.UntilDateUtc = RecurrenceEndDate.UtcDateTime;
                            existing.RecurrenceRule.RRule = existing.RecurrenceRule.ToRRuleString();
                        }
                    }
                }
                else
                {
                    existing.RecurrenceRule = null;
                }

                await _eventRepository.UpdateAsync(existing);

                // Push changes back to Google (or move to a different calendar)
                if (IsGoogleEvent && !_isReadOnlyGoogleEvent && _authService.IsSignedIn)
                {
                    string targetCalendarId = existing.CalendarId;
                    if (AvailableGoogleCalendars.Count > 0
                        && SelectedGoogleCalendarIndex >= 0
                        && SelectedGoogleCalendarIndex < AvailableGoogleCalendars.Count)
                    {
                        targetCalendarId = AvailableGoogleCalendars[SelectedGoogleCalendarIndex].Id;
                    }

                    if (!string.IsNullOrEmpty(_editingOriginalCalendarId)
                        && targetCalendarId != _editingOriginalCalendarId)
                    {
                        // Move: delete from old calendar, create on new calendar
                        await _googleCalendarService.DeleteEventAsync(existing.GoogleEventId, _editingOriginalCalendarId);
                        existing.GoogleEventId = string.Empty;
                        existing.CalendarId = targetCalendarId;
                        var moveResult = await _googleCalendarService.CreateEventAsync(existing, targetCalendarId);
                        if (moveResult.Success)
                        {
                            existing.GoogleEventId = moveResult.GoogleEventId ?? string.Empty;
                            existing.ETag = moveResult.ETag ?? string.Empty;
                            existing.SyncStatus = SyncStatus.Synced;
                            await _eventRepository.UpdateAsync(existing);
                        }
                    }
                    else
                    {
                        // Same calendar: push update
                        var updateResult = await _googleCalendarService.UpdateEventAsync(existing, existing.CalendarId);
                        if (updateResult.Success)
                        {
                            existing.ETag = updateResult.ETag ?? existing.ETag;
                            existing.SyncStatus = SyncStatus.Synced;
                            await _eventRepository.UpdateAsync(existing);
                        }
                        else
                        {
                            // Leave SyncStatus = PendingUpload so the next sync retries
                            ErrorMessage = $"Saved locally, but Google sync failed: {updateResult.ErrorMessage}";
                            IsBusy = false;
                            return;
                        }
                    }
                }
                else if (!IsGoogleEvent && _authService.IsSignedIn
                    && SelectedGoogleCalendarIndex > 0
                    && SelectedGoogleCalendarIndex - 1 < AvailableGoogleCalendars.Count)
                {
                    // Existing local event being pushed to Google for the first time
                    var targetCal = AvailableGoogleCalendars[SelectedGoogleCalendarIndex - 1];
                    existing.CalendarId = targetCal.Id;
                    var createResult = await _googleCalendarService.CreateEventAsync(existing, targetCal.Id);
                    if (createResult.Success)
                    {
                        existing.GoogleEventId = createResult.GoogleEventId ?? string.Empty;
                        existing.ETag = createResult.ETag ?? string.Empty;
                        existing.SyncStatus = SyncStatus.Synced;
                        await _eventRepository.UpdateAsync(existing);
                    }
                    else
                    {
                        ErrorMessage = $"Saved locally, but could not add to Google Calendar: {createResult.ErrorMessage}";
                        IsBusy = false;
                        return; // Stay on page so user can see the error
                    }
                }
            }

            WeakReferenceMessenger.Default.Send(new EventEditorClosedMessage());
            await _navigator.GoBack(this);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (!_editingEventId.HasValue) return;

        IsBusy = true;
        try
        {
            var existing = await _eventRepository.GetByIdAsync(_editingEventId.Value);
            if (existing != null && !string.IsNullOrEmpty(existing.GoogleEventId))
            {
                if (!_isReadOnlyGoogleEvent && _authService.IsSignedIn)
                {
                    // Push deletion to Google immediately, then hard-delete locally
                    await _googleCalendarService.DeleteEventAsync(existing.GoogleEventId, existing.CalendarId);
                    await _eventRepository.DeleteAsync(_editingEventId.Value);
                }
                else
                {
                    // Read-only account or offline: soft-delete so the next sync can clean it up
                    await _eventRepository.MarkAsDeletedAsync(_editingEventId.Value);
                }
            }
            else
            {
                await _eventRepository.DeleteAsync(_editingEventId.Value);
            }

            WeakReferenceMessenger.Default.Send(new EventEditorClosedMessage());
            await _navigator.GoBack(this);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CancelAsync()
    {
        await _navigator.GoBack(this);
    }

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        hex = (hex ?? string.Empty).TrimStart('#');
        if (hex.Length < 6) return Windows.UI.Color.FromArgb(255, 26, 115, 232);
        try
        {
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
        catch { return Windows.UI.Color.FromArgb(255, 26, 115, 232); }
    }

    private static string FormatRecurrenceRule(RecurrenceRule rule)
    {
        var dayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MO"] = "Monday", ["TU"] = "Tuesday", ["WE"] = "Wednesday",
            ["TH"] = "Thursday", ["FR"] = "Friday", ["SA"] = "Saturday", ["SU"] = "Sunday"
        };

        string label = rule.Frequency switch
        {
            RecurrenceFrequency.Daily   => rule.Interval > 1 ? $"Every {rule.Interval} days"   : "Every day",
            RecurrenceFrequency.Monthly => rule.Interval > 1 ? $"Every {rule.Interval} months" : "Every month",
            RecurrenceFrequency.Yearly  => rule.Interval > 1 ? $"Every {rule.Interval} years"  : "Every year",
            RecurrenceFrequency.Weekly  => BuildWeeklyLabel(rule, dayMap),
            _ => "Custom repeat"
        };

        if (rule.Count.HasValue)
            label += $", {rule.Count} times";
        else if (rule.UntilDateUtc.HasValue)
            label += $", until {rule.UntilDateUtc.Value:MMM d, yyyy}";

        return label;
    }

    private static string BuildWeeklyLabel(RecurrenceRule rule, Dictionary<string, string> dayMap)
    {
        var prefix = rule.Interval > 1 ? $"Every {rule.Interval} weeks" : "Every week";
        if (string.IsNullOrEmpty(rule.ByDay)) return prefix;
        var days = rule.ByDay.Split(',')
            .Select(d => dayMap.TryGetValue(d.Trim(), out var name) ? name : d.Trim());
        return $"{prefix} on {string.Join(", ", days)}";
    }

    private async Task LoadGoogleCalendarsAsync()
    {
        if (!_authService.IsSignedIn) return;
        try
        {
            var cached = (await _syncService.GetCachedCalendarListAsync()).ToList();
            // If the local cache is empty, do a quick refresh from Google so the picker is usable
            if (cached.Count == 0)
            {
                await _syncService.RefreshCalendarListAsync();
                cached = (await _syncService.GetCachedCalendarListAsync()).ToList();
            }
            AvailableGoogleCalendars.Clear();
            // Only show calendars where the user can create events
            foreach (var cal in cached.Where(c =>
                c.AccessRole == "owner" || c.AccessRole == "writer"))
            {
                AvailableGoogleCalendars.Add(cal);
            }
        }
        catch { /* best-effort */ }
        OnPropertyChanged(nameof(GoogleCalendarPickerItems));
        OnPropertyChanged(nameof(ShowGoogleCalendarSection));
        OnPropertyChanged(nameof(ShowMakeLocalButton));
    }

    private async Task MakeLocalAsync()
    {
        if (!_editingEventId.HasValue) return;
        IsBusy = true;
        try
        {
            var existing = await _eventRepository.GetByIdAsync(_editingEventId.Value);
            if (existing == null) return;

            // Delete from Google if this is a writable event
            if (!string.IsNullOrEmpty(existing.GoogleEventId) && !_isReadOnlyGoogleEvent && _authService.IsSignedIn)
                await _googleCalendarService.DeleteEventAsync(existing.GoogleEventId, existing.CalendarId);

            // Clear all Google fields
            existing.GoogleEventId = string.Empty;
            existing.GoogleAccountAlias = string.Empty;
            existing.CalendarId = string.Empty;
            existing.ETag = string.Empty;
            existing.SyncStatus = SyncStatus.Synced;
            existing.LastModifiedUtc = DateTime.UtcNow;
            await _eventRepository.UpdateAsync(existing);

            // Refresh editor state
            _isReadOnlyGoogleEvent = false;
            _editingOriginalCalendarId = string.Empty;
            IsGoogleEvent = false;
            EventSourceText = "Local event";
            SelectedGoogleCalendarIndex = 0;
            OnPropertyChanged(nameof(ShowGoogleCalendarSection));
            OnPropertyChanged(nameof(ShowMakeLocalButton));
            OnPropertyChanged(nameof(GoogleCalendarPickerItems));
            OnPropertyChanged(nameof(GoogleCalendarPickerHeader));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to make local: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static RecurrenceRule ParseSimpleRRule(string rrule)
    {
        var rule = rrule.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)
            ? rrule.Substring(6)
            : rrule;

        var parts = rule.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);

        var recurrence = new RecurrenceRule { RRule = rrule };

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

        if (parts.TryGetValue("BYDAY", out var byDay))
            recurrence.ByDay = byDay;

        if (parts.TryGetValue("UNTIL", out var until))
        {
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

        return recurrence;
    }
}

// ========== Supporting Types ==========

/// <summary>Sent when the user saves or deletes an event so MainViewModel can reload the view.</summary>
public record EventEditorClosedMessage;

public record EventColorOption(string Hex, string Name);

public record ReminderOption(int MinutesBefore, string Display);

public record RecurrenceOption(string? RRule, string Display);
