using CalendarApp.Services.Interfaces;

namespace CalendarApp.Presentation;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[SettingsPage] Loaded fired. DataContext type: {DataContext?.GetType().Name ?? "null"}");

        // At Loaded time the visual tree is realized. If settings were already
        // loaded (via DataContextChanged or constructor), re-broadcast all
        // property values so that now-connected bindings pick them up.
        if (DataContext is SettingsViewModel vm)
        {
            Console.WriteLine($"[SettingsPage] Loaded — requesting re-broadcast. ThemeIndex={vm.SelectedThemeIndex}, CalMode={vm.SelectedCalendarModeIndex}");
            vm.RebroadcastAllProperties();
        }
    }

    private async void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is SettingsViewModel vm)
        {
            Console.WriteLine("[SettingsPage] DataContextChanged — loading settings from DB");
            await vm.LoadSettingsFromDbAsync();
            Console.WriteLine($"[SettingsPage] Settings loaded. ThemeIndex={vm.SelectedThemeIndex}, CalMode={vm.SelectedCalendarModeIndex}");

            // The TabView lazily realizes tab content, so {Binding} expressions
            // on ComboBoxes/CheckBoxes inside the first tab may never receive the
            // initial PropertyChanged. Bypass the binding system entirely and set
            // controls directly from code-behind once they exist.
            // Poll with short delays until FindName succeeds (up to 3 seconds).
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(100);
                if (FindName("ThemeComboBox") != null)
                {
                    Console.WriteLine($"[SettingsPage] Tab 1 controls found after {(attempt + 1) * 100}ms");
                    PopulateTab1Controls(vm);
                    return;
                }
            }
            Console.WriteLine("[SettingsPage] Tab 1 controls never appeared after 3s");
        }
    }

    /// <summary>
    /// Sets Tab 1 control values from the ViewModel AND wires up change handlers
    /// so user interactions are pushed back to the ViewModel (since {Binding} may
    /// not be connected due to TabView lazy content realization).
    /// </summary>
    private void PopulateTab1Controls(SettingsViewModel vm)
    {
        Console.WriteLine($"[SettingsPage] PopulateTab1Controls: Theme={vm.SelectedThemeIndex}, CalMode={vm.SelectedCalendarModeIndex}, " +
                          $"AstroMode={vm.SelectedAstronomyModeIndex}, UseLastCal={vm.UseLastSelectedCalendarType}, " +
                          $"ShowHolidays={vm.ShowBiblicalHolidays}");

        try
        {
            // Helper to wire ComboBox → ViewModel (TwoWay binding replacement)
            void WireComboBox(string name, int initialIndex, Action<int> onChanged)
            {
                if (FindName(name) is ComboBox cb)
                {
                    cb.SelectedIndex = initialIndex;
                    cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) onChanged(cb.SelectedIndex); };
                }
                else
                    Console.WriteLine($"[SettingsPage] {name} not found via FindName");
            }

            // Helper to wire CheckBox → ViewModel
            void WireCheckBox(string name, bool initialValue, Action<bool> onChanged)
            {
                if (FindName(name) is CheckBox chk)
                {
                    chk.IsChecked = initialValue;
                    chk.Checked += (_, _) => onChanged(true);
                    chk.Unchecked += (_, _) => onChanged(false);
                }
            }

            WireComboBox("ThemeComboBox", vm.SelectedThemeIndex, v => vm.SelectedThemeIndex = v);
            WireComboBox("CalendarModeComboBox", vm.SelectedCalendarModeIndex, v => vm.SelectedCalendarModeIndex = v);
            WireComboBox("AstronomyModeComboBox", vm.SelectedAstronomyModeIndex, v => vm.SelectedAstronomyModeIndex = v);

            if (FindName("CalendarModeComboBox") is ComboBox calMode)
                calMode.IsEnabled = vm.IsCalendarModeEditable;

            WireCheckBox("UseLastCalTypeCheckBox", vm.UseLastSelectedCalendarType, v => vm.UseLastSelectedCalendarType = v);
            WireCheckBox("ShowBiblicalHolidaysCheckBox", vm.ShowBiblicalHolidays, v => vm.ShowBiblicalHolidays = v);
            WireCheckBox("ShowGregorianEventsCheckBox", vm.ShowGregorianEvents, v => vm.ShowGregorianEvents = v);
            WireCheckBox("ShowJulianEventsCheckBox", vm.ShowJulianEvents, v => vm.ShowJulianEvents = v);
            WireCheckBox("ShowBiblicalEventsCheckBox", vm.ShowBiblicalEvents, v => vm.ShowBiblicalEvents = v);

            Console.WriteLine("[SettingsPage] PopulateTab1Controls — done");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsPage] PopulateTab1Controls error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fires every time Settings becomes the active page.
    /// Loads settings from DB so the UI is populated.
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
