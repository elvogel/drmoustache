namespace DrMoustache.Sample;

/// <summary>Bound from the <c>Sqlite</c> section of appsettings.json.</summary>
public sealed class SqliteOptions
{
    public string DatabasePath { get; set; } = "weather.db";
}
