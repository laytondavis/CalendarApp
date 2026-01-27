using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
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
    private ICalendarCalculationService _calendarService;

    [ObservableProperty]
    private CalendarMode _currentCalendarMode = CalendarMode.Gregorian;

    [ObservableProperty]
    private CalendarViewType _currentViewType = CalendarViewType.Month;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _displayDate = DateTime.Today;

    [ObservableProperty]
    private string _displayMonthYear = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CalendarDayViewModel> _monthDays = new();

    [ObservableProperty]
    private ObservableCollection<CalendarEventViewModel> _selectedDateEvents = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _crossReferenceText;

    public MainViewModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        INavigator navigator,
        Func<CalendarMode, ICalendarCalculationService> calendarServiceFactory,
        IEventRepository eventRepository,
        ILocationService locationService)
    {
        _navigator = navigator;
        _calendarServiceFactory = calendarServiceFactory;
        _eventRepository = eventRepository;
        _locationService = locationService;
        _calendarService = calendarServiceFactory(CalendarMode.Gregorian);

        Title = localizer["ApplicationName"];

        // Initialize commands
        PreviousCommand = new AsyncRelayCommand(NavigatePreviousAsync);
        NextCommand = new AsyncRelayCommand(NavigateNextAsync);
        TodayCommand = new AsyncRelayCommand(NavigateToTodayAsync);
        SelectDateCommand = new AsyncRelayCommand<CalendarDayViewModel>(SelectDateAsync);
        ChangeViewCommand = new RelayCommand<CalendarViewType>(ChangeView);
        ChangeCalendarModeCommand = new AsyncRelayCommand<CalendarMode>(ChangeCalendarModeAsync);

        // Load initial data
        _ = LoadMonthDataAsync();
    }

    public string? Title { get; }

    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand SelectDateCommand { get; }
    public ICommand ChangeViewCommand { get; }
    public ICommand ChangeCalendarModeCommand { get; }

    public string[] CalendarModeNames => new[] { "Gregorian", "Julian", "Biblical" };
    public string[] ViewTypeNames => new[] { "Day", "Week", "Month", "Year", "Agenda" };

    partial void OnCurrentCalendarModeChanged(CalendarMode value)
    {
        _calendarService = _calendarServiceFactory(value);
        _ = LoadMonthDataAsync();
    }

    partial void OnDisplayDateChanged(DateTime value)
    {
        UpdateDisplayMonthYear();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadEventsForSelectedDateAsync();
        UpdateCrossReference();
    }

    private async Task LoadMonthDataAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
            var monthGrid = _calendarService.GetMonthGrid(calendarDate.Year, calendarDate.Month).ToList();

            // Get events for the visible date range
            var firstDate = monthGrid.First().GregorianEquivalent;
            var lastDate = monthGrid.Last().GregorianEquivalent.AddDays(1);
            var events = await _eventRepository.GetEventsForDateRangeAsync(firstDate, lastDate, CurrentCalendarMode);
            var eventsByDate = events.GroupBy(e => e.StartDateTime.Date).ToDictionary(g => g.Key, g => g.ToList());

            var dayViewModels = new ObservableCollection<CalendarDayViewModel>();
            var today = DateTime.Today;
            var selectedDateOnly = SelectedDate.Date;

            foreach (var date in monthGrid)
            {
                var isCurrentMonth = date.Month == calendarDate.Month;
                var isToday = date.GregorianEquivalent.Date == today;
                var isSelected = date.GregorianEquivalent.Date == selectedDateOnly;

                var dayEvents = eventsByDate.TryGetValue(date.GregorianEquivalent.Date, out var evts)
                    ? evts.Select(e => new CalendarEventViewModel(e)).ToList()
                    : new List<CalendarEventViewModel>();

                dayViewModels.Add(new CalendarDayViewModel
                {
                    CalendarDate = date,
                    Day = date.Day,
                    IsCurrentMonth = isCurrentMonth,
                    IsToday = isToday,
                    IsSelected = isSelected,
                    CrossReference = date.CrossReference,
                    Events = new ObservableCollection<CalendarEventViewModel>(dayEvents)
                });
            }

            MonthDays = dayViewModels;
            UpdateDisplayMonthYear();
            UpdateCrossReference();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadEventsForSelectedDateAsync()
    {
        var startOfDay = SelectedDate.Date;
        var endOfDay = startOfDay.AddDays(1);

        var events = await _eventRepository.GetEventsForDateRangeAsync(startOfDay, endOfDay, CurrentCalendarMode);
        SelectedDateEvents = new ObservableCollection<CalendarEventViewModel>(
            events.Select(e => new CalendarEventViewModel(e)));

        // Update the selected state in month grid
        foreach (var day in MonthDays)
        {
            day.IsSelected = day.CalendarDate.GregorianEquivalent.Date == SelectedDate.Date;
        }
    }

    private void UpdateDisplayMonthYear()
    {
        var calendarDate = _calendarService.GetDateForDateTime(DisplayDate);
        var monthName = _calendarService.GetMonthName(calendarDate.Month);
        DisplayMonthYear = $"{monthName} {calendarDate.Year}";
    }

    private void UpdateCrossReference()
    {
        var calendarDate = _calendarService.GetDateForDateTime(SelectedDate);
        CrossReferenceText = CurrentCalendarMode != CalendarMode.Gregorian
            ? _calendarService.GetCrossReferenceDisplay(calendarDate)
            : null;
    }

    private async Task NavigatePreviousAsync()
    {
        DisplayDate = CurrentViewType switch
        {
            CalendarViewType.Day => DisplayDate.AddDays(-1),
            CalendarViewType.Week => DisplayDate.AddDays(-7),
            CalendarViewType.Month => DisplayDate.AddMonths(-1),
            CalendarViewType.Year => DisplayDate.AddYears(-1),
            _ => DisplayDate.AddMonths(-1)
        };
        await LoadMonthDataAsync();
    }

    private async Task NavigateNextAsync()
    {
        DisplayDate = CurrentViewType switch
        {
            CalendarViewType.Day => DisplayDate.AddDays(1),
            CalendarViewType.Week => DisplayDate.AddDays(7),
            CalendarViewType.Month => DisplayDate.AddMonths(1),
            CalendarViewType.Year => DisplayDate.AddYears(1),
            _ => DisplayDate.AddMonths(1)
        };
        await LoadMonthDataAsync();
    }

    private async Task NavigateToTodayAsync()
    {
        DisplayDate = DateTime.Today;
        SelectedDate = DateTime.Today;
        await LoadMonthDataAsync();
    }

    private async Task SelectDateAsync(CalendarDayViewModel? day)
    {
        if (day == null) return;
        SelectedDate = day.CalendarDate.GregorianEquivalent;
    }

    private void ChangeView(CalendarViewType viewType)
    {
        CurrentViewType = viewType;
    }

    private async Task ChangeCalendarModeAsync(CalendarMode mode)
    {
        CurrentCalendarMode = mode;
    }
}

