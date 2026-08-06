namespace DrMoustache.Minimal;

using DrMoustache;
using DrMoustache.Discovery;
using DrMoustache.Output;
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

        // A broken template or an unwritable output directory is the user's
        // problem to fix, not a bug to report, so it gets a message rather than
        // a stack trace. Only these types are caught: anything else really is a
        // bug, and the trace is the useful thing to see.
        try
        {
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
        catch (Exception ex) when (ex is RenderException or OutputException)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
