namespace CalendarApp.Presentation;

public sealed partial class EventEditorPage : Page
{
    // Stores the nav parameter until the ViewModel is ready.
    private object? _navParameter;
    private bool _initialized;

    public EventEditorPage()
    {
        this.InitializeComponent();
        // DataContext may be set after OnNavigatedTo via DI — subscribe as a fallback.
        this.DataContextChanged += OnDataContextChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navParameter = e.Parameter;
        _initialized = false; // reset so each navigation initializes fresh
        TryInitialize();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        TryInitialize();
    }

    private void TryInitialize()
    {
        if (_initialized) return;
        if (DataContext is not EventEditorViewModel vm) return;

        _initialized = true;

        // _navParameter is an int event ID when editing, null/missing for new events.
        if (_navParameter is int eventId && eventId > 0)
            _ = vm.InitializeEditEventAsync(eventId);
        else
            vm.InitializeNewEvent(DateTime.Today);
    }
}
