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
    /// away and back. Uno Platform's TabView is lazy: even though the first tab
    /// is selected by default, its content may not be fully realized until a
    /// SelectionChanged event occurs. Setting SelectedIndex 0 → 1 → 0 triggers
    /// that event and ensures all bindings are connected before the user sees the tab.
    /// </summary>
    private void OnTabViewLoaded(object sender, RoutedEventArgs e)
    {
        SettingsTabView.SelectedIndex = 1;
        SettingsTabView.SelectedIndex = 0;
    }

    /// <summary>
    /// Fires every time Settings becomes the active page.
    /// Loads all settings from the database asynchronously, then resets the TabView's
    /// selection to force content realization with the newly loaded values. This ensures
    /// all ComboBox selections, TextBlock bindings, etc., are properly displayed.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is SettingsViewModel vm)
        {
            // Fire and forget the full load cycle, then refresh the tab view
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for settings to fully load (including DB cool-down)
                    await vm.LoadSettingsAndWaitAsync();

                    // Once loading is complete, toggle the tab view to force bindings to update
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        var idx = SettingsTabView.SelectedIndex;
                        SettingsTabView.SelectedIndex = idx == 0 ? 1 : 0;
                        SettingsTabView.SelectedIndex = idx;
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CalendarApp] Error in SettingsPage.OnNavigatedTo: {ex}");
                }
            });
        }
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
