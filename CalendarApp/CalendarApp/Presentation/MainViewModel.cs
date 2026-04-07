using CalendarApp;
using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services.Calendar;
using CalendarApp.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace CalendarApp.Presentation;

/// <summary>
/// Main view model for the calendar application.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly INavigator _navigator;
    private readonly Func<CalendarMode, ICalendarCalculationService> _calendarServiceFactory;
    private readonly IEventRepository _eventRepository;
    private readonly ILocationService _locationService;
    private readonly IAstronomicalService _astronomicalService;
    private readonly IGoogleAuthService _authService;
    private readonly ISyncService _syncService;
    private readonly CalendarDbContext _dbContext;
    private readonly IBiblicalHolidayService _holidayService;
    private ICalendarCalculationService _calendarService;
    // Kept as a dedicated field so GetLunarConjunctionDisplay can always read the user's
    // selected location timezone, even when the active mode is Gregorian or Julian.
    private readonly BiblicalCalendarService _biblicalService;
    private bool _useLastSelectedCalendarType;
    // Set of Google calendar IDs the user has unchecked; events from these are hidden at display time.
    private HashSet<string> _disabledGoogleCalendarIds = new(StringComparer.OrdinalIgnoreCase);
    // Maps CalendarId → effective hex color (UserColorHex if set, else Google ColorHex).
    private readonly Dictionary<string, string> _calendarColorMap = new(StringComparer.OrdinalIgnoreCase);
    // Captured on the UI thread so we can safely dispatch back to it from background threads.
    private readonly DispatcherQueue? _uiDispatcher;

    // Fires a background sync while signed in.
    // Non-metered connection: every 30 seconds.  Metered connection: every 15 minutes.
    // Uses System.Threading.Timer (thread-pool); SyncAsync is I/O-bound and proven to
    // work from non-UI threads in Uno Platform (startup sync follows the same path).
    private System.Threading.Timer? _syncTimer;
    private const int AutoSyncIntervalSecondsNonMetered = 30;
    private const int AutoSyncIntervalMinutesMetered    = 15;

    // Sentinel value (-1) ensures all mode buttons start in the inactive (false) state.
    // The real mode is set by RestoreDefaultCalendarModeAsync before view data loads.
    [ObservableProperty]
    private CalendarMode _currentCalendarMode = (CalendarMode)(-1);

    [ObservableProperty]
    private CalendarViewType _currentViewType = CalendarViewType.Month;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _displayDate = DateTime.Today;

    [ObservableProperty]
    private string _displayMonthYear = string.Empty;

    [ObservableProperty]
    private string _navigationTitle = string.Empty;

    // Month view
    [ObservableProperty]
    private ObservableCollection<CalendarDayViewModel> _monthDays = new();

    // Week view
    [ObservableProperty]
    private ObservableCollection<WeekDayColumnViewModel> _weekColumns = new();

    // Day view
    [ObservableProperty]
    private ObservableCollection<HourSlotViewModel> _dayHours = new();

    [ObservableProperty]
    private string _dayViewDateDisplay = string.Empty;

    // Year view
    [ObservableProperty]
    private ObservableCollection<YearMonthViewModel> _yearMonths = new();

    [ObservableProperty]
    private string _yearDisplay = string.Empty;

    // Agenda view
    [ObservableProperty]
    private ObservableCollection<AgendaGroupViewModel> _agendaGroups = new();

    // Events for selected date
    [ObservableProperty]
    private ObservableCollection<CalendarEventViewModel> _selectedDateEvents = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _crossReferenceText;

    // View visibility properties
    [ObservableProperty]
    private bool _isMonthView = true;

    [ObservableProperty]
    private bool _isWeekView;

    [ObservableProperty]
    private bool _isDayView;

    [ObservableProperty]
    private bool _isYearView;

    [ObservableProperty]
    private bool _isAgendaView;

    // Week view header visibility
    [ObservableProperty]
    private bool _showDayOfWeekHeaders = true;

    // Calendar mode active states — computed from CurrentCalendarMode so that the
    // initial value is always correct without requiring a PropertyChanged round-trip.
    public bool IsGregorianMode => CurrentCalendarMode == CalendarMode.Gregorian;
    public bool IsJulianMode    => CurrentCalendarMode == CalendarMode.Julian;
    public bool IsBiblicalMode  => CurrentCalendarMode == CalendarMode.Biblical;

    // The cross-calendar filter strip is always visible (there are always other calendar types to show).
    public bool ShowEventFilterStrip => true;

    // Biblical Holidays toggle is hidden when already in Biblical mode
    // (holidays are always visible on the Biblical calendar).
    public bool IsBiblicalHolidaysToggleVisible => !IsBiblicalMode;

    // Day view extra info
    [ObservableProperty]
    private string? _dayViewLunarConjunctionDisplay;

    [ObservableProperty]
    private string? _dayViewBiblicalDayStartDisplay;

    [ObservableProperty]
    private string? _dayViewCrescentIlluminationDisplay;

    [ObservableProperty]
    private string? _dayViewAstronomicalEventDisplay;

    [ObservableProperty]
    private string? _dayViewHolidayDisplay;

    // Year jump input
    [ObservableProperty]
    private string _yearInputText = string.Empty;

    // Google sync state
    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _googleAccountEmail = string.Empty;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText = string.Empty;

    // Cross-calendar event visibility toggles
    // When enabled the current calendar also shows events scoped to that calendar type.
    [ObservableProperty]
    private bool _showGregorianEvents;

    [ObservableProperty]
    private bool _showJulianEvents;

    [ObservableProperty]
    private bool _showBiblicalEvents;

    [ObservableProperty]
    private bool _showBiblicalHolidays;

    public MainViewModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        INavigator navigator,
        Func<CalendarMode, ICalendarCalculationService> calendarServiceFactory,
        IEventRepository eventRepository,
        ILocationService locationService,
        IAstronomicalService astronomicalService,
        IGoogleAuthService authService,
        ISyncService syncService,
        CalendarDbContext dbContext,
        IBiblicalHolidayService holidayService)
    {
        Console.WriteLine("[CalendarApp] MainViewModel constructor starting...");
        _navigator = navigator;
        _calendarServiceFactory = calendarServiceFactory;
        _eventRepository = eventRepository;
        _locationService = locationService;
        _astronomicalService = astronomicalService;
        _authService = authService;
        _syncService = syncService;
        _dbContext = dbContext;
        _holidayService = holidayService;
        _calendarService = calendarServiceFactory(CalendarMode.Gregorian);
        _biblicalService = (BiblicalCalendarService)calendarServiceFactory(CalendarMode.Biblical);
        _uiDispatcher = DispatcherQueue.GetForCurrentThread();

        Title = localizer["ApplicationName"];
        Console.WriteLine($"[CalendarApp] Title = {Title}");

        PreviousCommand = new AsyncRelayCommand(NavigatePreviousAsync);
        NextCommand = new AsyncRelayCommand(NavigateNextAsync);
        TodayCommand = new AsyncRelayCommand(NavigateToTodayAsync);
        SelectDateCommand = new AsyncRelayCommand<CalendarDayViewModel>(SelectDateAsync);
        ChangeViewCommand = new RelayCommand<string>(ChangeViewFromString);
        ChangeCalendarModeCommand = new AsyncRelayCommand<string>(ChangeCalendarModeFromStringAsync);
        SignInCommand = new AsyncRelayCommand(SignInAsync);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync);
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        NewEventCommand = new AsyncRelayCommand(NewEventAsync);
        EditEventCommand = new AsyncRelayCommand<CalendarEventViewModel>(EditEventAsync);
        SettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        JumpToYearCommand = new AsyncRelayCommand(JumpToYearAsync);
        ToggleGregorianEventsCommand = new AsyncRelayCommand(ToggleGregorianEventsAsync);
        ToggleJulianEventsCommand = new AsyncRelayCommand(ToggleJulianEventsAsync);
        ToggleBiblicalEventsCommand = new AsyncRelayCommand(ToggleBiblicalEventsAsync);
        ToggleBiblicalHolidaysCommand = new AsyncRelayCommand(ToggleBiblicalHolidaysAsync);

        _syncService.SyncStatusChanged += (_, e) =>
        {
            SyncStatusText = e.Message;
        };

        // Recompute the calendar whenever the user closes Settings (location or mode may have changed).
        WeakReferenceMessenger.Default.Register<SettingsClosedMessage>(this, (_, msg) =>
        {
            _ = RefreshAfterSettingsClosedAsync(msg.HasGoogleChanges);
        });

        // Reload view data whenever the user saves or deletes an event.
        WeakReferenceMessenger.Default.Register<EventEditorClosedMessage>(this, (_, _) =>
        {
            _ = LoadViewDataAsync();
        });

        // Check existing sign-in state
        IsSignedIn = _authService.IsSignedIn;
        GoogleAccountEmail = _authService.UserEmail ?? string.Empty;

        // Set initial title immediately (no async needed)
        UpdateNavigationTitle();

        // Fire-and-forget initial data load on UI thread
        _ = InitialLoadAsync();
    }

    private async Task InitialLoadAsync()
    {
        try
        {
            // Small delay to let the UI render first
            await Task.Delay(100);

            SyncDiagnosticLog.StartSession();
            SyncDiagnosticLog.Write($"InitialLoadAsync: starting — IsSignedIn={IsSignedIn}");

            // Restart the sync timer whenever the network type changes (metered ↔ non-metered).
            try
            {
                Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged
                    += OnNetworkStatusChanged;
            }
            catch { /* not supported on this platform — interval stays fixed */ }

            // Load persisted default calendar mode
            await RestoreDefaultCalendarModeAsync();

            // Populate the location cache used by GetLunarConjunctionDisplay on all calendar modes.
            await _biblicalService.InitializeLocationAsync();

            // Load which Google calendars are disabled (used to filter events at display time)
            // and load the per-calendar color map.
            await LoadDisabledCalendarIdsAsync();
            await LoadCalendarColorsAsync();

            // Try to silently restore a previous Google sign-in (no browser opened).
            // If successful, kick off a background sync so fresh events appear.
            var restored = await _authService.TryRestoreSignInAsync();
            IsSignedIn        = _authService.IsSignedIn;
            GoogleAccountEmail = _authService.UserEmail ?? string.Empty;

            // Load the view with locally-stored events first (fast).
            await LoadViewDataAsync();

            // Then sync in the background so newly-pulled events appear without blocking startup.
            // Also start the periodic timer so changes made in Google Calendar while the app is
            // running (adds, edits, deletes) are picked up automatically.
            // NOTE: do NOT use Task.Run here — SyncAsync is I/O-bound and uses async/await
            // throughout, so it never blocks the UI thread.  Task.Run would run it with a null
            // SynchronizationContext, causing every continuation (including the IsSyncing flag
            // reset) to run on a thread-pool thread, which can cause the timer's IsSyncing guard
            // to see a stale value and skip every tick while the startup sync is in-flight.
            if (IsSignedIn)
            {
                SyncDiagnosticLog.Write("InitialLoadAsync: signed in — firing startup sync and starting timer");
                _ = SyncAsync();   // fire-and-forget on the UI thread
                StartSyncTimer();
            }
            else
            {
                SyncDiagnosticLog.Write("InitialLoadAsync: not signed in — skipping sync");
            }

            Console.WriteLine("[CalendarApp] Initial data load complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Initial load error: {ex}");
        }
    }

    private async Task RestoreDefaultCalendarModeAsync()
    {
        try
        {
            await _dbContext.InitializeAsync();

            // Load the "use last selected" preference
            var useLastSetting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "UseLastSelectedCalendarType");
            _useLastSelectedCalendarType = useLastSetting?.Value == "true";

            // Restore the Biblical holidays toggle
            var holidaysSetting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "ShowBiblicalHolidays");
            ShowBiblicalHolidays = holidaysSetting?.Value == "true";

            // Restore the cross-calendar event overlay toggles
            var gregSetting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "ShowGregorianEvents");
            ShowGregorianEvents = gregSetting?.Value == "true";

            var julianSetting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "ShowJulianEvents");
            ShowJulianEvents = julianSetting?.Value == "true";

            var biblicalSetting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "ShowBiblicalEvents");
            ShowBiblicalEvents = biblicalSetting?.Value == "true";

            var setting = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "DefaultCalendarMode");

            if (setting?.Value != null)
            {
                var mode = setting.Value switch
                {
                    "Julian" => CalendarMode.Julian,
                    "Biblical" => CalendarMode.Biblical,
                    _ => CalendarMode.Gregorian
                };

                if (mode != CurrentCalendarMode)
                {
                    Console.WriteLine($"[CalendarApp] Restoring calendar mode: {mode}");
                    CurrentCalendarMode = mode;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error restoring calendar mode: {ex}");
        }
    }

    public string? Title { get; }

    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand SelectDateCommand { get; }
    public ICommand ChangeViewCommand { get; }
    public ICommand ChangeCalendarModeCommand { get; }
    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand NewEventCommand { get; }
    public ICommand EditEventCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand JumpToYearCommand { get; }
    public ICommand ToggleGregorianEventsCommand { get; }
    public ICommand ToggleJulianEventsCommand { get; }
    public ICommand ToggleBiblicalEventsCommand { get; }
    public ICommand ToggleBiblicalHolidaysCommand { get; }

    public string[] CalendarModeNames => new[] { "Gregorian", "Julian", "Biblical" };
    public string[] ViewTypeNames => new[] { "Day", "Week", "Month", "Year", "Agenda" };

    // ComboBox selection indices (kept in sync with CurrentCalendarMode / CurrentViewType)
    [ObservableProperty]
    private int _selectedCalendarModeIndex;   // 0=Gregorian, 1=Julian, 2=Biblical

    [ObservableProperty]
    private int _selectedViewTypeIndex = 2;   // 0=Day,1=Week,2=Month,3=Year,4=Agenda

    partial void OnSelectedCalendarModeIndexChanged(int value)
    {
        string mode = value switch { 1 => "Julian", 2 => "Biblical", _ => "Gregorian" };
        _ = ChangeCalendarModeFromStringAsync(mode);
    }

    partial void OnSelectedViewTypeIndexChanged(int value)
    {
        string view = value switch
        {
            0 => "Day", 1 => "Week", 2 => "Month", 3 => "Year", 4 => "Agenda", _ => "Month"
        };
        ChangeViewFromString(view);
    }

    partial void OnCurrentCalendarModeChanged(CalendarMode value)
    {
        Console.WriteLine($"[CalendarApp] Calendar mode changed to: {value}");
        _calendarService = _calendarServiceFactory(value);

        // Keep the dropdown in sync
        SelectedCalendarModeIndex = value switch { CalendarMode.Julian => 1, CalendarMode.Biblical => 2, _ => 0 };

        // Notify computed mode-indicator properties so button bindings refresh
        OnPropertyChanged(nameof(IsGregorianMode));
        OnPropertyChanged(nameof(IsJulianMode));
        OnPropertyChanged(nameof(IsBiblicalMode));
        OnPropertyChanged(nameof(IsBiblicalHolidaysToggleVisible));

        // Reset busy flag in case a previous load is stuck
        IsBusy = false;
        _ = OnCalendarModeChangedAsync(value);
    }

    private async Task OnCalendarModeChangedAsync(CalendarMode value)
    {
        try
        {
            // If "use last selected" is enabled, save the mode to DB
            if (_useLastSelectedCalendarType)
            {
                var modeStr = value switch
                {
                    CalendarMode.Julian => "Julian",
                    CalendarMode.Biblical => "Biblical",
                    _ => "Gregorian"
                };
                await SaveSettingAsync("DefaultCalendarMode", modeStr);
            }

            // Refresh the location cache (used by new moon display on all calendar modes).
            await _biblicalService.InitializeLocationAsync();
            await LoadViewDataAsync();

            // Re-notify mode indicators after all async work has settled, dispatched at
            // Low priority on the UI thread so it runs after all pending data/layout
            // updates. This fixes Gregorian (and any mode) when PropertyChanged fired
            // on a background thread during startup is processed out of order.
            _uiDispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                OnPropertyChanged(nameof(IsGregorianMode));
                OnPropertyChanged(nameof(IsJulianMode));
                OnPropertyChanged(nameof(IsBiblicalMode));
                OnPropertyChanged(nameof(IsBiblicalHolidaysToggleVisible));
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error changing calendar mode: {ex}");
        }
    }

    partial void OnCurrentViewTypeChanged(CalendarViewType value)
    {
        Console.WriteLine($"[CalendarApp] View type changed to: {value}");
        // Keep the dropdown in sync
        SelectedViewTypeIndex = (int)value;
        IsMonthView = value == CalendarViewType.Month;
        IsWeekView = value == CalendarViewType.Week;
        IsDayView = value == CalendarViewType.Day;
        IsYearView = value == CalendarViewType.Year;
        IsAgendaView = value == CalendarViewType.Agenda;
        ShowDayOfWeekHeaders = value == CalendarViewType.Month || value == CalendarViewType.Week;
        // Reset busy flag in case a previous load is stuck
        IsBusy = false;
        _ = LoadViewDataAsync();
    }

    partial void OnDisplayDateChanged(DateTime value)
    {
        UpdateNavigationTitle();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadEventsForSelectedDateAsync();
        UpdateCrossReference();
    }

    private int _loadGeneration;

    private async Task LoadViewDataAsync()
    {
        // Increment generation to cancel any in-flight loads
        var generation = ++_loadGeneration;

        // Refresh Google auth state so the sync button reflects sign-ins done from Settings
        IsSignedIn        = _authService.IsSignedIn;
        GoogleAccountEmail = _authService.UserEmail ?? string.Empty;

        IsBusy = true;
        try
        {
            // If another load was requested while we were starting, bail
            if (generation != _loadGeneration) return;

            Console.WriteLine($"[CalendarApp] LoadViewDataAsync gen={generation} mode={CurrentCalendarMode} view={CurrentViewType} date={DisplayDate:yyyy-MM-dd}");

            // Update title/header in its own try/catch so a failure here never blocks data loading
            try
            {
                UpdateNavigationTitle();
                Console.WriteLine($"[CalendarApp] NavigationTitle set to: '{NavigationTitle}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarApp] ERROR in UpdateNavigationTitle: {ex}");
                // Set a safe fallback so the header is never blank
                NavigationTitle = $"{DisplayDate:MMMM yyyy}";
                DisplayMonthYear = NavigationTitle;
            }

            try
            {
                UpdateCrossReference();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarApp] ERROR in UpdateCrossReference: {ex}");
            }

            if (generation != _loadGeneration) return;

            switch (CurrentViewType)
            {
                case CalendarViewType.Month:
                    await LoadMonthDataAsync();
                    break;
                case CalendarViewType.Week:
                    await LoadWeekDataAsync();
                    break;
                case CalendarViewType.Day:
                    await LoadDayDataAsync();
                    break;
                case CalendarViewType.Year:
                    await LoadYearDataAsync();
                    break;
                case CalendarViewType.Agenda:
                    await LoadAgendaDataAsync();
                    break;
            }

            Console.WriteLine($"[CalendarApp] LoadViewDataAsync gen={generation} complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] ERROR in LoadViewDataAsync gen={generation}: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========== GOOGLE CALENDAR FILTER HELPERS ==========

    /// <summary>
    /// Loads the set of disabled Google calendar IDs from the local DB so events
    /// from unchecked calendars can be filtered out at display time.
    /// </summary>
    private async Task LoadDisabledCalendarIdsAsync()
    {
        try
        {
            await _dbContext.InitializeAsync();
            var disabled = await _dbContext.Connection
                .Table<GoogleCalendarListEntity>()
                .Where(c => !c.IsEnabled)
                .ToListAsync();
            _disabledGoogleCalendarIds = new HashSet<string>(
                disabled.Select(c => c.CalendarId), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _disabledGoogleCalendarIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Loads the CalendarId → hex-color map from the local DB.
    /// UserColorHex takes precedence over the Google-assigned ColorHex.
    /// </summary>
    private async Task LoadCalendarColorsAsync()
    {
        try
        {
            await _dbContext.InitializeAsync();
            var entities = await _dbContext.Connection
                .Table<GoogleCalendarListEntity>()
                .ToListAsync();
            _calendarColorMap.Clear();
            foreach (var e in entities)
            {
                var hex = !string.IsNullOrEmpty(e.UserColorHex) ? e.UserColorHex : e.ColorHex;
                if (!string.IsNullOrEmpty(hex))
                    _calendarColorMap[e.CalendarId] = hex;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error loading calendar colors: {ex}");
        }
    }

    /// <summary>Creates a CalendarEventViewModel with the correct per-calendar color and display timezone.</summary>
    private CalendarEventViewModel MakeEventVm(CalendarEvent e)
    {
        _calendarColorMap.TryGetValue(e.CalendarId, out var calColorHex);
        return new CalendarEventViewModel(e, calColorHex ?? string.Empty, GetDisplayTimezone());
    }

    /// <summary>
    /// Returns the timezone to use when displaying event times.
    /// Uses the timezone geocoded from the user's configured location, with system local as fallback.
    /// </summary>
    private TimeZoneInfo GetDisplayTimezone()
    {
        try { return _biblicalService.GetCurrentLocation().TimeZone; }
        catch { return TimeZoneInfo.Local; }
    }

    /// <summary>
    /// Gets events for a date range and filters out any that belong to a Google
    /// calendar the user has unchecked. Locally-created events (no CalendarId) always show.
    /// </summary>
    private async Task<IEnumerable<CalendarEvent>> GetFilteredEventsAsync(DateTime start, DateTime end)
    {
        var events = await _eventRepository.GetEventsForDateRangeAsync(
            start, end, CurrentCalendarMode, GetAdditionalVisibleModes());

        if (_disabledGoogleCalendarIds.Count == 0) return events;

        return events.Where(e =>
            string.IsNullOrEmpty(e.CalendarId) ||
            !_disabledGoogleCalendarIds.Contains(e.CalendarId));
    }

    /// <summary>
    /// Builds a date → events map that correctly spans multi-day events across every
    /// calendar date they cover, not just their start date.
    /// All-day events follow the Google convention where EndDateTime is midnight of the
    /// day AFTER the last visible day, so they are excluded on that midnight date.
    ///
    /// Google events are stored as UTC (DateTimeOffset.UtcDateTime). This method
    /// converts their times to the display timezone before computing the date key,
    /// so an 8 pm Eastern event stored as the following day in UTC appears on the
    /// correct local calendar date.
    ///
    /// When <paramref name="sunsetByDate"/> is provided (Biblical mode), timed events
    /// whose local start falls at or after sunset are shifted forward one cell —
    /// reflecting the Biblical day that begins at that sunset.
    /// </summary>
    private Dictionary<DateTime, List<CalendarEvent>> BuildEventsByDate(
        IEnumerable<CalendarEvent> events,
        Dictionary<DateTime, DateTime>? sunsetByDate = null)
    {
        var tz = GetDisplayTimezone();
        var dict = new Dictionary<DateTime, List<CalendarEvent>>();
        foreach (var evt in events)
        {
            // Resolve local start/end times.
            // Google timed events are stored as UTC; local events use local/unspecified Kind.
            DateTime startLocal, endLocal;
            if (!evt.IsAllDay && !string.IsNullOrEmpty(evt.GoogleEventId))
            {
                startLocal = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(evt.StartDateTime, DateTimeKind.Utc), tz);
                endLocal = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(evt.EndDateTime, DateTimeKind.Utc), tz);
            }
            else
            {
                startLocal = evt.StartDateTime;
                endLocal   = evt.EndDateTime;
            }

            var startDay = startLocal.Date;
            var endDay   = endLocal.Date;

            // Biblical mode: a timed event whose local start falls at or after sunset
            // belongs to the next Biblical day (shown on the following Gregorian cell).
            bool wasShifted = false;
            if (sunsetByDate != null && !evt.IsAllDay
                && sunsetByDate.TryGetValue(startLocal.Date, out var sunsetLocal)
                && startLocal.TimeOfDay >= sunsetLocal.TimeOfDay)
            {
                startDay = startDay.AddDays(1);
                wasShifted = true;
            }

            // After a Biblical shift, a short post-sunset event (e.g. 8 pm–9 pm March 5)
            // has endDay = March 5 but startDay = March 6, so the loop would never execute.
            // Clamp endDay up to startDay so the loop runs at least once.
            if (endDay < startDay) endDay = startDay;

            for (var day = startDay; day <= endDay; day = day.AddDays(1))
            {
                // Exclude the day if the event has already ended before this day started.
                // For the first cell of a shifted event the day begins at the preceding
                // sunset (not midnight), so skip the midnight check there — the sunset
                // guard above already confirms the event starts inside this Biblical day.
                bool isFirstShiftedCell = wasShifted && day == startDay;
                if (!isFirstShiftedCell && endLocal <= day) break;

                if (!dict.TryGetValue(day, out var list))
                {
                    list = new List<CalendarEvent>();
                    dict[day] = list;
                }
                list.Add(evt);
            }
        }
        return dict;
    }

    /// <summary>
    /// Precomputes local sunset times for each Gregorian date in [start-1, end].
    /// Returns null when not in Biblical mode (no sunset shifting needed).
    ///
    /// The range intentionally starts one day before <paramref name="start"/> so that
    /// events whose local Gregorian date is the day before the first visible cell —
    /// but whose start time is after that day's sunset (and therefore belongs to the
    /// first visible Biblical day) — are still shifted into the correct cell.
    /// </summary>
    private Dictionary<DateTime, DateTime>? GetSunsetTimesForRange(DateTime start, DateTime end)
    {
        if (CurrentCalendarMode != CalendarMode.Biblical) return null;

        var result = new Dictionary<DateTime, DateTime>();
        try
        {
            var location = _biblicalService.GetCurrentLocation();
            // Start one day early to cover events that straddle the grid boundary at sunset.
            for (var date = start.Date.AddDays(-1); date <= end.Date; date = date.AddDays(1))
            {
                try
                {
                    result[date] = _astronomicalService.CalculateSunset(date, location);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    // ========== MONTH VIEW ==========

    private async Task LoadMonthDataAsync()
    {
        Console.WriteLine($"[CalendarApp] LoadMonthDataAsync starting for {CurrentCalendarMode}...");
        CalendarDate calendarDate;
        try
        {
            calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
            Console.WriteLine($"[CalendarApp] Display date → {calendarDate.Year}/{calendarDate.Month}/{calendarDate.Day}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] ERROR in GetDateForDateTime (month): {ex}");
            MonthDays = new ObservableCollection<CalendarDayViewModel>();
            return;
        }

        List<CalendarDate> monthGrid;
        try
        {
            monthGrid = await Task.Run(() => _calendarService.GetMonthGrid(calendarDate.Year, calendarDate.Month).ToList());
            Console.WriteLine($"[CalendarApp] Month grid computed: {monthGrid.Count} cells");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] ERROR in GetMonthGrid: {ex}");
            MonthDays = new ObservableCollection<CalendarDayViewModel>();
            return;
        }

        var firstDate = monthGrid.First().GregorianEquivalent;
        var lastDate = monthGrid.Last().GregorianEquivalent.AddDays(1);
        var events = await GetFilteredEventsAsync(firstDate, lastDate);
        var sunsetByDate = GetSunsetTimesForRange(firstDate, lastDate);
        var eventsByDate = BuildEventsByDate(events, sunsetByDate);
        var holidays = await GetBiblicalHolidaysForRangeAsync(firstDate, lastDate);

        var dayViewModels = new ObservableCollection<CalendarDayViewModel>();
        var today = DateTime.Today;

        foreach (var date in monthGrid)
        {
            var isCurrentMonth = date.Month == calendarDate.Month;
            var isToday = date.GregorianEquivalent.Date == today;
            var isSelected = date.GregorianEquivalent.Date == SelectedDate.Date;

            var dayEvents = eventsByDate.TryGetValue(date.GregorianEquivalent.Date, out var evts)
                ? evts.Select(e => MakeEventVm(e)).ToList()
                : new List<CalendarEventViewModel>();

            holidays.TryGetValue(date.GregorianEquivalent.Date, out var holidayName);

            dayViewModels.Add(new CalendarDayViewModel
            {
                CalendarDate = date,
                Day = date.Day,
                IsCurrentMonth = isCurrentMonth,
                IsToday = isToday,
                IsSelected = isSelected,
                CrossReference = FormatCrossReference(date.CrossReference),
                LunarConjunctionDisplay = GetLunarConjunctionDisplay(date.GregorianEquivalent),
                CrescentIlluminationDisplay = GetCrescentIlluminationDisplay(date.GregorianEquivalent),
                DayStartDisplay = CurrentCalendarMode == CalendarMode.Biblical
                    ? GetBiblicalDayStartDisplay(date.GregorianEquivalent, compact: true) : null,
                AstronomicalEventDisplay = GetAstronomicalEventDisplay(date.GregorianEquivalent),
                HolidayDisplay = holidayName,
                Events = new ObservableCollection<CalendarEventViewModel>(dayEvents)
            });
        }

        MonthDays = dayViewModels;
    }

    // ========== WEEK VIEW ==========

    private async Task LoadWeekDataAsync()
    {
        Console.WriteLine($"[CalendarApp] LoadWeekDataAsync starting for {CurrentCalendarMode}...");
        CalendarDate calendarDate;
        List<CalendarDate> weekDays;
        try
        {
            calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
            weekDays = await Task.Run(() => _calendarService.GetWeekDays(calendarDate).ToList());
            Console.WriteLine($"[CalendarApp] Week days computed: {weekDays.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] ERROR in LoadWeekDataAsync setup: {ex}");
            WeekColumns = new ObservableCollection<WeekDayColumnViewModel>();
            return;
        }

        var firstDate = weekDays.First().GregorianEquivalent;
        var lastDate = weekDays.Last().GregorianEquivalent.AddDays(1);
        var events = await GetFilteredEventsAsync(firstDate, lastDate);
        // No Biblical sunset shifting here: the week view places events by vm.StartTime
        // within midnight-based hour slots, so shifting the cell would orphan the event.
        var eventsByDate = BuildEventsByDate(events);
        var holidays = await GetBiblicalHolidaysForRangeAsync(firstDate, lastDate);

        var today = DateTime.Today;
        var columns = new ObservableCollection<WeekDayColumnViewModel>();

        foreach (var date in weekDays)
        {
            var dayEvents = eventsByDate.TryGetValue(date.GregorianEquivalent.Date, out var evts)
                ? evts.ToList()
                : new List<CalendarEvent>();

            // Convert to VMs first so StartTime/EndTime reflect the display timezone
            // (Google events are stored as UTC; the VM converts them to local time).
            var dayVms = dayEvents.Select(e => MakeEventVm(e)).ToList();

            var hours = new ObservableCollection<HourSlotViewModel>();
            for (int h = 0; h < 24; h++)
            {
                var hourStart = date.GregorianEquivalent.Date.AddHours(h);
                var hourEnd = hourStart.AddHours(1);
                var hourEvents = dayVms
                    .Where(vm => !vm.IsAllDay && vm.StartTime < hourEnd && vm.EndTime > hourStart)
                    .ToList();

                hours.Add(new HourSlotViewModel
                {
                    Hour = h,
                    HourDisplay = h == 0 ? "12 AM" : h < 12 ? $"{h} AM" : h == 12 ? "12 PM" : $"{h - 12} PM",
                    Events = new ObservableCollection<CalendarEventViewModel>(hourEvents),
                    HasEvents = hourEvents.Count > 0
                });
            }

            var allDayEvents = dayVms.Where(vm => vm.IsAllDay).ToList();

            holidays.TryGetValue(date.GregorianEquivalent.Date, out var weekHolidayName);
            columns.Add(new WeekDayColumnViewModel
            {
                CalendarDate = date,
                DayOfWeek = date.GregorianEquivalent.DayOfWeek.ToString().Substring(0, 3),
                DayNumber = date.Day,
                IsToday = date.GregorianEquivalent.Date == today,
                IsSelected = date.GregorianEquivalent.Date == SelectedDate.Date,
                CrossReference = FormatCrossReference(date.CrossReference),
                LunarConjunctionDisplay = GetLunarConjunctionDisplay(date.GregorianEquivalent),
                CrescentIlluminationDisplay = GetCrescentIlluminationDisplay(date.GregorianEquivalent),
                DayStartDisplay = CurrentCalendarMode == CalendarMode.Biblical
                    ? GetBiblicalDayStartDisplay(date.GregorianEquivalent, compact: true) : null,
                AstronomicalEventDisplay = GetAstronomicalEventDisplay(date.GregorianEquivalent),
                HolidayDisplay = weekHolidayName,
                AllDayEvents = new ObservableCollection<CalendarEventViewModel>(allDayEvents),
                HourSlots = hours
            });
        }

        WeekColumns = columns;
    }

    // ========== DAY VIEW ==========

    private async Task LoadDayDataAsync()
    {
        var calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
        var startOfDay = DisplayDate.Date;
        var endOfDay = startOfDay.AddDays(1);

        // Expand the query by ±1 day so Google events stored as UTC on an adjacent
        // calendar date are fetched. The hour-slot filter below (using vm.StartTime,
        // which is already converted to local time) places them in the correct slots.
        var events = await GetFilteredEventsAsync(startOfDay.AddDays(-1), endOfDay.AddDays(1));
        var eventsList = events.ToList();

        // Convert to VMs first so StartTime/EndTime reflect the display timezone.
        var dayVms = eventsList.Select(e => MakeEventVm(e)).ToList();

        var hours = new ObservableCollection<HourSlotViewModel>();
        for (int h = 0; h < 24; h++)
        {
            var hourStart = startOfDay.AddHours(h);
            var hourEnd = hourStart.AddHours(1);
            var hourEvents = dayVms
                .Where(vm => !vm.IsAllDay && vm.StartTime < hourEnd && vm.EndTime > hourStart)
                .ToList();

            hours.Add(new HourSlotViewModel
            {
                Hour = h,
                HourDisplay = h == 0 ? "12 AM" : h < 12 ? $"{h} AM" : h == 12 ? "12 PM" : $"{h - 12} PM",
                Events = new ObservableCollection<CalendarEventViewModel>(hourEvents),
                HasEvents = hourEvents.Count > 0
            });
        }

        DayHours = hours;

        var dayName = DisplayDate.ToString("dddd");
        var monthName = _calendarService.GetMonthName(calendarDate.Month);
        DayViewDateDisplay = $"{dayName}, {monthName} {calendarDate.Day}";
        DayViewLunarConjunctionDisplay = GetLunarConjunctionDisplay(DisplayDate);
        DayViewCrescentIlluminationDisplay = GetCrescentIlluminationDisplay(DisplayDate);
        DayViewBiblicalDayStartDisplay = CurrentCalendarMode == CalendarMode.Biblical
            ? GetBiblicalDayStartDisplay(DisplayDate) : null;
        DayViewAstronomicalEventDisplay = GetAstronomicalEventDisplay(DisplayDate);
        var dayHolidays = await GetBiblicalHolidaysForRangeAsync(startOfDay, endOfDay);
        dayHolidays.TryGetValue(startOfDay, out var dayHolName);
        DayViewHolidayDisplay = dayHolName;
    }

    // ========== YEAR VIEW ==========

    private async Task LoadYearDataAsync()
    {
        Console.WriteLine($"[CalendarApp] LoadYearDataAsync starting for {CurrentCalendarMode}...");
        CalendarDate calendarDate;
        int year;
        int monthsInYear;
        CalendarDate yearStart;
        DateTime yearEndDate;
        try
        {
            calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
            year = calendarDate.Year;
            monthsInYear = await Task.Run(() => _calendarService.GetMonthsInYear(year));
            yearStart = await Task.Run(() => _calendarService.GetFirstDayOfYear(year));
            yearEndDate = monthsInYear > 0
                ? (await Task.Run(() => _calendarService.GetLastDayOfMonth(year, monthsInYear))).GregorianEquivalent.AddDays(1)
                : DisplayDate.Date.AddYears(1);
            Console.WriteLine($"[CalendarApp] Year {year} has {monthsInYear} months");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] ERROR in LoadYearDataAsync setup: {ex}");
            YearMonths = new ObservableCollection<YearMonthViewModel>();
            return;
        }

        var events = await GetFilteredEventsAsync(yearStart.GregorianEquivalent, yearEndDate);
        var sunsetByDate = GetSunsetTimesForRange(yearStart.GregorianEquivalent, yearEndDate);
        var eventDates = new HashSet<DateTime>(BuildEventsByDate(events, sunsetByDate).Keys);
        var yearHolidays = await GetBiblicalHolidaysForRangeAsync(yearStart.GregorianEquivalent, yearEndDate);

        // Precompute equinox/solstice dates for the year view (one lookup covers all mini-day cells).
        var astroEvents = new Dictionary<DateTime, string>();
        {
            TimeZoneInfo tz = TimeZoneInfo.Local;
            try { tz = _biblicalService.GetCurrentLocation().TimeZone; }
            catch { }

            // Check the Gregorian year that contains most of this calendar year's dates.
            int gregYear = DisplayDate.Year;
            foreach (int y in new[] { gregYear - 1, gregYear, gregYear + 1 })
            {
                void TryAdd(string name, DateTime utc)
                {
                    try
                    {
                        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
                        astroEvents.TryAdd(local.Date, name);
                    }
                    catch { }
                }
                TryAdd("Spring Equinox",  _astronomicalService.CalculateVernalEquinox(y));
                TryAdd("Summer Solstice", _astronomicalService.CalculateSummerSolstice(y));
                TryAdd("Fall Equinox",    _astronomicalService.CalculateAutumnalEquinox(y));
                TryAdd("Winter Solstice", _astronomicalService.CalculateWinterSolstice(y));
            }
        }

        var months = new ObservableCollection<YearMonthViewModel>();
        var today = DateTime.Today;

        for (int m = 1; m <= monthsInYear; m++)
        {
            var monthName = _calendarService.GetMonthName(m);
            var daysInMonth = _calendarService.GetDaysInMonth(year, m);
            var firstDay = _calendarService.GetFirstDayOfMonth(year, m);
            var firstDayOfWeek = (int)firstDay.GregorianEquivalent.DayOfWeek;

            var miniDays = new ObservableCollection<MiniDayViewModel>();

            // Blank cells for offset
            for (int i = 0; i < firstDayOfWeek; i++)
            {
                miniDays.Add(new MiniDayViewModel { Day = 0, IsBlank = true });
            }

            for (int d = 1; d <= daysInMonth; d++)
            {
                var dayDate = firstDay.GregorianEquivalent.AddDays(d - 1);
                astroEvents.TryGetValue(dayDate.Date, out var astroName);
                yearHolidays.TryGetValue(dayDate.Date, out var miniHolName);
                miniDays.Add(new MiniDayViewModel
                {
                    Day = d,
                    IsToday = dayDate.Date == today,
                    HasEvents = eventDates.Contains(dayDate.Date),
                    GregorianDate = dayDate.Date,
                    IsBlank = false,
                    IsAstronomicalEvent = astroName != null,
                    AstronomicalEventName = astroName,
                    IsHoliday = miniHolName != null,
                    HolidayName = miniHolName
                });
            }

            months.Add(new YearMonthViewModel
            {
                Month = m,
                MonthName = monthName,
                Days = miniDays
            });
        }

        YearMonths = months;
        YearDisplay = year.ToString();
    }

    // ========== AGENDA VIEW ==========

    private async Task LoadAgendaDataAsync()
    {
        var startDate = DisplayDate.Date;
        var endDate = startDate.AddDays(30); // Show 30 days ahead

        var events = await GetFilteredEventsAsync(startDate, endDate);
        var sunsetByDate = GetSunsetTimesForRange(startDate, endDate);
        var eventsByDate = BuildEventsByDate(events.OrderBy(e => e.StartDateTime), sunsetByDate);

        var holidays = await GetBiblicalHolidaysForRangeAsync(startDate, endDate);

        // Collect all dates that should appear in the agenda:
        // days with events + holy days (Biblical mode only, since holidays is empty otherwise)
        var allDates = new HashSet<DateTime>(eventsByDate.Keys);
        foreach (var kvp in holidays)
            allDates.Add(kvp.Key.Date);

        var grouped = allDates
            .OrderBy(d => d)
            .Select(date =>
            {
                var calDate = _calendarService.GetDateForDateTime(date);
                var dayName = date.ToString("dddd");
                var monthName = _calendarService.GetMonthName(calDate.Month);
                var dayEvents = eventsByDate.TryGetValue(date, out var evts) ? evts : new List<CalendarEvent>();
                holidays.TryGetValue(date, out var agendaHolName);
                return new AgendaGroupViewModel
                {
                    Date = date,
                    DateDisplay = $"{dayName}, {monthName} {calDate.Day}",
                    IsToday = date == DateTime.Today,
                    CrossReference = FormatCrossReference(calDate.CrossReference),
                    LunarConjunctionDisplay = GetLunarConjunctionDisplay(date),
                    CrescentIlluminationDisplay = GetCrescentIlluminationDisplay(date),
                    DayStartDisplay = CurrentCalendarMode == CalendarMode.Biblical
                        ? GetBiblicalDayStartDisplay(date) : null,
                    AstronomicalEventDisplay = GetAstronomicalEventDisplay(date),
                    HolidayDisplay = agendaHolName,
                    Events = new ObservableCollection<CalendarEventViewModel>(
                        dayEvents.Select(e => MakeEventVm(e)))
                };
            })
            .ToList();

        // If nothing to show, display a placeholder for today
        if (grouped.Count == 0)
        {
            var todayCalDate = _calendarService.GetDateForDateTime(DateTime.Today);
            var todayName = DateTime.Today.ToString("dddd");
            var todayMonthName = _calendarService.GetMonthName(todayCalDate.Month);
            grouped.Add(new AgendaGroupViewModel
            {
                Date = DateTime.Today,
                DateDisplay = $"{todayName}, {todayMonthName} {todayCalDate.Day}",
                IsToday = true,
                CrossReference = FormatCrossReference(todayCalDate.CrossReference),
                LunarConjunctionDisplay = GetLunarConjunctionDisplay(DateTime.Today),
                CrescentIlluminationDisplay = GetCrescentIlluminationDisplay(DateTime.Today),
                DayStartDisplay = CurrentCalendarMode == CalendarMode.Biblical
                    ? GetBiblicalDayStartDisplay(DateTime.Today, compact: true) : null,
                AstronomicalEventDisplay = GetAstronomicalEventDisplay(DateTime.Today),
                Events = new ObservableCollection<CalendarEventViewModel>()
            });
        }

        AgendaGroups = new ObservableCollection<AgendaGroupViewModel>(grouped);
    }

    // ========== NAVIGATION ==========

    private void UpdateNavigationTitle()
    {
        var calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
        var monthName = _calendarService.GetMonthName(calendarDate.Month);

        NavigationTitle = CurrentViewType switch
        {
            CalendarViewType.Day => $"{monthName} {calendarDate.Day}, {calendarDate.Year}",
            CalendarViewType.Week => $"{monthName} {calendarDate.Year}",
            CalendarViewType.Month => $"{monthName} {calendarDate.Year}",
            CalendarViewType.Year => calendarDate.Year.ToString(),
            CalendarViewType.Agenda => "Agenda",
            _ => $"{monthName} {calendarDate.Year}"
        };

        DisplayMonthYear = NavigationTitle;
    }

    private void UpdateCrossReference()
    {
        var calendarDate = _calendarService.GetDateForDateTime(SelectedDate);
        CrossReferenceText = CurrentCalendarMode != CalendarMode.Gregorian
            ? FormatCrossReference(_calendarService.GetCrossReferenceDisplay(calendarDate))
            : null;
    }

    /// <summary>
    /// Adds a platform-appropriate prefix to a Gregorian cross-reference day number.
    /// Desktop: "Greg:N", Android: just "N" (no prefix in portrait; horizontal handled by layout).
    /// </summary>
    private static string? FormatCrossReference(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
#if __ANDROID__
        return dateStr;
#else
        return $"Greg: {dateStr}";
#endif
    }

    private async Task LoadEventsForSelectedDateAsync()
    {
        var startOfDay = SelectedDate.Date;
        var endOfDay = startOfDay.AddDays(1);

        // Expand the query by ±1 day so Google events stored as UTC on an adjacent
        // calendar date (e.g. 8 pm Eastern = UTC next day 1 am) are still fetched.
        var events = await GetFilteredEventsAsync(startOfDay.AddDays(-1), endOfDay.AddDays(1));

        // Filter in-memory to events whose local display time falls on the selected date.
        var tz = GetDisplayTimezone();
        var localEvents = events.Where(e =>
        {
            DateTime localStart = (!e.IsAllDay && !string.IsNullOrEmpty(e.GoogleEventId))
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.StartDateTime, DateTimeKind.Utc), tz)
                : e.StartDateTime;
            return localStart.Date == startOfDay;
        });

        SelectedDateEvents = new ObservableCollection<CalendarEventViewModel>(
            localEvents.Select(e => MakeEventVm(e)));

        foreach (var day in MonthDays)
        {
            day.IsSelected = day.CalendarDate.GregorianEquivalent.Date == SelectedDate.Date;
        }
    }

    private async Task NavigatePreviousAsync()
    {
        DisplayDate = CurrentViewType switch
        {
            CalendarViewType.Day => DisplayDate.AddDays(-1),
            CalendarViewType.Week => DisplayDate.AddDays(-7),
            CalendarViewType.Month => DisplayDate.AddMonths(-1),
            CalendarViewType.Year => DisplayDate.AddYears(-1),
            CalendarViewType.Agenda => DisplayDate.AddDays(-30),
            _ => DisplayDate.AddMonths(-1)
        };
        await LoadViewDataAsync();
    }

    private async Task NavigateNextAsync()
    {
        DisplayDate = CurrentViewType switch
        {
            CalendarViewType.Day => DisplayDate.AddDays(1),
            CalendarViewType.Week => DisplayDate.AddDays(7),
            CalendarViewType.Month => DisplayDate.AddMonths(1),
            CalendarViewType.Year => DisplayDate.AddYears(1),
            CalendarViewType.Agenda => DisplayDate.AddDays(30),
            _ => DisplayDate.AddMonths(1)
        };
        await LoadViewDataAsync();
    }

    private async Task NavigateToTodayAsync()
    {
        DisplayDate = DateTime.Today;
        SelectedDate = DateTime.Today;
        await LoadViewDataAsync();
    }

    private async Task SelectDateAsync(CalendarDayViewModel? day)
    {
        if (day == null) return;
        SelectedDate = day.CalendarDate.GregorianEquivalent;
    }

    private void ChangeViewFromString(string? viewTypeStr)
    {
        if (viewTypeStr != null && Enum.TryParse<CalendarViewType>(viewTypeStr, out var viewType))
        {
            CurrentViewType = viewType;
        }
    }

    private async Task ChangeCalendarModeFromStringAsync(string? modeStr)
    {
        if (modeStr != null && Enum.TryParse<CalendarMode>(modeStr, out var mode))
        {
            CurrentCalendarMode = mode;
        }
    }

    // ========== EVENT EDITOR NAVIGATION ==========

    private async Task NewEventAsync()
    {
        await _navigator.NavigateViewModelAsync<EventEditorViewModel>(this, qualifier: Qualifiers.None);
    }

    private async Task EditEventAsync(CalendarEventViewModel? eventVm)
    {
        if (eventVm == null) return;
        await _navigator.NavigateViewModelAsync<EventEditorViewModel>(this, qualifier: Qualifiers.None,
            data: eventVm.Id);
    }

    private async Task OpenSettingsAsync()
    {
        await _navigator.NavigateViewModelAsync<SettingsViewModel>(this, qualifier: Qualifiers.None);
        // Refresh is triggered by SettingsClosedMessage when the user navigates back.
    }

    /// <summary>
    /// Called when the user closes the Settings page.
    /// Re-reads the persisted calendar mode and location, refreshes the disabled-calendar
    /// filter, and triggers a sync if the user toggled any Google calendar checkboxes.
    /// </summary>
    private async Task RefreshAfterSettingsClosedAsync(bool hasGoogleChanges = false)
    {
        try
        {
            await RestoreDefaultCalendarModeAsync();
            await _biblicalService.InitializeLocationAsync();
            await LoadDisabledCalendarIdsAsync();
            await LoadCalendarColorsAsync();

            if (hasGoogleChanges && _authService.IsSignedIn)
            {
                // Sync first so newly-enabled calendars are pulled; SyncAsync calls LoadViewDataAsync.
                await SyncAsync();
            }
            else
            {
                await LoadViewDataAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error refreshing after settings closed: {ex}");
        }
    }

    // ========== BIBLICAL HOLIDAY HELPER ==========

    /// <summary>
    /// Returns a Gregorian-date → holiday-name dictionary for the given range.
    /// In Biblical mode, returns the raw holy-day dates.
    /// On midnight-based calendars (Gregorian/Julian) with ShowBiblicalHolidays enabled,
    /// also adds an "Eve of X" entry for the day preceding each holy day, unless that
    /// preceding day already carries a holy day of its own.
    /// </summary>
    private async Task<Dictionary<DateTime, string>> GetBiblicalHolidaysForRangeAsync(DateTime start, DateTime end)
    {
        if (CurrentCalendarMode != CalendarMode.Biblical && !ShowBiblicalHolidays)
            return new Dictionary<DateTime, string>();

        try
        {
            var holidays = await _holidayService.GetHolidayDisplaysForRangeAsync(start, end);

            // On midnight-based calendars, prepend an "Eve of X" day before each holy day.
            if (CurrentCalendarMode != CalendarMode.Biblical)
            {
                var eveEntries = new Dictionary<DateTime, string>();
                foreach (var kvp in holidays)
                {
                    var eve = kvp.Key.AddDays(-1);
                    // Only add eve if there's no existing holiday on the eve day
                    if (!holidays.ContainsKey(eve))
                    {
                        var eveDisplay = $"Eve of {kvp.Value}";
                        if (eveEntries.ContainsKey(eve))
                            eveEntries[eve] += "\n" + eveDisplay;  // Multiple eves on same day → concatenate
                        else
                            eveEntries[eve] = eveDisplay;
                    }
                }
                foreach (var kvp in eveEntries)
                {
                    if (holidays.ContainsKey(kvp.Key))
                        holidays[kvp.Key] += "\n" + kvp.Value;  // Concatenate with existing holiday
                    else
                        holidays[kvp.Key] = kvp.Value;
                }
            }

            return holidays;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error computing Biblical holidays: {ex.Message}");
            return new Dictionary<DateTime, string>();
        }
    }

    // ========== CROSS-CALENDAR VISIBILITY TOGGLES ==========

    /// <summary>
    /// Returns the additional CalendarMode values whose scoped events should be shown
    /// alongside the current mode's own events and EventScope.All events.
    /// </summary>
    private IEnumerable<CalendarMode> GetAdditionalVisibleModes()
    {
        var modes = new List<CalendarMode>();
        if (ShowGregorianEvents && CurrentCalendarMode != CalendarMode.Gregorian)
            modes.Add(CalendarMode.Gregorian);
        if (ShowJulianEvents && CurrentCalendarMode != CalendarMode.Julian)
            modes.Add(CalendarMode.Julian);
        if (ShowBiblicalEvents && CurrentCalendarMode != CalendarMode.Biblical)
            modes.Add(CalendarMode.Biblical);
        return modes;
    }

    private async Task ToggleGregorianEventsAsync()
    {
        ShowGregorianEvents = !ShowGregorianEvents;
        await LoadViewDataAsync();
    }

    private async Task ToggleJulianEventsAsync()
    {
        ShowJulianEvents = !ShowJulianEvents;
        await LoadViewDataAsync();
    }

    private async Task ToggleBiblicalEventsAsync()
    {
        ShowBiblicalEvents = !ShowBiblicalEvents;
        await LoadViewDataAsync();
    }

    private async Task ToggleBiblicalHolidaysAsync()
    {
        ShowBiblicalHolidays = !ShowBiblicalHolidays;
        await SaveSettingAsync("ShowBiblicalHolidays", ShowBiblicalHolidays ? "true" : "false");
        await LoadViewDataAsync();
    }

    // ========== ASTRONOMICAL HELPERS ==========

    private string? GetLunarConjunctionDisplay(DateTime gregorianDate)
    {
        try
        {
            // Always use the user's selected location timezone (from the Biblical service singleton)
            // so that the new moon date is consistent across all calendar modes.
            TimeZoneInfo tz = TimeZoneInfo.Local;
            try { tz = _biblicalService.GetCurrentLocation().TimeZone; }
            catch { }

            if (CurrentCalendarMode == CalendarMode.Biblical)
            {
                // In Biblical mode the day shown on gregorianDate's grid cell runs from
                // sunset(gregorianDate-1) to sunset(gregorianDate). A conjunction that
                // falls after sunset belongs to the NEXT Biblical day (gregorianDate+1 cell),
                // so we must test against sunset boundaries rather than midnight boundaries.
                var location = _biblicalService.GetCurrentLocation();
                var openingSunsetLocal = _astronomicalService.CalculateSunset(gregorianDate.AddDays(-1), location);
                var closingSunsetLocal = _astronomicalService.CalculateSunset(gregorianDate, location);
                var openingSunsetUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(openingSunsetLocal, DateTimeKind.Unspecified), tz);
                var closingSunsetUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(closingSunsetLocal, DateTimeKind.Unspecified), tz);

                var nextNewMoonUtc = _astronomicalService.CalculateNextNewMoon(openingSunsetUtc);
                if (nextNewMoonUtc < closingSunsetUtc)
                {
                    var newMoonLocal = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(nextNewMoonUtc, DateTimeKind.Utc), tz);
                    var tzAbbrev = GetTimeZoneAbbreviation(tz, newMoonLocal);
                    return $"New Moon: {newMoonLocal:h:mm tt} {tzAbbrev}";
                }
                return null;
            }

            // Gregorian / Julian: day boundary is midnight.
            var startOfLocalDayUtc = TimeZoneInfo.ConvertTimeToUtc(gregorianDate.Date, tz);
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(startOfLocalDayUtc);
            var newMoon = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(nextNewMoon, DateTimeKind.Utc), tz);

            if (newMoon.Date == gregorianDate.Date)
            {
                var tzAbbrev = GetTimeZoneAbbreviation(tz, newMoon);
                return $"New Moon: {newMoon:h:mm tt} {tzAbbrev}";
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// For the Biblical calendar: returns the lunar crescent illumination percentage at
    /// sunset on this Gregorian day, but only for the first four crescent evenings of a
    /// new month (the sunset immediately following the conjunction plus the next three).
    /// Returns null on all other days or when not in Biblical mode.
    /// </summary>
    private string? GetCrescentIlluminationDisplay(DateTime gregorianDate)
    {
        if (CurrentCalendarMode != CalendarMode.Biblical) return null;

        try
        {
            if (_calendarService is not BiblicalCalendarService biblical) return null;
            var location = biblical.GetCurrentLocation();
            var tz = location.TimeZone;

            // Opening sunset = sunset on the PREVIOUS Gregorian day.
            // This is the sunset that BEGINS the Biblical day labeled as gregorianDate.
            var openingSunsetLocal = _astronomicalService.CalculateSunset(gregorianDate.Date.AddDays(-1), location);
            var openingSunsetUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(openingSunsetLocal, DateTimeKind.Unspecified), tz);

            // Most recent new moon before this opening sunset
            var newMoonUtc = _astronomicalService.CalculatePreviousNewMoon(openingSunsetUtc);
            var newMoonLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(newMoonUtc, DateTimeKind.Utc), tz);

            // First crescent evening = first sunset on or after the conjunction
            var sunsetOnConjDay = _astronomicalService.CalculateSunset(newMoonLocal.Date, location);
            var firstCrescentDate = newMoonLocal < sunsetOnConjDay
                ? newMoonLocal.Date           // conjunction before sunset → first crescent tonight
                : newMoonLocal.Date.AddDays(1); // conjunction after sunset → first crescent tomorrow

            // eveningIndex: how many days after firstCrescentDate the opening sunset falls.
            // The opening sunset is on gregorianDate-1, so index = (gregorianDate-1 - firstCrescentDate).
            int eveningIndex = (gregorianDate.Date.AddDays(-1) - firstCrescentDate).Days;
            if (eveningIndex < 0 || eveningIndex > 3) return null;

            // Illumination at the opening sunset (UTC, for accuracy)
            var illumination = _astronomicalService.GetLunarIllumination(openingSunsetUtc);

            return $"\U0001f319 {illumination:F3}% (eve. {eveningIndex + 1})";
        }
        catch { }
        return null;
    }

    /// <param name="compact">
    /// When true returns a short form (e.g. "↓5:45 PM CDT") suitable for narrow month/week cells.
    /// When false returns the full form (e.g. "Day began: 5:45 PM CDT") for the Day-view header.
    /// </param>
    private string? GetBiblicalDayStartDisplay(DateTime gregorianDate, bool compact = false)
    {
        try
        {
            // GetDayStart → CalculateSunset → already returns local time at the observer's location
            var dayStartLocal = _calendarService.GetDayStart(gregorianDate);

            TimeZoneInfo tz = TimeZoneInfo.Local;
            if (_calendarService is BiblicalCalendarService biblical)
            {
                try { tz = biblical.GetCurrentLocation().TimeZone; }
                catch { /* keep Local as fallback */ }
            }

            var tzAbbrev = GetTimeZoneAbbreviation(tz, dayStartLocal);

            return compact
                ? $"{dayStartLocal:h:mm tt} {tzAbbrev}"
                : $"Day began: {dayStartLocal:h:mm tt} {tzAbbrev}";
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Returns a display string if the given Gregorian date is an equinox or solstice,
    /// e.g. "Spring Equinox: 5:24 PM CDT". Returns null on all other days.
    /// Checks adjacent calendar years to handle timezone date-boundary shifts.
    /// </summary>
    private string? GetAstronomicalEventDisplay(DateTime gregorianDate)
    {
        try
        {
            TimeZoneInfo tz = TimeZoneInfo.Local;
            try { tz = _biblicalService.GetCurrentLocation().TimeZone; }
            catch { }

            for (int y = gregorianDate.Year - 1; y <= gregorianDate.Year + 1; y++)
            {
                var candidates = new (string Name, DateTime UtcTime)[]
                {
                    ("Spring Equinox",  _astronomicalService.CalculateVernalEquinox(y)),
                    ("Summer Solstice", _astronomicalService.CalculateSummerSolstice(y)),
                    ("Fall Equinox",    _astronomicalService.CalculateAutumnalEquinox(y)),
                    ("Winter Solstice", _astronomicalService.CalculateWinterSolstice(y)),
                };

                foreach (var (name, utcTime) in candidates)
                {
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), tz);
                    if (localTime.Date == gregorianDate.Date)
                    {
                        var tzAbbrev = GetTimeZoneAbbreviation(tz, localTime);
                        return $"{name}: {localTime:h:mm tt} {tzAbbrev}";
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string GetTimeZoneAbbreviation(TimeZoneInfo tz, DateTime forTime)
    {
        // UTC special cases
        if (tz.BaseUtcOffset == TimeSpan.Zero &&
            (tz.StandardName.Contains("Universal") || tz.StandardName.Contains("UTC") || tz.Id == "UTC"))
            return "UTC";

        var isDst = tz.IsDaylightSavingTime(forTime);
        var name = isDst ? tz.DaylightName : tz.StandardName;

        // Extract first letter of each word: "Israel Standard Time" → "IST"
        var initials = string.Concat(
            name.Split(' ')
                .Where(w => w.Length > 0 && char.IsLetter(w[0]))
                .Select(w => w[0]));

        if (initials.Length >= 2 && initials.Length <= 5)
            return initials;

        // Fallback: UTC offset format
        var offset = tz.GetUtcOffset(forTime);
        if (offset == TimeSpan.Zero) return "UTC";
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var abs = offset.Duration();
        return abs.Minutes == 0
            ? $"UTC{sign}{(int)abs.TotalHours}"
            : $"UTC{sign}{(int)abs.TotalHours}:{abs.Minutes:D2}";
    }

    // ========== YEAR JUMP ==========

    private async Task JumpToYearAsync()
    {
        if (int.TryParse(YearInputText?.Trim(), out int year) && year >= 1 && year <= 9999)
        {
            try
            {
                DisplayDate = new DateTime(year, 1, 1);
                YearInputText = string.Empty;
                await LoadViewDataAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarApp] JumpToYear error: {ex}");
            }
        }
    }

    // ========== SETTINGS HELPER ==========


    private async Task SaveSettingAsync(string key, string value)
    {
        try
        {
            await _dbContext.InitializeAsync();
            var existing = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == key);

            if (existing != null)
            {
                existing.Value = value;
                existing.ValueType = "string";
                await _dbContext.Connection.UpdateAsync(existing);
            }
            else
            {
                await _dbContext.Connection.InsertAsync(new SettingsEntity
                {
                    Key = key,
                    Value = value,
                    ValueType = "string"
                });
            }
            Console.WriteLine($"[CalendarApp] MainVM saved setting '{key}' = '{value}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] MainVM error saving setting '{key}': {ex}");
        }
    }

    // ========== GOOGLE SYNC ==========

    private async Task SignInAsync()
    {
        IsBusy = true;
        SyncStatusText = "Signing in...";
        try
        {
            var success = await _authService.SignInAsync();
            IsSignedIn = success;
            GoogleAccountEmail = _authService.UserEmail ?? string.Empty;
            SyncStatusText = success ? "Signed in" : "Sign in failed";

            if (success)
            {
                // Auto-sync after sign-in, then keep syncing periodically.
                StartSyncTimer();
                await SyncAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        IsBusy = true;
        SyncStatusText = "Signing out...";
        try
        {
            StopSyncTimer();
            await _authService.SignOutAsync();
            IsSignedIn = false;
            GoogleAccountEmail = string.Empty;
            SyncStatusText = "Signed out";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartSyncTimer()
    {
        StopSyncTimer();
        var interval = GetSyncInterval();
        SyncDiagnosticLog.Write($"StartSyncTimer: interval={interval.TotalSeconds:0}s (metered={IsMeteredConnection()})");

        // SyncAsync is I/O-bound and runs correctly from thread-pool threads in Uno Platform
        // (the startup sync follows this same code path successfully).  No dispatcher dispatch
        // is needed, so System.Threading.Timer works without requiring _uiDispatcher.
        _syncTimer = new System.Threading.Timer(async _ =>
        {
            SyncDiagnosticLog.Write($"Timer tick — signedIn={_authService.IsSignedIn}, syncService.IsSyncing={_syncService.IsSyncing}");
            try { await SyncAsync(); }
            catch (Exception ex) { SyncDiagnosticLog.Write($"Timer tick error: {ex.Message}"); }
        }, null, interval, interval);

        SyncDiagnosticLog.Write("StartSyncTimer: timer started");
    }

    private void StopSyncTimer()
    {
        if (_syncTimer != null)
        {
            _syncTimer.Dispose();
            _syncTimer = null;
            SyncDiagnosticLog.Write("StopSyncTimer: timer stopped");
        }
    }

    /// <summary>
    /// Returns the sync interval: 30 seconds on an unrestricted connection,
    /// 15 minutes on a metered (variable-cost) or roaming connection.
    /// </summary>
    private static TimeSpan GetSyncInterval() =>
        IsMeteredConnection()
            ? TimeSpan.FromMinutes(AutoSyncIntervalMinutesMetered)
            : TimeSpan.FromSeconds(AutoSyncIntervalSecondsNonMetered);

    /// <summary>
    /// Returns true when the active internet connection is metered (variable-cost or roaming).
    /// Falls back to false (non-metered) when the platform doesn't support the query.
    /// </summary>
    private static bool IsMeteredConnection()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation
                              .GetInternetConnectionProfile();
            if (profile == null) return false;

            var cost = profile.GetConnectionCost();
            return cost.NetworkCostType
                       != Windows.Networking.Connectivity.NetworkCostType.Unrestricted
                   || cost.Roaming;
        }
        catch { return false; }
    }

    /// <summary>
    /// Called by the OS when the network status changes (e.g. Wi-Fi → mobile data).
    /// Restarts the sync timer so the interval immediately reflects the new connection type.
    /// </summary>
    private void OnNetworkStatusChanged(object sender)
    {
        if (!IsSignedIn) return;
        // StartSyncTimer only creates a System.Threading.Timer now, so it's safe to call
        // from the OS network-status callback thread without any dispatcher dispatch.
        StartSyncTimer();
    }

    private async Task SyncAsync()
    {
        if (!_authService.IsSignedIn)
        {
            SyncDiagnosticLog.Write("SyncAsync: skipping — not signed in");
            return;
        }
        if (_syncService.IsSyncing)
        {
            SyncDiagnosticLog.Write("SyncAsync: skipping — sync already in progress");
            return;
        }

        IsSyncing = true;
        SyncStatusText = "Syncing...";
        SyncDiagnosticLog.Write("SyncAsync: starting...");
        try
        {
            var result = await _syncService.SyncAsync();
            SyncDiagnosticLog.Write($"SyncAsync: done — {result.Summary}");
            if (result.Errors.Count > 0)
                SyncDiagnosticLog.Write($"SyncAsync: errors — {string.Join("; ", result.Errors)}");

            SyncStatusText = result.Summary;

            // Reload current view to show synced data
            await LoadViewDataAsync();
        }
        catch (Exception ex)
        {
            SyncDiagnosticLog.Write($"SyncAsync: exception — {ex}");
            throw;
        }
        finally
        {
            IsSyncing = false;
        }
    }
}

// ========== ENUMS ==========

public enum CalendarViewType
{
    Day,
    Week,
    Month,
    Year,
    Agenda
}

// ========== VIEW MODELS ==========

/// <summary>
/// View model for a day cell in the month calendar grid.
/// </summary>
public partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDate CalendarDate { get; set; } = default!;
    public int Day { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public string? CrossReference { get; set; }
    public bool HasCrossReference => !string.IsNullOrEmpty(CrossReference);
    public string? LunarConjunctionDisplay { get; set; }
    public bool HasLunarConjunctionDisplay => !string.IsNullOrEmpty(LunarConjunctionDisplay);
    public string? CrescentIlluminationDisplay { get; set; }
    public bool HasCrescentIlluminationDisplay => !string.IsNullOrEmpty(CrescentIlluminationDisplay);
    public string? DayStartDisplay { get; set; }
    public bool HasDayStartDisplay => !string.IsNullOrEmpty(DayStartDisplay);
    public string? AstronomicalEventDisplay { get; set; }
    public bool HasAstronomicalEventDisplay => !string.IsNullOrEmpty(AstronomicalEventDisplay);
    public string? HolidayDisplay { get; set; }
    public bool HasHolidayDisplay => !string.IsNullOrEmpty(HolidayDisplay);
    public ObservableCollection<CalendarEventViewModel> Events { get; set; } = new();
    public bool HasEvents => Events.Count > 0;
}

/// <summary>
/// View model for a column in the week view (one day).
/// </summary>
public class WeekDayColumnViewModel
{
    public CalendarDate CalendarDate { get; set; } = default!;
    public string DayOfWeek { get; set; } = string.Empty;
    public int DayNumber { get; set; }
    public bool IsToday { get; set; }
    public bool IsSelected { get; set; }
    public string? CrossReference { get; set; }
    public bool HasCrossReference => !string.IsNullOrEmpty(CrossReference);
    public string? LunarConjunctionDisplay { get; set; }
    public bool HasLunarConjunctionDisplay => !string.IsNullOrEmpty(LunarConjunctionDisplay);
    public string? CrescentIlluminationDisplay { get; set; }
    public bool HasCrescentIlluminationDisplay => !string.IsNullOrEmpty(CrescentIlluminationDisplay);
    public string? DayStartDisplay { get; set; }
    public bool HasDayStartDisplay => !string.IsNullOrEmpty(DayStartDisplay);
    public string? AstronomicalEventDisplay { get; set; }
    public bool HasAstronomicalEventDisplay => !string.IsNullOrEmpty(AstronomicalEventDisplay);
    public string? HolidayDisplay { get; set; }
    public bool HasHolidayDisplay => !string.IsNullOrEmpty(HolidayDisplay);
    public ObservableCollection<CalendarEventViewModel> AllDayEvents { get; set; } = new();
    public ObservableCollection<HourSlotViewModel> HourSlots { get; set; } = new();
}

/// <summary>
/// View model for a single hour slot (used in day and week views).
/// </summary>
public class HourSlotViewModel
{
    public int Hour { get; set; }
    public string HourDisplay { get; set; } = string.Empty;
    public ObservableCollection<CalendarEventViewModel> Events { get; set; } = new();
    public bool HasEvents { get; set; }
}

/// <summary>
/// View model for a mini month in the year view.
/// </summary>
public class YearMonthViewModel
{
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public ObservableCollection<MiniDayViewModel> Days { get; set; } = new();
}

/// <summary>
/// View model for a mini day in the year view.
/// </summary>
public class MiniDayViewModel
{
    public int Day { get; set; }
    public bool IsToday { get; set; }
    public bool HasEvents { get; set; }
    public DateTime GregorianDate { get; set; }
    public bool IsBlank { get; set; }
    public bool IsAstronomicalEvent { get; set; }
    public string? AstronomicalEventName { get; set; }
    public bool IsHoliday { get; set; }
    public string? HolidayName { get; set; }
    public string DayDisplay => IsBlank ? "" : Day.ToString();
}

/// <summary>
/// View model for a date group in the agenda view.
/// </summary>
public class AgendaGroupViewModel
{
    public DateTime Date { get; set; }
    public string DateDisplay { get; set; } = string.Empty;
    public bool IsToday { get; set; }
    public string? CrossReference { get; set; }
    public bool HasCrossReference => !string.IsNullOrEmpty(CrossReference);
    public string? LunarConjunctionDisplay { get; set; }
    public bool HasLunarConjunctionDisplay => !string.IsNullOrEmpty(LunarConjunctionDisplay);
    public string? CrescentIlluminationDisplay { get; set; }
    public bool HasCrescentIlluminationDisplay => !string.IsNullOrEmpty(CrescentIlluminationDisplay);
    public string? DayStartDisplay { get; set; }
    public bool HasDayStartDisplay => !string.IsNullOrEmpty(DayStartDisplay);
    public string? AstronomicalEventDisplay { get; set; }
    public bool HasAstronomicalEventDisplay => !string.IsNullOrEmpty(AstronomicalEventDisplay);
    public string? HolidayDisplay { get; set; }
    public bool HasHolidayDisplay => !string.IsNullOrEmpty(HolidayDisplay);
    public ObservableCollection<CalendarEventViewModel> Events { get; set; } = new();
    public bool HasEvents => Events.Count > 0;
    public string NoEventsText => "No events";
}

/// <summary>
/// View model for a calendar event.
/// </summary>
public class CalendarEventViewModel
{
    /// <param name="calendarEvent">The domain event.</param>
    /// <param name="calendarColorHex">
    /// User-chosen (or Google-assigned) hex color for the owning calendar, e.g. "#0B8043".
    /// When non-empty this overrides the event's own ColorHex.
    /// </param>
    /// <param name="displayTz">
    /// Timezone to use when displaying event times. Events sourced from Google Calendar
    /// are stored as UTC; this timezone is used to convert them to the user's local time.
    /// Defaults to the system local timezone when null.
    /// </param>
    public CalendarEventViewModel(CalendarEvent calendarEvent, string calendarColorHex = "",
        TimeZoneInfo? displayTz = null)
    {
        Id = calendarEvent.Id;
        Title = calendarEvent.Title;
        Description = calendarEvent.Description;
        IsAllDay = calendarEvent.IsAllDay;
        ColorHex = calendarEvent.ColorHex;
        Location = calendarEvent.Location;

        // Events from Google are stored as UTC (GoogleCalendarService.ConvertToLocal uses
        // DateTimeDateTimeOffset.UtcDateTime). Convert to the configured display timezone so
        // the user sees their local time rather than UTC.
        if (!calendarEvent.IsAllDay && !string.IsNullOrEmpty(calendarEvent.GoogleEventId))
        {
            var tz = displayTz ?? TimeZoneInfo.Local;
            StartTime = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(calendarEvent.StartDateTime, DateTimeKind.Utc), tz);
            EndTime = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(calendarEvent.EndDateTime, DateTimeKind.Utc), tz);
        }
        else
        {
            StartTime = calendarEvent.StartDateTime;
            EndTime   = calendarEvent.EndDateTime;
        }

        // Resolve display color: calendar override → event's own color → default blue
        var effectiveHex = !string.IsNullOrEmpty(calendarColorHex) ? calendarColorHex
                         : !string.IsNullOrEmpty(calendarEvent.ColorHex) ? calendarEvent.ColorHex
                         : "#1a73e8";
        DisplayColorHex = effectiveHex;
        TextColorHex = GetContrastingHex(effectiveHex);
    }

    public int Id { get; }
    public string Title { get; }
    public string Description { get; }
    public DateTime StartTime { get; }
    public DateTime EndTime { get; }
    public bool IsAllDay { get; }
    public string ColorHex { get; }
    public string Location { get; }

    /// <summary>Background hex color for rendering this event chip.</summary>
    public string DisplayColorHex { get; }

    /// <summary>Foreground hex color (#000000 or #FFFFFF) that contrasts with DisplayColorHex.</summary>
    public string TextColorHex { get; }

    public string StartTimeDisplay => IsAllDay ? "All day" : StartTime.ToString("h:mm tt");
    public string TimeRangeDisplay => IsAllDay ? "All day" : $"{StartTime:h:mm tt} - {EndTime:h:mm tt}";
    public string DurationDisplay
    {
        get
        {
            var duration = EndTime - StartTime;
            if (IsAllDay) return "All day";
            if (duration.TotalHours >= 1) return $"{duration.TotalHours:0.#}h";
            return $"{duration.TotalMinutes:0}m";
        }
    }

    public double TopOffset => IsAllDay ? 0 : StartTime.Hour * 60 + StartTime.Minute;
    public double HeightMinutes => IsAllDay ? 60 : Math.Max((EndTime - StartTime).TotalMinutes, 15);

    private static string GetContrastingHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return "#FFFFFF";
        try
        {
            double r = Convert.ToByte(hex[0..2], 16) / 255.0;
            double g = Convert.ToByte(hex[2..4], 16) / 255.0;
            double b = Convert.ToByte(hex[4..6], 16) / 255.0;
            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            return luminance > 0.35 ? "#000000" : "#FFFFFF";
        }
        catch { return "#FFFFFF"; }
    }
}
