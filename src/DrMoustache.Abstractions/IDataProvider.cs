namespace DrMoustache.Abstractions;

using System.Runtime.CompilerServices;

/// <summary>Supplies records to the rendering engine.</summary>
/// <remarks>
/// <para>Read-only: implementations must not mutate their underlying source.
/// There is deliberately no write, delete, or acknowledge surface. A plausible
/// early use is browsing a dead-letter queue, where a destructive read is
/// unrecoverable.</para>
/// <para>Providers are dependency-injected services. Declare what you need —
/// configuration, options, logging, a client, a database context — in your
/// constructor.</para>
/// </remarks>
public interface IDataProvider
{
    /// <summary>Matched against a template's <c>source:</c> setting, and the
    /// name this provider was registered under.</summary>
    string Name { get; }

    /// <summary>The <see cref="DrMoustacheRecordAttribute"/>-annotated type
    /// this provider yields.</summary>
    Type RecordType { get; }

    /// <summary>Reads every record. May stream; the host materializes.</summary>
    /// <param name="ct">Cancels the read.</param>
    IAsyncEnumerable<object> ReadAsync(CancellationToken ct);
}

/// <summary>Base class for providers. Derive from this, not from
/// <see cref="IDataProvider"/> directly.</summary>
/// <typeparam name="TRecord">The annotated record type this provider yields.</typeparam>
public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public Type RecordType => typeof(TRecord);

    /// <summary>Reads every record. May stream; the host materializes.</summary>
    /// <param name="ct">Cancels the read.</param>
    public abstract IAsyncEnumerable<TRecord> ReadAsync(CancellationToken ct);

    async IAsyncEnumerable<object> IDataProvider.ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in ReadAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            yield return record;
    }
}
