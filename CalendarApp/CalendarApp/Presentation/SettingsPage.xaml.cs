using CalendarApp.Services.Interfaces;

namespace CalendarApp.Presentation;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Forces the first tab's content into the visual tree by briefly switching
    /// away and back. But first, ensure settings are fully loaded from the database.
    /// Uno Platform's TabView is lazy: even though the first tab is selected by default,
    /// its content may not be fully realized until a SelectionChanged event occurs.
    /// </summary>
    private bool _initialLoadDone;

    private async void OnTabViewLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        try
        {
            // Load settings from database before any tab content is shown
            await vm.LoadSettingsAndWaitAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error in OnTabViewLoaded: {ex}");
        }

        // Force Uno to re-realize the first tab content now that data is loaded.
        // We swap to tab 1, yield a frame, then swap back.
        await ForceTabRefreshAsync();
        _initialLoadDone = true;
    }

    /// <summary>
    /// Called each time Settings is navigated to. On re-entry the TabView is
    /// already loaded so OnTabViewLoaded won't fire again — refresh here instead.
    /// </summary>
    internal async void OnSettingsNavigatedTo()
    {
        if (!_initialLoadDone) return; // first load is handled by OnTabViewLoaded

        if (DataContext is SettingsViewModel vm)
        {
            try
            {
                await vm.LoadSettingsAndWaitAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarApp] Error reloading settings: {ex}");
            }
        }
        await ForceTabRefreshAsync();
    }

    private async Task ForceTabRefreshAsync()
    {
        var current = SettingsTabView.SelectedIndex;
        SettingsTabView.SelectedIndex = current == 0 ? 1 : 0;
        // Yield a frame so Uno processes the SelectionChanged
        var tcs = new TaskCompletionSource();
        DispatcherQueue.TryEnqueue(() => tcs.SetResult());
        await tcs.Task;
        SettingsTabView.SelectedIndex = current;
    }

    /// <summary>
    /// Fires every time Settings becomes the active page.
    /// Calls OnNavigatedToSettings (which initializes location service, etc.)
    /// but actual settings DB loading is deferred to OnTabViewLoaded to avoid
    /// rendering the page before data is available.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is SettingsViewModel vm)
            vm.OnNavigatedToSettings();
        OnSettingsNavigatedTo();
    }

    /// <summary>
    /// Fires whenever the user leaves Settings — whether via the in-app back button,
    /// the Android native back button/gesture, or any other navigation.
    /// Ensures MainViewModel always refreshes after Settings is closed.
    /// </summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (DataContext is SettingsViewModel vm)
            vm.EnsureSettingsClosedMessageSent();
    }

    /// <summary>
    /// Fired when the user taps a row in the search-results list.
    /// Adds that location to the saved list and makes it active.
    /// </summary>
    private void OnLocationResultClicked(object sender, ItemClickEventArgs e)
    {
        Console.WriteLine($"[CalendarApp] LocationResultsList ItemClick: {e.ClickedItem?.GetType().Name ?? "null"}");
        if (e.ClickedItem is LocationSuggestion suggestion && DataContext is SettingsViewModel vm)
        {
            Console.WriteLine($"[CalendarApp] Adding suggestion: {suggestion.Name}, City={suggestion.City}");
            vm.SelectLocationCommand.Execute(suggestion);
        }
    }

    /// <summary>
    /// Fired when the user taps a row in the saved-locations list.
    /// Sets that location as the active location.
    /// </summary>
    private void OnSavedLocationClicked(object sender, ItemClickEventArgs e)
    {
        Console.WriteLine($"[CalendarApp] SavedLocationsList ItemClick: {e.ClickedItem?.GetType().Name ?? "null"}");
        if (e.ClickedItem is SavedLocationItem item && DataContext is SettingsViewModel vm)
        {
            Console.WriteLine($"[CalendarApp] Setting active: Id={item.Id} '{item.DisplayName}'");
            vm.SetActiveLocationCommand.Execute(item);
        }
    }

    /// <summary>
    /// Fired when the user clicks the ✕ remove button on a saved location row.
    /// The Tag property carries the SavedLocationItem for that row.
    /// </summary>
    private void OnRemoveLocationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SavedLocationItem item
            && DataContext is SettingsViewModel vm)
        {
            Console.WriteLine($"[CalendarApp] Removing location: Id={item.Id} '{item.DisplayName}'");
            vm.RemoveLocationCommand.Execute(item);
        }
    }
}
