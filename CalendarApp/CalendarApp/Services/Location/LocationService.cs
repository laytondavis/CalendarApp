using CalendarApp.Data;
using CalendarApp.Data.Entities;
using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using Windows.Devices.Geolocation;

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
        // Set once; ParseAdd throws if the header is added a second time
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("CalendarApp/1.0");
    }

    public async Task<LocationInfo?> GetCurrentLocationAsync()
    {
        await _context.InitializeAsync();
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
        await _context.InitializeAsync();
        var entity = await _context.Connection.Table<LocationEntity>()
            .FirstOrDefaultAsync(l => l.IsDefault);

        return entity != null ? MapToLocationInfo(entity) : null;
    }

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            var status = await Geolocator.RequestAccessAsync();
            return status == GeolocationAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveDefaultLocationAsync(LocationInfo location, string name, string? city = null, string? zipCode = null, string? county = null, string? state = null)
    {
        await _context.InitializeAsync();

        // Delete all previously saved manual locations (not GPS)
        var oldLocations = await _context.Connection.Table<LocationEntity>()
            .Where(l => !l.IsFromGps)
            .ToListAsync();
        foreach (var old in oldLocations)
            await _context.Connection.DeleteAsync(old);

        // Insert fresh
        var entity = new LocationEntity
        {
            Name = name,
            City = city,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Elevation = location.Elevation,
            TimeZoneId = location.TimeZoneId ?? TimeZoneInfo.Local.Id,
            IsDefault = true,
            IsFromGps = false,
            ZipCode = zipCode,
            County = county,
            State = state,
            LastUpdatedUtc = DateTime.UtcNow
        };
        await _context.Connection.InsertAsync(entity);
        Console.WriteLine($"[CalendarApp] LocationService: saved '{name}' (City={city}, ZIP={zipCode}, IsDefault=true)");
    }

    public async Task<IEnumerable<SavedLocation>> GetSavedLocationsAsync()
    {
        await _context.InitializeAsync();
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

    public async Task<int> AddLocationAsync(LocationInfo location, string name, string? city = null, string? zipCode = null, string? county = null, string? state = null)
    {
        await _context.InitializeAsync();

        var count = await _context.Connection.Table<LocationEntity>()
            .Where(l => !l.IsFromGps)
            .CountAsync();

        if (count >= 8)
            throw new InvalidOperationException("Maximum of 8 saved locations reached.");

        var entity = new LocationEntity
        {
            Name = name,
            City = city,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Elevation = location.Elevation,
            TimeZoneId = location.TimeZoneId ?? TimeZoneInfo.Local.Id,
            IsDefault = false,
            IsFromGps = false,
            ZipCode = zipCode,
            County = county,
            State = state,
            LastUpdatedUtc = DateTime.UtcNow
        };
        await _context.Connection.InsertAsync(entity);
        Console.WriteLine($"[CalendarApp] LocationService: added '{name}' to list (Id={entity.Id})");
        return entity.Id;
    }

    public async Task SetActiveLocationAsync(int id)
    {
        await _context.InitializeAsync();

        var all = await _context.Connection.Table<LocationEntity>()
            .Where(l => !l.IsFromGps)
            .ToListAsync();

        foreach (var loc in all)
        {
            bool shouldBeDefault = id > 0 && loc.Id == id;
            if (loc.IsDefault != shouldBeDefault)
            {
                loc.IsDefault = shouldBeDefault;
                await _context.Connection.UpdateAsync(loc);
            }
        }
        // id == 0 → no DB record has IsDefault=true → BiblicalCalendarService falls back to Jerusalem
        Console.WriteLine($"[CalendarApp] LocationService: set active location Id={id}");
    }

    public async Task DeleteLocationAsync(int id)
    {
        await _context.InitializeAsync();

        var entity = await _context.Connection.Table<LocationEntity>()
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsFromGps);
        if (entity == null) return;

        await _context.Connection.DeleteAsync(entity);
        Console.WriteLine($"[CalendarApp] LocationService: deleted location Id={id} '{entity.Name}'");
        // If this was the active location, GetDefaultLocationAsync now returns null → Jerusalem fallback
    }

    public async Task<IEnumerable<LocationSuggestion>> SearchLocationsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            return Enumerable.Empty<LocationSuggestion>();

        try
        {
            // Using Nominatim OpenStreetMap geocoding with address details
            var url = $"https://nominatim.openstreetmap.org/search?" +
                      $"q={Uri.EscapeDataString(query)}&format=json&limit=5&addressdetails=1";

            var response = await _httpClient.GetStringAsync(url);
            var json = System.Text.Json.JsonDocument.Parse(response);

            // Extract all data from the JsonDocument synchronously first (JsonDocument is not thread-safe for concurrent reads)
            var rawItems = new List<(double lat, double lon, string name, string displayName,
                                     string? city, string? zipCode, string? county, string? state)>();

            foreach (var item in json.RootElement.EnumerateArray())
            {
                var lat = double.Parse(item.GetProperty("lat").GetString()!);
                var lon = double.Parse(item.GetProperty("lon").GetString()!);
                var displayName = item.GetProperty("display_name").GetString()!;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString()! : displayName.Split(',')[0];

                string? city = null, zipCode = null, county = null, state = null;
                if (item.TryGetProperty("address", out var address))
                {
                    if (address.TryGetProperty("city", out var ci)) city = ci.GetString();
                    else if (address.TryGetProperty("town", out var tn)) city = tn.GetString();
                    else if (address.TryGetProperty("village", out var vi)) city = vi.GetString();
                    else if (address.TryGetProperty("municipality", out var mu)) city = mu.GetString();

                    if (address.TryGetProperty("postcode", out var pc)) zipCode = pc.GetString();
                    if (address.TryGetProperty("county", out var co)) county = co.GetString();
                    if (address.TryGetProperty("state", out var st)) state = st.GetString();
                }

                rawItems.Add((lat, lon, name, displayName, city, zipCode, county, state));
            }

            // Look up the real timezone for each result in parallel.
            // Falls back to the machine's local timezone if the API call fails.
            var tasks = rawItems.Select(async r =>
            {
                var timeZoneId = await LookupTimezoneAsync(r.lat, r.lon) ?? TimeZoneInfo.Local.Id;
                return new LocationSuggestion(r.name, r.displayName, r.lat, r.lon,
                    timeZoneId, r.city, r.zipCode, r.county, r.state);
            });

            return await Task.WhenAll(tasks);
        }
        catch
        {
            return Enumerable.Empty<LocationSuggestion>();
        }
    }

    /// <summary>
    /// Calls timeapi.io to look up the IANA/Windows timezone ID for a pair of coordinates.
    /// Returns null if the call fails so callers can apply their own fallback.
    /// </summary>
    private async Task<string?> LookupTimezoneAsync(double lat, double lon)
    {
        try
        {
            var url = $"https://timeapi.io/api/TimeZone/coordinate?latitude={lat:F6}&longitude={lon:F6}";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("timeZone", out var tzProp))
            {
                var ianaId = tzProp.GetString();
                if (!string.IsNullOrEmpty(ianaId) && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out _))
                    return ianaId;
            }
        }
        catch { }
        return null;
    }

    public async Task<LocationInfo?> GetGpsLocationAsync()
    {
        try
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                Console.WriteLine("[CalendarApp] GPS: access not granted.");
                return null;
            }

            var geolocator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await geolocator.GetGeopositionAsync(
                maximumAge: TimeSpan.FromMinutes(10),
                timeout: TimeSpan.FromSeconds(20));

            var lat = position.Coordinate.Point.Position.Latitude;
            var lon = position.Coordinate.Point.Position.Longitude;
            var alt = position.Coordinate.Point.Position.Altitude;

            // Use the device's local timezone for GPS fixes — most accurate
            // for the current location and avoids network call to timeapi.io.
            var tzId = TimeZoneInfo.Local.Id;

            Console.WriteLine($"[CalendarApp] GPS fix: {lat:F5}, {lon:F5}, tz={tzId}");
            return new LocationInfo(lat, lon, alt, tzId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] GPS error: {ex.Message}");
            return null;
        }
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
