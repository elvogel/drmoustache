namespace DrMoustache.Sample;

using System.Globalization;
using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Reads observations from SQLite.</summary>
/// <remarks>An ordinary dependency-injected service: options and logging
/// arrive through the constructor like any other .NET component.</remarks>
public sealed class SqliteObservationProvider(
    IOptions<SqliteOptions> options,
    ILogger<SqliteObservationProvider> logger)
    : DataProvider<ObservationRecord>
{
    public override string Name => "observations";

    public override async IAsyncEnumerable<ObservationRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var path = options.Value.DatabasePath;
        logger.LogInformation("Reading observations from {Path}", path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT station, temperature, recorded_at, note FROM observations ORDER BY rowid";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return new ObservationRecord
            {
                Station = reader.GetString(0),
                Temperature = reader.GetDecimal(1),
                RecordedAt = DateTimeOffset.Parse(
                    reader.GetString(2), CultureInfo.InvariantCulture),
                Note = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
    }
}
