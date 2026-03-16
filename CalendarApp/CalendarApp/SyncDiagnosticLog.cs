namespace CalendarApp;

/// <summary>
/// Writes timestamped sync diagnostic entries to a text file so the sync pipeline
/// can be analysed offline without a debugger.
///
/// Log file location: %LocalAppData%\CalendarApp\sync_log.txt
/// </summary>
internal static class SyncDiagnosticLog
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CalendarApp", "sync_log.txt");

    /// <summary>
    /// Call once at app startup. Trims the log to the last 200 lines from
    /// previous sessions and writes a session-start marker.
    /// </summary>
    public static void StartSession()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);

            // Keep the last 200 lines from old sessions for context.
            if (File.Exists(LogPath))
            {
                var lines = File.ReadAllLines(LogPath);
                if (lines.Length > 200)
                    File.WriteAllLines(LogPath, lines[^200..]);
            }

            File.AppendAllText(LogPath,
                Environment.NewLine +
                $"=== SESSION START {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===" +
                Environment.NewLine);
        }
        catch { /* never crash on diagnostics */ }
    }

    /// <summary>
    /// Appends a timestamped line to the log file and echoes it to Console.
    /// Safe to call from any thread; silently swallows I/O errors.
    /// </summary>
    public static void Write(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine($"[SyncLog] {message}");
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }
}
