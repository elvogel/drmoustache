namespace DrMoustache.Minimal;

using DrMoustache;
using DrMoustache.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services
            .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
            .AddSource<MyProvider>("main");

        using var host = builder.Build();

        var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

        // Pass --check to validate without reading data or writing files.
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
}
