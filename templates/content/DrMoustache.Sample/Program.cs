namespace DrMoustache.Sample;

using DrMoustache;
using DrMoustache.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<SqliteOptions>(builder.Configuration.GetSection("Sqlite"));

        builder.Services
            .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
            .AddSource<SqliteObservationProvider>("observations");

        using var host = builder.Build();

        Seed(host.Services.GetRequiredService<IOptions<SqliteOptions>>().Value.DatabasePath);

        var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

        if (args.Contains("--check", StringComparer.Ordinal))
        {
            var check = await renderer.CheckAsync().ConfigureAwait(false);
            Console.Write(check.Summary);
            return check.Success ? 0 : 1;
        }

        var result = await renderer.RenderAsync().ConfigureAwait(false);
        Console.Write(result.Summary);
        return result.Success ? 0 : 1;
    }

    /// <summary>Creates and fills the database on first run, so the sample is
    /// self-contained and the template package stays text-only.</summary>
    private static void Seed(string path)
    {
        if (File.Exists(path)) return;

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE observations (
                station     TEXT NOT NULL,
                temperature REAL NOT NULL,
                recorded_at TEXT NOT NULL,
                note        TEXT);

            INSERT INTO observations (station, temperature, recorded_at, note) VALUES
                ('alpha',  12.5,  '2026-08-05T09:00:00+00:00', NULL),
                ('beta',   -3.25, '2026-08-05T09:05:00+00:00',
                 'sensor drift | readings *suspect*'),
                ('beta',   -3.00, '2026-08-05T09:10:00+00:00', NULL);
            """;

        command.ExecuteNonQuery();
    }
}
