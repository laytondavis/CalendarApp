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
using CalendarApp.Services.Update;

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

                    // App update service (Velopack on desktop; no-op on other platforms)
                    services.AddSingleton<IUpdateService, UpdateService>();
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

            // Copy credentials.json from bundled location to AppData on first install
            await EnsureCredentialsInstalledAsync();

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

    /// <summary>
    /// On the first launch after install, copies credentials.json from the bundled
    /// location to the AppData folder that GoogleAuthService reads from.
    /// Desktop: copies from the app's base directory (included via &lt;Content&gt; in .csproj).
    /// Android: extracts from the APK assets folder (included via &lt;AndroidAsset&gt; in .csproj).
    /// No-ops if the destination file already exists.
    /// </summary>
    private static async Task EnsureCredentialsInstalledAsync()
    {
        try
        {
            var destFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CalendarApp");
            var destPath = Path.Combine(destFolder, "credentials.json");

            // If a valid-looking file already exists in AppData (> 50 bytes = real JSON),
            // assume it was installed in a prior version — preserve it across updates.
            if (File.Exists(destPath) && new FileInfo(destPath).Length > 50)
            {
                Console.WriteLine("[CalendarApp] credentials.json already exists in AppData — preserving across update.");
                return;
            }

            Directory.CreateDirectory(destFolder);

#if __ANDROID__
            try
            {
                using var assetStream = Android.App.Application.Context.Assets!.Open("credentials.json");
                using var destStream = File.Create(destPath);
                await assetStream.CopyToAsync(destStream);
                Console.WriteLine("[CalendarApp] credentials.json installed from Android asset.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarApp] Could not load credentials.json from Android assets: {ex.Message}");
            }
#elif __SKIA__
            // Search for credentials.json in multiple locations
            var searchPaths = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "credentials.json"),
                Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? "", "credentials.json"),
                // Also try going up two levels (in case of Velopack nested dirs)
                Path.Combine(Directory.GetParent(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? "")?.FullName ?? "", "credentials.json"),
            };

            foreach (var srcPath in searchPaths)
            {
                if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) continue;

                try
                {
                    File.Copy(srcPath, destPath, overwrite: true);
                    Console.WriteLine($"[CalendarApp] credentials.json installed to AppData from: {srcPath}");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CalendarApp] Could not copy credentials.json from {srcPath}: {ex.Message}");
                }
            }

            // If we reach here, credentials.json was not found in any location.
            // This is expected when the CI secret was not set during the build.
            Console.WriteLine("[CalendarApp] credentials.json not found in app directory or AppData.");
            Console.WriteLine($"[CalendarApp] Expected location: {destPath}");
            Console.WriteLine("[CalendarApp] Ensure GOOGLE_CREDENTIALS_JSON_BASE64 secret is set in GitHub to include credentials during build.");
            await Task.CompletedTask;
#else
            await Task.CompletedTask;
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] EnsureCredentialsInstalledAsync failed: {ex.Message}");
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
    /// Silently checks for a newer version on startup via the registered IUpdateService.
    /// Downloads the update in the background; the user can then apply it from the
    /// Settings → About tab without restarting manually.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            if (Host == null) return;
            var updateService = Host.Services.GetRequiredService<IUpdateService>();
            await updateService.CheckAndDownloadAsync();   // no progress reporting on silent check
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Background update check failed: {ex.Message}");
        }
    }
}
