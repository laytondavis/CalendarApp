using Uno.Resizetizer;
using CalendarApp.Data;
using CalendarApp.Models;
using CalendarApp.Services;
using CalendarApp.Services.Interfaces;
using CalendarApp.Services.Calendar;
using CalendarApp.Services.Astronomy;
using CalendarApp.Services.Data;
using CalendarApp.Services.Google;
using CalendarApp.Services.Location;
#if __SKIA__
using Velopack;
using Velopack.Sources;
#endif

namespace CalendarApp;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    public Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Console.WriteLine("[CalendarApp] OnLaunched starting...");

        // Catch unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Console.WriteLine($"[CalendarApp] UNHANDLED: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (s, e) =>
            Console.WriteLine($"[CalendarApp] UNOBSERVED TASK: {e.Exception}");

        // ── Show window immediately with a loading indicator ─────────────────
        // NavigateAsync builds the entire DI container before Shell renders, so
        // without this the window stays hidden until all services are resolved.
        // Setting content and activating the window here means the user sees a
        // spinner right away; NavigateAsync then replaces the content with Shell.
        var builder = this.CreateBuilder(args);
        MainWindow = builder.Window;

        MainWindow.Content = new Grid
        {
            Children =
            {
                new ProgressRing
                {
                    IsActive = true,
                    Width = 80,
                    Height = 80,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                }
            }
        };

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();
        MainWindow.Activate();

        // ── Configure host (pure registrations — fast) ───────────────────────
        builder = builder
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .UseHttp((context, services) => {
#if DEBUG
                // DelegatingHandler will be automatically injected
                services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif

})
                .ConfigureServices((context, services) =>
                {
                    // HttpClient for services that need it
                    services.AddSingleton<HttpClient>();

                    // Database
                    services.AddSingleton<CalendarDbContext>();

                    // Astronomical Service
                    services.AddSingleton<IAstronomicalService, AstronomicalService>();

                    // Location Service
                    services.AddSingleton<ILocationService, LocationService>();

                    // Calendar Calculation Services (Strategy Pattern)
                    services.AddSingleton<GregorianCalendarService>();
                    services.AddSingleton<JulianCalendarService>();
                    services.AddSingleton<BiblicalCalendarService>();
                    services.AddSingleton<Func<CalendarMode, ICalendarCalculationService>>(sp => mode =>
                    {
                        return mode switch
                        {
                            CalendarMode.Gregorian => sp.GetRequiredService<GregorianCalendarService>(),
                            CalendarMode.Julian => sp.GetRequiredService<JulianCalendarService>(),
                            CalendarMode.Biblical => sp.GetRequiredService<BiblicalCalendarService>(),
                            _ => sp.GetRequiredService<GregorianCalendarService>()
                        };
                    });

                    // Data Repositories
                    services.AddSingleton<IEventRepository, EventRepository>();

                    // Biblical Holiday Service
                    services.AddSingleton<IBiblicalHolidayService, BiblicalHolidayService>();

                    // Google Calendar Services
                    services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
                    services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
                    services.AddSingleton<ISyncService, SyncService>();
                })
                .UseNavigation(RegisterRoutes)
            );

        // ── Navigate — builds DI container and renders Shell ─────────────────
        Console.WriteLine("[CalendarApp] Builder configured, navigating to Shell...");
        try
        {
            Host = await builder.NavigateAsync<Shell>();
            Console.WriteLine("[CalendarApp] Navigation to Shell complete.");

            // Restore persisted settings
            await RestoreSettingsAsync();

            // Initialize read-only Google accounts from config
            await InitializeReadOnlyGoogleAccountsAsync();

            // Check for app updates in the background (desktop only).
            // If a newer release is found on GitHub it is downloaded silently;
            // it will be applied the next time the app is launched.
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Navigation FAILED: {ex}");
        }
    }

    private async Task RestoreSettingsAsync()
    {
        try
        {
            if (Host == null) return;
            var dbContext = Host.Services.GetRequiredService<CalendarDbContext>();
            await dbContext.InitializeAsync();

            // Restore location mode
            var locationModeSetting = await dbContext.Connection
                .Table<Data.Entities.SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "LocationMode");
            if (locationModeSetting != null)
            {
                var locationService = Host.Services.GetRequiredService<ILocationService>();
                locationService.Mode = locationModeSetting.Value == "ManualOnly"
                    ? LocationMode.ManualOnly
                    : LocationMode.GpsWithManualFallback;
            }

            // Restore astronomy mode
            var astroModeSetting = await dbContext.Connection
                .Table<Data.Entities.SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "AstronomyMode");
            if (astroModeSetting != null)
            {
                var astroService = Host.Services.GetRequiredService<IAstronomicalService>();
                astroService.Mode = astroModeSetting.Value == "WebApi"
                    ? AstronomyCalculationMode.WebApi
                    : AstronomyCalculationMode.BuiltIn;
            }

            // Restore visual theme
            var themeSetting = await dbContext.Connection
                .Table<Data.Entities.SettingsEntity>()
                .FirstOrDefaultAsync(s => s.Key == "Theme");
            if (themeSetting != null)
                ThemeApplier.Apply(themeSetting.Value);

            Console.WriteLine("[CalendarApp] Settings restored.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error restoring settings: {ex}");
        }
    }

    private async Task InitializeReadOnlyGoogleAccountsAsync()
    {
        try
        {
            if (Host == null) return;

            var appConfig = Host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppConfig>>().Value;
            if (appConfig.GoogleAccounts.Count == 0) return;

            var authService = Host.Services.GetRequiredService<IGoogleAuthService>();
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var calendarAppFolder = Path.Combine(appDataFolder, "CalendarApp");

            foreach (var account in appConfig.GoogleAccounts.Where(a => a.ReadOnly))
            {
                if (string.IsNullOrWhiteSpace(account.Alias) || string.IsNullOrWhiteSpace(account.CredentialsFile))
                {
                    Console.WriteLine($"[CalendarApp] Skipping read-only account with missing Alias or CredentialsFile.");
                    continue;
                }

                // Resolve relative paths against the app data folder.
                var credPath = Path.IsPathRooted(account.CredentialsFile)
                    ? account.CredentialsFile
                    : Path.Combine(calendarAppFolder, account.CredentialsFile);

                Console.WriteLine($"[CalendarApp] Initializing read-only Google account '{account.Alias}'...");
                var success = await authService.InitializeReadOnlyAccountAsync(account.Alias, credPath);
                Console.WriteLine($"[CalendarApp] Read-only account '{account.Alias}': {(success ? "OK" : "FAILED")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error initializing read-only Google accounts: {ex}");
        }
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellViewModel)),
            new ViewMap<MainPage, MainViewModel>(),
            new ViewMap<EventEditorPage, EventEditorViewModel>(),
            new ViewMap<SettingsPage, SettingsViewModel>(),
            new DataViewMap<SecondPage, SecondViewModel, Entity>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
                Nested:
                [
                    new ("Main", View: views.FindByViewModel<MainViewModel>(), IsDefault:true),
                    new ("EventEditor", View: views.FindByViewModel<EventEditorViewModel>()),
                    new ("Settings", View: views.FindByViewModel<SettingsViewModel>()),
                    new ("Second", View: views.FindByViewModel<SecondViewModel>()),
                ]
            )
        );
    }

    /// <summary>
    /// Checks GitHub Releases for a newer version of the app.
    /// If found, downloads it silently in the background; the update is
    /// applied the next time the app is launched (via VelopackApp.Build().Run()
    /// in Program.cs).  Only active on desktop builds.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
#if __SKIA__
        try
        {
            if (Host == null) return;

            var appConfig = Host.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<AppConfig>>().Value;

            if (string.IsNullOrWhiteSpace(appConfig.GithubRepo) ||
                appConfig.GithubRepo.Contains("YOUR_USERNAME"))
            {
                Console.WriteLine("[CalendarApp] Update check skipped: GithubRepo not configured.");
                return;
            }

            var manager = new UpdateManager(
                new GithubSource(appConfig.GithubRepo, null, false));

            var newVersion = await manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                Console.WriteLine("[CalendarApp] App is up to date.");
                return;
            }

            Console.WriteLine($"[CalendarApp] Update available: v{newVersion.TargetFullRelease.Version} — downloading...");
            await manager.DownloadUpdatesAsync(newVersion);
            Console.WriteLine("[CalendarApp] Update downloaded. Will apply on next restart.");
        }
        catch (Exception ex)
        {
            // Update errors are non-fatal; log and continue.
            Console.WriteLine($"[CalendarApp] Update check failed: {ex.Message}");
        }
#endif
    }
}
