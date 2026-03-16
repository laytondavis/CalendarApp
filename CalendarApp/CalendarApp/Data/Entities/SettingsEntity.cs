using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for application settings.
/// </summary>
[Table("Settings")]
public class SettingsEntity
{
    [PrimaryKey]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ValueType { get; set; } = string.Empty;
}
