using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for managing location data used in astronomical calculations.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Gets or sets the location mode (GPS with fallback or manual only).
    /// </summary>
    LocationMode Mode { get; set; }

    /// <summary>
    /// Gets whether location services are enabled on the device.
    /// </summary>
    bool IsLocationEnabled { get; }

    /// <summary>
    /// Gets the current location asynchronously.
    /// Falls back to stored location if GPS is unavailable and mode allows.
    /// </summary>
    Task<LocationInfo?> GetCurrentLocationAsync();

    /// <summary>
    /// Gets the last known location.
    /// </summary>
    Task<LocationInfo?> GetLastKnownLocationAsync();

    /// <summary>
    /// Gets the default saved location.
    /// </summary>
    Task<LocationInfo?> GetDefaultLocationAsync();

    /// <summary>
    /// Requests location permission from the user.
    /// </summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Saves a location as the default.
    /// </summary>
    Task SaveDefaultLocationAsync(LocationInfo location, string name, string? city = null, string? zipCode = null, string? county = null, string? state = null);

    /// <summary>
    /// Gets all saved locations.
    /// </summary>
    Task<IEnumerable<SavedLocation>> GetSavedLocationsAsync();

    /// <summary>
    /// Adds a location to the saved list without replacing others. Max 8 user locations.
    /// Returns the Id of the newly-added location.
    /// </summary>
    Task<int> AddLocationAsync(LocationInfo location, string name, string? city = null, string? zipCode = null, string? county = null, string? state = null);

    /// <summary>
    /// Sets the active (default) location. Pass id=0 to clear all defaults and fall back to Jerusalem.
    /// </summary>
    Task SetActiveLocationAsync(int id);

    /// <summary>
    /// Deletes a user-saved location by Id.
    /// </summary>
    Task DeleteLocationAsync(int id);

    /// <summary>
    /// Searches for locations by name (geocoding).
    /// </summary>
    Task<IEnumerable<LocationSuggestion>> SearchLocationsAsync(string query);

    /// <summary>
    /// Gets the current GPS fix directly from device hardware.
    /// Returns null if GPS is unavailable, denied, or the platform doesn't support it.
    /// </summary>
    Task<LocationInfo?> GetGpsLocationAsync();
}

/// <summary>
/// Location service mode.
/// </summary>
public enum LocationMode
{
    /// <summary>
    /// Use GPS when available, fall back to manual selection.
    /// </summary>
    GpsWithManualFallback,

    /// <summary>
    /// Always use manual selection, never use GPS.
    /// </summary>
    ManualOnly
}

/// <summary>
/// A saved location with metadata.
/// </summary>
public partial record SavedLocation(
    int Id,
    string Name,
    LocationInfo Location,
    bool IsDefault,
    bool IsFromGps,
    DateTime LastUpdatedUtc);

/// <summary>
/// A location suggestion from geocoding search.
/// </summary>
public record LocationSuggestion(
    string Name,
    string DisplayName,
    double Latitude,
    double Longitude,
    string TimeZoneId,
    string? City = null,
    string? ZipCode = null,
    string? County = null,
    string? State = null);