/// <summary>
/// Calendar view types.
/// </summary>
public enum CalendarViewType
{
    Day,
    Week,
    Month,
    Year,
    Agenda
}

/// <summary>
/// View model for a day cell in the calendar grid.
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
    public ObservableCollection<CalendarEventViewModel> Events { get; set; } = new();
    public bool HasEvents => Events.Count > 0;
}

/// <summary>
/// View model for a calendar event.
/// </summary>
public class CalendarEventViewModel
{
    public CalendarEventViewModel(CalendarEvent calendarEvent)
    {
        Id = calendarEvent.Id;
        Title = calendarEvent.Title;
        StartTime = calendarEvent.StartDateTime;
        EndTime = calendarEvent.EndDateTime;
        IsAllDay = calendarEvent.IsAllDay;
        ColorHex = calendarEvent.ColorHex;
        Location = calendarEvent.Location;
    }

    public int Id { get; }
    public string Title { get; }
    public DateTime StartTime { get; }
    public DateTime EndTime { get; }
    public bool IsAllDay { get; }
    public string ColorHex { get; }
    public string Location { get; }

    public string StartTimeDisplay => IsAllDay ? "All day" : StartTime.ToString("h:mm tt");
    public string TimeRangeDisplay => IsAllDay ? "All day" : $"{StartTime:h:mm tt} - {EndTime:h:mm tt}";
}
