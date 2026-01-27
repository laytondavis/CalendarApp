namespace CalendarApp.Models;

/// <summary>
/// Represents a geographic location used for astronomical calculations.
/// </summary>
public record LocationInfo(
    double Latitude,
    double Longitude,
    double? Elevation,
    string TimeZoneId)
{
    /// <summary>
    /// Gets whether this location has elevation data.
    /// </summary>
    public bool HasElevation => Elevation.HasValue;

    /// <summary>
    /// Gets the TimeZoneInfo for this location.
    /// </summary>
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>
    /// Creates a LocationInfo from coordinates with the local time zone.
    /// </summary>
    public static LocationInfo FromCoordinates(double latitude, double longitude) =>
        new(latitude, longitude, null, TimeZoneInfo.Local.Id);
}
