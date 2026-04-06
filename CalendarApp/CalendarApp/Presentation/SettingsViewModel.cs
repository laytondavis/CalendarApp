using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services;
using CalendarApp.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;

namespace CalendarApp.Presentation;

public partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// Version string derived from the assembly version, which is stamped at build time
    /// by passing -p:Version=X.Y.Z to dotnet publish in the release workflow.
    /// Falls back to "1.0.0" for local dev builds where the version is not set.
    /// </summary>
    public string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
#if __ANDROID__
        try
        {
            var ctx = Android.App.Application.Context;
            var info = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0);
            return "v" + (info?.VersionName ?? "1.0.0");
        }
        catch { return "v1.0.0"; }
#else
        return "v" + (System.Reflection.Assembly.GetEntryAssembly()
            ?.GetName().Version?.ToString(3) ?? "1.0.0");
#endif
    }

    private readonly INavigator _navigator;
    private readonly ILocationService _locationService;
    private readonly IAstronomicalService _astronomicalService;
    private readonly CalendarDbContext _dbContext;
    private readonly IUpdateService _updateService;
    private bool _isLoading = true;
    private bool _googleCalendarSelectionChanged;

    // Theme
    // 0=System, 1=Dark, 2=Light, 3=Windows11, 4=WindowsXP, 5=MacOSModern, 6=MacOSClassic
    [ObservableProperty]
    private int _selectedThemeIndex;

    // Default Calendar Mode
    [ObservableProperty]
    private int _selectedCalendarModeIndex; // 0=Gregorian, 1=Julian, 2=Biblical

    // Use last selected calendar type
    [ObservableProperty]
    private bool _useLastSelectedCalendarType;

    // Whether the calendar mode combobox should be enabled
    [ObservableProperty]
    private bool _isCalendarModeEditable = true;

    // Location Mode
    [ObservableProperty]
    private int _selectedLocationModeIndex; // 0=GPS+Fallback, 1=Manual Only

    // Astronomy Calculation Mode
    [ObservableProperty]
    private int _selectedAstronomyModeIndex; // 0=Built-in, 1=Web API

    // Cross-calendar holiday display
    [ObservableProperty]
    private bool _showBiblicalHolidays;

    // Cross-calendar event overlays ("Also Show")
    [ObservableProperty]
    private bool _showGregorianEvents;

    [ObservableProperty]
    private bool _showJulianEvents;

    [ObservableProperty]
    private bool _showBiblicalEvents;

    // Saved locations list (Jerusalem always at index 0)
    [ObservableProperty]
    private ObservableCollection<SavedLocationItem> _savedLocations = new();

    // True when fewer than 8 user locations are saved
    [ObservableProperty]
    private bool _canAddLocation = true;

    // True when all 8 user location slots are used
    [ObservableProperty]
    private bool _isLocationListFull;

    // Location search
    [ObservableProperty]
    private string _locationSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchingLocations;

    [ObservableProperty]
    private bool _showLocationPicker;

    [ObservableProperty]
    private ObservableCollection<LocationSuggestion> _locationSuggestions = new();

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Google Account ───────────────────────────────────────────────────────

    private readonly IGoogleAuthService _googleAuthService;
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private ObservableCollection<GoogleCalendarItem> _googleCalendars = new();

    [ObservableProperty]
    private bool _hasGoogleCalendars;

    [ObservableProperty]
    private bool _isGoogleSignedIn;

    [ObservableProperty]
    private string _googleAccountEmail = string.Empty;

    [ObservableProperty]
    private bool _isGoogleBusy;

    [ObservableProperty]
    private string _googleStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasGoogleError;

    public ICommand GoogleSignInCommand { get; }
    public ICommand GoogleSignOutCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand RefreshGpsCommand { get; }

    [ObservableProperty]
    private string _gpsLocationDisplay = "GPS not loaded yet.";

    [ObservableProperty]
    private bool _isGpsBusy;

    // ── App Update ───────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private int _updateDownloadProgress;

    /// <summary>Desktop only: update has been downloaded and can be applied immediately.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadLinkAvailable))]
    private bool _isUpdateReady;

    /// <summary>True on any platform when a newer version has been found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadLinkAvailable))]
    private bool _isUpdateAvailable;

    /// <summary>
    /// True when a newer version was found but NOT yet ready to install directly
    /// (i.e. Android, where the user must download the APK from the releases page).
    /// </summary>
    public bool IsDownloadLinkAvailable => IsUpdateAvailable && !IsUpdateReady;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    /// <summary>
    /// True on desktop and Android (both have an update mechanism).
    /// False only on WebAssembly where in-app updates are not applicable.
    /// Note: __SKIA__ is NOT used here because it can be undefined on some
    /// Uno SDK desktop configurations; instead we exclude only __BROWSERWASM__.
    /// </summary>
    public bool IsUpdateSupported { get; } =
