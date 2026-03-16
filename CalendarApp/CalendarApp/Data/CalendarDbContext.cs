using CalendarApp.Data.Entities;
using SQLite;

namespace CalendarApp.Data;

/// <summary>
/// Database context for the calendar application.
/// </summary>
public class CalendarDbContext
{
    private readonly SQLiteAsyncConnection _connection;
    private bool _initialized;

    public CalendarDbContext()
    {
        var dbPath = GetDatabasePath();
        _connection = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
    }

    /// <summary>
    /// Gets the database connection.
    /// </summary>
    public SQLiteAsyncConnection Connection => _connection;

    /// <summary>
    /// Ensures the database is initialized with all tables.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _connection.CreateTableAsync<EventEntity>();
        await _connection.CreateTableAsync<RecurrenceEntity>();
        await _connection.CreateTableAsync<ReminderEntity>();
        await _connection.CreateTableAsync<LocationEntity>();
        await _connection.CreateTableAsync<SettingsEntity>();
        await _connection.CreateTableAsync<SyncStateEntity>();
        await _connection.CreateTableAsync<GoogleCalendarListEntity>();

        // Explicitly add columns that were added after initial release.
        // ALTER TABLE throws if the column already exists — that's fine, we catch it.
        await TryAddColumnAsync("Locations", "City", "TEXT");
        await TryAddColumnAsync("Locations", "ZipCode", "TEXT");
        await TryAddColumnAsync("Locations", "County", "TEXT");
        await TryAddColumnAsync("Locations", "State", "TEXT");

        await TryAddColumnAsync("Events", "EventScopeValue", "INTEGER DEFAULT 0");
        await TryAddColumnAsync("Events", "GoogleAccountAlias", "TEXT DEFAULT ''");

        await TryAddColumnAsync("GoogleCalendars", "UserColorHex", "TEXT DEFAULT ''");

        _initialized = true;
    }

    private async Task TryAddColumnAsync(string table, string column, string type)
    {
        try
        {
            await _connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
            Console.WriteLine($"[CalendarApp] DB: Added column {table}.{column}");
        }
        catch
        {
            // Column already exists — ignore
        }
    }

    /// <summary>
    /// Gets the database file path.
    /// </summary>
    private static string GetDatabasePath()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(folder, "CalendarApp");

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        return Path.Combine(appFolder, "calendar.db");
    }

    /// <summary>
    /// Closes the database connection.
    /// </summary>
    public async Task CloseAsync()
    {
        await _connection.CloseAsync();
    }
}
