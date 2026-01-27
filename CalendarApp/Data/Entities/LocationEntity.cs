using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for saved locations.
/// </summary>
[Table("Locations")]
public class LocationEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? Elevation { get; set; }

    [MaxLength(100)]
    public string TimeZoneId { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsFromGps { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}
