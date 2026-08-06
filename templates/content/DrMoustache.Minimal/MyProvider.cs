namespace DrMoustache.Minimal;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>Supplies the records your templates render.</summary>
/// <remarks>This is the file to replace. A provider is an ordinary
/// dependency-injected service, so declare whatever you need in the
/// constructor — an HttpClient, a DbContext, a queue client, IOptions.</remarks>
public sealed class MyProvider(ILogger<MyProvider> logger) : DataProvider<MyRecord>
{
    /// <summary>Matched against the <c>source:</c> key in a template.</summary>
    public override string Name => "main";

    public override async IAsyncEnumerable<MyRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        logger.LogInformation("Reading records");

        // TODO: replace everything below with your real data source.
        // The placeholder exists so `dotnet run` works before you write
        // anything — delete it once your own reads are in place.
        await Task.CompletedTask.ConfigureAwait(false);

        yield return new MyRecord { Id = "sample-1", Name = "Placeholder", Value = 42 };
    }
}
