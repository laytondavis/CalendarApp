using CalendarApp.Services.Interfaces;

namespace CalendarApp.Presentation;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Fires every time Settings becomes the active page.
    /// Reloads all settings from the database so the displayed values are always
    /// current, and resets the closed-message flag so MainViewModel is notified
    /// when the user leaves this time.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is SettingsViewModel vm)
            vm.OnNavigatedToSettings();
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