#if __BROWSERWASM__
        false;
#else
        true;
#endif

    /// <summary>True on Android only — used to show the Exit App button.</summary>
    public bool IsAndroid { get; } =
#if __ANDROID__
        true;
#else
        false;
#endif

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand OpenReleasesPageCommand { get; }
    public ICommand ExitAppCommand { get; }
    public ICommand ReportIssueCommand { get; }

    public SettingsViewModel(
        INavigator navigator,
        ILocationService locationService,
        IAstronomicalService astronomicalService,
        CalendarDbContext dbContext,
        IGoogleAuthService googleAuthService,
        ISyncService syncService,
        IUpdateService updateService)
    {
        _navigator = navigator;
        _locationService = locationService;
        _astronomicalService = astronomicalService;
        _dbContext = dbContext;
        _googleAuthService = googleAuthService;
        _syncService = syncService;
        _updateService = updateService;

        GoBackCommand = new AsyncRelayCommand(GoBackAsync);
        SearchLocationCommand = new AsyncRelayCommand(SearchLocationsAsync);
        SelectLocationCommand = new AsyncRelayCommand<LocationSuggestion>(SelectLocationAsync);
        RemoveLocationCommand = new AsyncRelayCommand<SavedLocationItem>(RemoveLocationAsync);
        SetActiveLocationCommand = new AsyncRelayCommand<SavedLocationItem>(SetActiveLocationImplAsync);
        GoogleSignInCommand  = new AsyncRelayCommand(GoogleSignInAsync);
        GoogleSignOutCommand = new AsyncRelayCommand(GoogleSignOutAsync);
        SyncNowCommand       = new AsyncRelayCommand(SyncNowAsync);
        RefreshGpsCommand    = new AsyncRelayCommand(RefreshGpsAsync);
        CheckForUpdatesCommand  = new AsyncRelayCommand(CheckForUpdatesAsync);
        InstallUpdateCommand    = new RelayCommand(InstallUpdate, () => IsUpdateReady);
        OpenReleasesPageCommand = new AsyncRelayCommand(OpenReleasesPageAsync);
        ExitAppCommand          = new RelayCommand(ExitApp);
        ReportIssueCommand      = new AsyncRelayCommand(ReportIssueAsync);

        // Reflect current auth state immediately
        IsGoogleSignedIn   = _googleAuthService.IsSignedIn;
        GoogleAccountEmail = _googleAuthService.UserEmail ?? string.Empty;

        // Reflect any update already found/downloaded in the background on startup
        if (_updateService.IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            IsUpdateReady     = _updateService.IsUpdateReady;
            UpdateStatusText  = _updateService.IsUpdateReady
                ? $"Version {_updateService.NewVersionString} is ready to install."
                : $"Version {_updateService.NewVersionString} is available.";
        }

        _ = LoadSettingsAsync();
    }

    public ICommand GoBackCommand { get; }
    public ICommand SearchLocationCommand { get; }
    public ICommand SelectLocationCommand { get; }
    public ICommand RemoveLocationCommand { get; }
    public ICommand SetActiveLocationCommand { get; }

    private async Task LoadSettingsAsync()
    {
        _isLoading = true;
        try
        {
            await _dbContext.InitializeAsync();

            var themeSetting = await GetSettingAsync("Theme");
            var calModeSetting = await GetSettingAsync("DefaultCalendarMode");
            var locationModeSetting = await GetSettingAsync("LocationMode");
            var astroModeSetting = await GetSettingAsync("AstronomyMode");
            var useLastCalTypeSetting = await GetSettingAsync("UseLastSelectedCalendarType");
            var showBiblicalHolidaysSetting = await GetSettingAsync("ShowBiblicalHolidays");
            var showGregorianEventsSetting = await GetSettingAsync("ShowGregorianEvents");
            var showJulianEventsSetting    = await GetSettingAsync("ShowJulianEvents");
            var showBiblicalEventsSetting  = await GetSettingAsync("ShowBiblicalEvents");

            Console.WriteLine($"[CalendarApp] Settings loaded from DB: Theme={themeSetting}, CalMode={calModeSetting}, LocMode={locationModeSetting}, AstroMode={astroModeSetting}, UseLastCalType={useLastCalTypeSetting}, ShowBiblicalHolidays={showBiblicalHolidaysSetting}");

            SelectedThemeIndex = themeSetting switch
            {
                "Dark"        => 1,
                "Light"       => 2,
                "Windows11"   => 3,
                "WindowsXP"   => 4,
                "MacOSModern" => 5,
                "MacOSClassic"=> 6,
                _             => 0  // "System" or unrecognised
            };

            UseLastSelectedCalendarType = useLastCalTypeSetting == "true";
            IsCalendarModeEditable = !UseLastSelectedCalendarType;

            SelectedCalendarModeIndex = calModeSetting switch
            {
                "Julian" => 1,
                "Biblical" => 2,
                _ => 0
            };

            if (locationModeSetting != null)
            {
                SelectedLocationModeIndex = locationModeSetting == "ManualOnly" ? 1 : 0;
                _locationService.Mode = locationModeSetting == "ManualOnly"
                    ? LocationMode.ManualOnly : LocationMode.GpsWithManualFallback;
            }
            else
            {
                SelectedLocationModeIndex = _locationService.Mode == LocationMode.ManualOnly ? 1 : 0;
            }

            if (astroModeSetting != null)
            {
                SelectedAstronomyModeIndex = astroModeSetting == "WebApi" ? 1 : 0;
                _astronomicalService.Mode = astroModeSetting == "WebApi"
                    ? AstronomyCalculationMode.WebApi : AstronomyCalculationMode.BuiltIn;
            }
            else
            {
                SelectedAstronomyModeIndex = _astronomicalService.Mode == AstronomyCalculationMode.WebApi ? 1 : 0;
            }

            ShowBiblicalHolidays  = showBiblicalHolidaysSetting == "true";
            ShowGregorianEvents   = showGregorianEventsSetting  == "true";
            ShowJulianEvents      = showJulianEventsSetting     == "true";
            ShowBiblicalEvents    = showBiblicalEventsSetting   == "true";

            await LoadSavedLocationsAsync();
            _ = RefreshGpsAsync();   // non-blocking; populates GpsLocationDisplay
            await LoadGoogleCalendarsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error loading settings: {ex}");
        }
        finally
        {
            await Task.Delay(500);
            _isLoading = false;
            Console.WriteLine("[CalendarApp] Settings load complete, saves now enabled");
        }
    }

    private async Task LoadSavedLocationsAsync()
    {
        try
        {
            await _dbContext.InitializeAsync();

            var entities = await _dbContext.Connection.Table<LocationEntity>()
                .Where(l => !l.IsFromGps)
                .ToListAsync();

            // Sort by Id so the list order is stable
            entities = entities.OrderBy(e => e.Id).ToList();

            bool hasAnyDefault = entities.Any(l => l.IsDefault);

            var items = new List<SavedLocationItem>();

            // Jerusalem is always first and is never removable
            items.Add(new SavedLocationItem(
                Id: 0,
                DisplayName: "Jerusalem, Israel",
                Coords: "Lat: 31.7683   Lon: 35.2137",
                IsActive: !hasAnyDefault,
                IsJerusalem: true));

            foreach (var e in entities)
            {
                var displayName = FormatLocationName(e.City, e.State);
                if (displayName == "Unknown Location") displayName = e.Name;
                items.Add(new SavedLocationItem(
                    Id: e.Id,
                    DisplayName: displayName,
                    Coords: $"Lat: {e.Latitude:F4}   Lon: {e.Longitude:F4}",
                    IsActive: e.IsDefault,
                    IsJerusalem: false));
            }

            SavedLocations = new ObservableCollection<SavedLocationItem>(items);
            CanAddLocation = entities.Count < 8;
            IsLocationListFull = !CanAddLocation;

            Console.WriteLine($"[CalendarApp] Loaded {items.Count} locations (Jerusalem + {entities.Count} user). CanAdd={CanAddLocation}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error loading saved locations: {ex}");
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isLoading) return;
        var theme = value switch
        {
            1 => "Dark",
            2 => "Light",
            3 => "Windows11",
            4 => "WindowsXP",
            5 => "MacOSModern",
            6 => "MacOSClassic",
            _ => "System"
        };
        _ = SaveSettingAsync("Theme", theme);
        ThemeApplier.Apply(theme);
    }

    partial void OnSelectedCalendarModeIndexChanged(int value)
    {
        if (_isLoading) return;
        var mode = value switch
        {
            1 => "Julian",
            2 => "Biblical",
            _ => "Gregorian"
        };
        _ = SaveSettingAsync("DefaultCalendarMode", mode);
    }

    partial void OnUseLastSelectedCalendarTypeChanged(bool value)
    {
        IsCalendarModeEditable = !value;
        if (_isLoading) return;
        _ = SaveSettingAsync("UseLastSelectedCalendarType", value ? "true" : "false");
    }

    partial void OnSelectedLocationModeIndexChanged(int value)
    {
        if (_isLoading) return;
        _locationService.Mode = value == 1 ? LocationMode.ManualOnly : LocationMode.GpsWithManualFallback;
        _ = SaveSettingAsync("LocationMode", value == 1 ? "ManualOnly" : "GpsWithManualFallback");
    }

    partial void OnSelectedAstronomyModeIndexChanged(int value)
    {
        if (_isLoading) return;
        _astronomicalService.Mode = value == 1 ? AstronomyCalculationMode.WebApi : AstronomyCalculationMode.BuiltIn;
        _ = SaveSettingAsync("AstronomyMode", value == 1 ? "WebApi" : "BuiltIn");
    }

    partial void OnShowBiblicalHolidaysChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveSettingAsync("ShowBiblicalHolidays", value ? "true" : "false");
    }

    partial void OnShowGregorianEventsChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveSettingAsync("ShowGregorianEvents", value ? "true" : "false");
    }

    partial void OnShowJulianEventsChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveSettingAsync("ShowJulianEvents", value ? "true" : "false");
    }

    partial void OnShowBiblicalEventsChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveSettingAsync("ShowBiblicalEvents", value ? "true" : "false");
    }

    // ── Google Account sign-in / sign-out ────────────────────────────────────

    private async Task GoogleSignInAsync()
    {
        IsGoogleBusy    = true;
        HasGoogleError  = false;
        GoogleStatusText = string.Empty;
        try
        {
            var success = await _googleAuthService.SignInAsync();
            IsGoogleSignedIn   = success;
            GoogleAccountEmail = _googleAuthService.UserEmail ?? string.Empty;
            if (!success)
            {
                // Report a diagnostic path so it's clear whether the problem is
                // a missing credentials file or a failed OAuth redirect.
                var credPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CalendarApp", "credentials.json");
                var credExists = System.IO.File.Exists(credPath);
                GoogleStatusText = credExists
                    ? "Sign-in failed. credentials.json was found but the OAuth flow did not complete. " +
                      "Check that 'http://localhost' is an authorized redirect URI in Google Cloud Console."
                    : $"Sign-in failed. credentials.json not found at:\n{credPath}\n" +
                      "Re-install the app or place the file there manually.";
                HasGoogleError = true;
            }
            else
            {
                GoogleStatusText = "Loading calendar list...";
                await _syncService.RefreshCalendarListAsync();
                await LoadGoogleCalendarsAsync();

                GoogleStatusText = "Syncing...";
                var result = await _syncService.SyncAsync();
                GoogleStatusText = result.Success
                    ? $"Synced: {result.EventsDownloaded} events downloaded"
                    : $"Sync error: {string.Join("; ", result.Errors)}";
                HasGoogleError = !result.Success;
            }
        }
        catch (Exception ex)
        {
            GoogleStatusText = $"Sign-in error: {ex.Message}";
            HasGoogleError   = true;
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    private async Task GoogleSignOutAsync()
    {
        IsGoogleBusy = true;
        try
        {
            await _googleAuthService.SignOutAsync();
            IsGoogleSignedIn   = false;
            GoogleAccountEmail = string.Empty;
            HasGoogleError     = false;
            GoogleStatusText   = string.Empty;

            // Clear the calendar list display
            foreach (var item in GoogleCalendars)
                item.PropertyChanged -= OnCalendarItemPropertyChanged;
            GoogleCalendars.Clear();
            HasGoogleCalendars = false;
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    private async Task SyncNowAsync()
    {
        IsGoogleBusy     = true;
        HasGoogleError   = false;
        GoogleStatusText = "Re-pulling from Google…";
        try
        {
            var result = await _syncService.ForceFullPullAsync();
            GoogleStatusText = result.Success
                ? result.Summary
                : $"Sync failed: {string.Join("; ", result.Errors)}";
            HasGoogleError = !result.Success;
        }
        catch (Exception ex)
        {
            GoogleStatusText = $"Sync error: {ex.Message}";
            HasGoogleError   = true;
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    private async Task LoadGoogleCalendarsAsync()
    {
        if (!_googleAuthService.IsSignedIn)
        {
            foreach (var item in GoogleCalendars)
                item.PropertyChanged -= OnCalendarItemPropertyChanged;
            GoogleCalendars.Clear();
            HasGoogleCalendars = false;
            return;
        }

        try
        {
            await _dbContext.InitializeAsync();
            var entities = await _dbContext.Connection
                .Table<GoogleCalendarListEntity>()
                .ToListAsync();

            // Unsubscribe old items before replacing
            foreach (var old in GoogleCalendars)
                old.PropertyChanged -= OnCalendarItemPropertyChanged;

            var items = entities
                .OrderByDescending(e => e.IsPrimary)
                .ThenBy(e => e.Summary)
                .Select(e => new GoogleCalendarItem
                {
                    CalendarId = e.CalendarId,
                    Summary    = e.Summary,
                    ColorHex   = e.ColorHex,
                    IsPrimary  = e.IsPrimary,
                    IsEnabled  = e.IsEnabled,
                    // Seed with the user override if set, otherwise the Google-assigned color.
                    UserColor  = GoogleCalendarItem.ParseHexColor(
                        !string.IsNullOrEmpty(e.UserColorHex) ? e.UserColorHex : e.ColorHex)
                })
                .ToList();

            foreach (var item in items)
                item.PropertyChanged += OnCalendarItemPropertyChanged;

            GoogleCalendars    = new ObservableCollection<GoogleCalendarItem>(items);
            HasGoogleCalendars = items.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error loading Google calendars: {ex}");
        }
    }

    private void OnCalendarItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not GoogleCalendarItem item) return;
        if (e.PropertyName == nameof(GoogleCalendarItem.IsEnabled))
            _ = SaveCalendarEnabledAsync(item);
        else if (e.PropertyName == nameof(GoogleCalendarItem.UserColor))
            _ = SaveCalendarUserColorAsync(item);
    }

    private async Task SaveCalendarEnabledAsync(GoogleCalendarItem item)
    {
        try
        {
            await _dbContext.InitializeAsync();
            var entity = await _dbContext.Connection
                .Table<GoogleCalendarListEntity>()
                .Where(e => e.CalendarId == item.CalendarId)
                .FirstOrDefaultAsync();
            if (entity != null)
            {
                entity.IsEnabled = item.IsEnabled;
                await _dbContext.Connection.UpdateAsync(entity);
                _googleCalendarSelectionChanged = true;
                Console.WriteLine($"[CalendarApp] Calendar '{item.Summary}' IsEnabled={item.IsEnabled}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error saving calendar enabled state: {ex}");
        }
    }

    private async Task SaveCalendarUserColorAsync(GoogleCalendarItem item)
    {
        try
        {
            await _dbContext.InitializeAsync();
            var entity = await _dbContext.Connection
                .Table<GoogleCalendarListEntity>()
                .Where(e => e.CalendarId == item.CalendarId)
                .FirstOrDefaultAsync();
            if (entity != null)
            {
                entity.UserColorHex = GoogleCalendarItem.ColorToHex(item.UserColor);
                await _dbContext.Connection.UpdateAsync(entity);
                _googleCalendarSelectionChanged = true;
                Console.WriteLine($"[CalendarApp] Calendar '{item.Summary}' UserColorHex={entity.UserColorHex}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error saving calendar user color: {ex}");
        }
    }

    private async Task RefreshGpsAsync()
    {
        IsGpsBusy = true;
        GpsLocationDisplay = "Requesting GPS…";
        try
        {
            var location = await _locationService.GetGpsLocationAsync();
            GpsLocationDisplay = location != null
                ? $"Lat: {location.Latitude:F5}   Lon: {location.Longitude:F5}\nTimezone: {location.TimeZoneId}"
                : "GPS unavailable — check that location permission is granted.";
        }
        catch (Exception ex)
        {
            GpsLocationDisplay = $"GPS error: {ex.Message}";
        }
        finally
        {
            IsGpsBusy = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates   = true;
        IsDownloadingUpdate    = false;
        IsUpdateAvailable      = false;
        IsUpdateReady          = false;
        UpdateDownloadProgress = 0;
        UpdateStatusText       = "Checking for updates…";
        try
        {
            var progress = new Progress<int>(p =>
            {
                IsDownloadingUpdate    = true;
                UpdateDownloadProgress = p;
                UpdateStatusText       = $"Downloading… {p}%";
            });

            bool found = await _updateService.CheckAndDownloadAsync(progress);
            if (found)
            {
                IsUpdateAvailable = true;
                IsUpdateReady     = _updateService.IsUpdateReady;
                UpdateStatusText  = _updateService.IsUpdateReady
                    ? $"Version {_updateService.NewVersionString} is ready to install."
                    : $"Version {_updateService.NewVersionString} is available.";
            }
            else
            {
                UpdateStatusText = "You are up to date.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
            IsDownloadingUpdate  = false;
            (InstallUpdateCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private void InstallUpdate() => _updateService.ApplyAndRestart();

    private async Task OpenReleasesPageAsync()
    {
        var url = _updateService.ReleasesPageUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Could not open releases page: {ex.Message}");
        }
    }

    private static void ExitApp()
    {
#if __ANDROID__
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#endif
    }

    private async Task ReportIssueAsync()
    {
        try
        {
            var platform =
#if __ANDROID__
                "Android";
#elif __SKIA__
                "Windows/Desktop";
#else
                "Other";
#endif
            var version = AppVersion;

            // Build a pre-filled GitHub new-issue URL. No API token needed —
            // the browser opens the GitHub form with title/body pre-populated.
            const string repoBase = "https://github.com/laytondavis/CalendarApp";
            var title = Uri.EscapeDataString($"[Bug] {platform} — <describe issue here>");
            var body = Uri.EscapeDataString(
                $"**App version:** {version}\n" +
                $"**Platform:** {platform}\n\n" +
                "**Steps to reproduce:**\n1. \n2. \n\n" +
                "**Expected behavior:**\n\n" +
                "**Actual behavior:**\n\n" +
                "**Additional context / screenshots:**\n");

            var url = $"{repoBase}/issues/new?title={title}&body={body}";
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Could not open issue reporter: {ex.Message}");
        }
    }

    private async Task SearchLocationsAsync()
    {
        if (string.IsNullOrWhiteSpace(LocationSearchQuery) || LocationSearchQuery.Length < 3)
        {
            LocationSuggestions.Clear();
            ShowLocationPicker = false;
            return;
        }

        IsSearchingLocations = true;
        try
        {
            var results = (await _locationService.SearchLocationsAsync(LocationSearchQuery)).ToList();

            if (results.Count == 1)
            {
                LocationSuggestions.Clear();
                ShowLocationPicker = false;
                await SelectLocationAsync(results[0]);
            }
            else if (results.Count > 1)
            {
                LocationSuggestions = new ObservableCollection<LocationSuggestion>(results);
                ShowLocationPicker = true;
            }
            else
            {
                LocationSuggestions.Clear();
                ShowLocationPicker = false;
                StatusMessage = "No results found";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Location search error: {ex}");
        }
        finally
        {
            IsSearchingLocations = false;
        }
    }

    /// <summary>
    /// Adds the selected search result to the saved locations list and makes it active.
    /// </summary>
    private async Task SelectLocationAsync(LocationSuggestion? suggestion)
    {
        if (suggestion == null) return;

        Console.WriteLine($"[CalendarApp] SelectLocationAsync: adding '{suggestion.Name}' to list");
        StatusMessage = $"Adding {suggestion.Name}...";

        try
        {
            var location = new LocationInfo(
                suggestion.Latitude,
                suggestion.Longitude,
                null,
                suggestion.TimeZoneId);

            var newId = await _locationService.AddLocationAsync(
                location, suggestion.Name,
                suggestion.City, suggestion.ZipCode, suggestion.County, suggestion.State);

            await _locationService.SetActiveLocationAsync(newId);

            LocationSearchQuery = string.Empty;
            LocationSuggestions.Clear();
            ShowLocationPicker = false;
            StatusMessage = $"Added: {FormatLocationName(suggestion.City, suggestion.State)}";

            await LoadSavedLocationsAsync();
        }
        catch (InvalidOperationException ex)
        {
            // Max locations reached
            StatusMessage = ex.Message;
            Console.WriteLine($"[CalendarApp] Cannot add location: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error adding location: {ex}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task RemoveLocationAsync(SavedLocationItem? item)
    {
        if (item == null || item.IsJerusalem) return;

        Console.WriteLine($"[CalendarApp] Removing location Id={item.Id} '{item.DisplayName}'");
        try
        {
            await _locationService.DeleteLocationAsync(item.Id);
            await LoadSavedLocationsAsync();
            StatusMessage = $"Removed: {item.DisplayName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error removing location: {ex}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task SetActiveLocationImplAsync(SavedLocationItem? item)
    {
        if (item == null) return;

        Console.WriteLine($"[CalendarApp] Setting active location Id={item.Id} '{item.DisplayName}'");
        try
        {
            // Id=0 means Jerusalem → clears all DB defaults
            await _locationService.SetActiveLocationAsync(item.Id);
            await LoadSavedLocationsAsync();
            StatusMessage = $"Active location: {item.DisplayName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error setting active location: {ex}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task GoBackAsync()
    {
        EnsureSettingsClosedMessageSent();
        await _navigator.GoBack(this);
    }

    /// <summary>
    /// Called by SettingsPage.OnNavigatedTo so that settings are always freshly loaded
    /// from the database whenever the page is shown, and the closed-message flag is
    /// reset so MainViewModel gets notified on the NEXT exit too.
    /// </summary>
    public void OnNavigatedToSettings()
    {
        _settingsClosedMessageSent = false;
        _googleCalendarSelectionChanged = false;
        _ = LoadSettingsAsync();
    }

    /// <summary>
    /// Loads settings and waits for the load to complete (including the 500ms cool-down).
    /// Used by SettingsPage to coordinate UI updates after settings are loaded.
    /// </summary>
    public async Task LoadSettingsAndWaitAsync()
    {
        _settingsClosedMessageSent = false;
        _googleCalendarSelectionChanged = false;
        await LoadSettingsAsync();
    }

    /// <summary>
    /// Sends SettingsClosedMessage if it hasn't been sent yet for this session.
    /// Called from both GoBackAsync (in-app back button) and SettingsPage.OnNavigatedFrom
    /// (Android native back button / swipe-back) so MainViewModel always refreshes.
    /// </summary>
    public void EnsureSettingsClosedMessageSent()
    {
        if (_settingsClosedMessageSent) return;
        _settingsClosedMessageSent = true;
        WeakReferenceMessenger.Default.Send(new SettingsClosedMessage(_googleCalendarSelectionChanged));
    }

    private bool _settingsClosedMessageSent;

    private async Task<string?> GetSettingAsync(string key)
    {
        try
        {
            await _dbContext.InitializeAsync();
            var entity = await _dbContext.Connection
                .Table<SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == key);
            return entity?.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error reading setting '{key}': {ex}");
            return null;
        }
    }

    private static string FormatLocationName(string? city, string? state)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(city)) parts.Add(city);
        if (!string.IsNullOrWhiteSpace(state)) parts.Add(state);
        return parts.Count > 0 ? string.Join(", ", parts) : "Unknown Location";
    }

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
            Console.WriteLine($"[CalendarApp] Saved setting '{key}' = '{value}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error saving setting '{key}': {ex}");
        }
    }
}

/// <summary>
/// Sent via WeakReferenceMessenger when the user navigates back from the Settings page.
/// MainViewModel listens for this to recompute the calendar with any updated settings.
/// HasGoogleChanges is true when the user toggled at least one Google calendar checkbox,
/// signalling that a sync should run to pull in newly-enabled calendars.
/// </summary>
public record SettingsClosedMessage(bool HasGoogleChanges = false);

/// <summary>
/// Represents one Google Calendar sub-calendar shown in the Settings calendar-selection list.
/// IsEnabled and UserColor are two-way bound; changes are persisted automatically.
/// </summary>
public partial class GoogleCalendarItem : ObservableObject
{
    public string CalendarId { get; init; } = string.Empty;
    public string Summary    { get; init; } = string.Empty;
    /// <summary>Color from the Google Calendar API (read-only, used as fallback).</summary>
    public string ColorHex   { get; init; } = string.Empty;
    public bool   IsPrimary  { get; init; }

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>User-selected override color, bound TwoWay to the ColorPicker.</summary>
    [ObservableProperty]
    private Windows.UI.Color _userColor;

    partial void OnUserColorChanged(Windows.UI.Color value)
    {
        // Refresh the swatch brush so the button background updates immediately.
        OnPropertyChanged(nameof(UserColorBrush));
    }

    /// <summary>Brush for the color swatch button; always opaque.</summary>
    public SolidColorBrush UserColorBrush
    {
        get
        {
            var c = UserColor;
            return new SolidColorBrush(c.A > 0 ? c : Windows.UI.Color.FromArgb(255, 100, 149, 237));
        }
    }

    // ── Static helpers ─────────────────────────────────────────────────────

    internal static Windows.UI.Color ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return default;
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return default;
        try
        {
            var r = Convert.ToByte(hex[0..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }
        catch { return default; }
    }

    internal static string ColorToHex(Windows.UI.Color c)
        => c.A == 0 ? string.Empty : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>
/// Represents one entry in the saved locations list shown in Settings.
/// Jerusalem is always Id=0 and IsJerusalem=true; user locations have positive Ids.
/// </summary>
public partial record SavedLocationItem(
    int Id,
    string DisplayName,
    string Coords,
    bool IsActive,
    bool IsJerusalem)
{
    /// <summary>True for all locations except Jerusalem (the permanent first entry).</summary>
    public bool IsRemovable => !IsJerusalem;
}
