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

        _initialized = true;
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
