using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Location;

/// <summary>
/// Service for managing location data.
/// </summary>
public class LocationService : ILocationService
{
    private readonly CalendarDbContext _context;
    private readonly HttpClient _httpClient;

    public LocationMode Mode { get; set; } = LocationMode.GpsWithManualFallback;

    public bool IsLocationEnabled => true; // Platform-specific check would go here

    public LocationService(CalendarDbContext context, HttpClient httpClient)
    {
        _context = context;
        _httpClient = httpClient;
    }

    public async Task<LocationInfo?> GetCurrentLocationAsync()
    {
        if (Mode == LocationMode.ManualOnly)
        {
            return await GetDefaultLocationAsync();
        }

        try
        {
            // Try to get GPS location
            var gpsLocation = await GetGpsLocationAsync();
            if (gpsLocation != null)
            {
                // Cache the GPS location
                await SaveGpsLocationAsync(gpsLocation);
                return gpsLocation;
            }
        }
        catch
        {
            // GPS failed, fall back to manual location
        }

        return await GetDefaultLocationAsync();
    }

    public async Task<LocationInfo?> GetLastKnownLocationAsync()
    {
        var gpsEntity = await _context.Connection.Table<LocationEntity>()
            .Where(l => l.IsFromGps)
            .OrderByDescending(l => l.LastUpdatedUtc)
            .FirstOrDefaultAsync();

        return gpsEntity != null ? MapToLocationInfo(gpsEntity) : await GetDefaultLocationAsync();
    }

    public async Task<LocationInfo?> GetDefaultLocationAsync()
    {
        var entity = await _context.Connection.Table<LocationEntity>()
            .FirstOrDefaultAsync(l => l.IsDefault);

        return entity != null ? MapToLocationInfo(entity) : null;
    }

    public async Task<bool> RequestPermissionAsync()
    {
        // Platform-specific permission request would go here
        // For now, return true
        return await Task.FromResult(true);
    }

    public async Task SaveDefaultLocationAsync(LocationInfo location, string name)
    {
        // Clear existing default
        var existingDefault = await _context.Connection.Table<LocationEntity>()
            .FirstOrDefaultAsync(l => l.IsDefault);

        if (existingDefault != null)
        {
            existingDefault.IsDefault = false;
            await _context.Connection.UpdateAsync(existingDefault);
        }

        // Check if this location already exists
        var existing = await _context.Connection.Table<LocationEntity>()
            .FirstOrDefaultAsync(l =>
                Math.Abs(l.Latitude - location.Latitude) < 0.001 &&
                Math.Abs(l.Longitude - location.Longitude) < 0.001);

        if (existing != null)
        {
            existing.IsDefault = true;
            existing.Name = name;
            existing.LastUpdatedUtc = DateTime.UtcNow;
            await _context.Connection.UpdateAsync(existing);
        }
        else
        {
            var entity = new LocationEntity
            {
                Name = name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Elevation = location.Elevation,
                TimeZoneId = location.TimeZoneId,
                IsDefault = true,
                IsFromGps = false,
                LastUpdatedUtc = DateTime.UtcNow
            };
            await _context.Connection.InsertAsync(entity);
        }
    }

    public async Task<IEnumerable<SavedLocation>> GetSavedLocationsAsync()
    {
        var entities = await _context.Connection.Table<LocationEntity>()
            .Where(l => !l.IsFromGps)
            .OrderBy(l => l.Name)
            .ToListAsync();

        return entities.Select(e => new SavedLocation(
            e.Id,
            e.Name,
            MapToLocationInfo(e),
            e.IsDefault,
            e.IsFromGps,
            e.LastUpdatedUtc));
    }

    public async Task<IEnumerable<LocationSuggestion>> SearchLocationsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return Enumerable.Empty<LocationSuggestion>();
        }

        try
        {
            // Using Nominatim OpenStreetMap geocoding
            var url = $"https://nominatim.openstreetmap.org/search?" +
                      $"q={Uri.EscapeDataString(query)}&format=json&limit=5";

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CalendarApp/1.0");
            var response = await _httpClient.GetStringAsync(url);
            var json = System.Text.Json.JsonDocument.Parse(response);

            var suggestions = new List<LocationSuggestion>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var lat = double.Parse(item.GetProperty("lat").GetString()!);
                var lon = double.Parse(item.GetProperty("lon").GetString()!);
                var displayName = item.GetProperty("display_name").GetString()!;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString()! : displayName.Split(',')[0];

                // Try to determine timezone (simplified - would need proper timezone lookup)
                var timeZoneId = TimeZoneInfo.Local.Id;

                suggestions.Add(new LocationSuggestion(
                    name,
                    displayName,
                    lat,
                    lon,
                    timeZoneId));
            }

            return suggestions;
        }
        catch
        {
            return Enumerable.Empty<LocationSuggestion>();
        }
    }

    private async Task<LocationInfo?> GetGpsLocationAsync()
    {
        // Platform-specific GPS implementation would go here
        // This is a placeholder that returns null (GPS not available)
        await Task.Delay(100); // Simulate async operation
        return null;
    }

    private async Task SaveGpsLocationAsync(LocationInfo location)
    {
        // Remove old GPS locations (keep only the latest)
        var oldGpsLocations = await _context.Connection.Table<LocationEntity>()
            .Where(l => l.IsFromGps)
            .ToListAsync();

        foreach (var old in oldGpsLocations)
        {
            await _context.Connection.DeleteAsync(old);
        }

        var entity = new LocationEntity
        {
            Name = "GPS Location",
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Elevation = location.Elevation,
            TimeZoneId = location.TimeZoneId,
            IsDefault = false,
            IsFromGps = true,
            LastUpdatedUtc = DateTime.UtcNow
        };

        await _context.Connection.InsertAsync(entity);
    }

    private static LocationInfo MapToLocationInfo(LocationEntity entity)
    {
        return new LocationInfo(
            entity.Latitude,
            entity.Longitude,
            entity.Elevation,
            entity.TimeZoneId);
    }
}
