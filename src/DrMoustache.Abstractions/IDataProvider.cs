namespace DrMoustache.Abstractions;

using System.Runtime.CompilerServices;

/// <summary>Supplies records to the rendering engine.</summary>
/// <remarks>Read-only: implementations must not mutate their underlying
/// source. There is deliberately no write, delete, or acknowledge surface.</remarks>
public interface IDataProvider
{
    /// <summary>Matched against a template's <c>source:</c> setting.</summary>
    string Name { get; }

    /// <summary>The <see cref="DrMoustacheRecordAttribute"/>-annotated type
    /// this provider yields.</summary>
    Type RecordType { get; }

    /// <summary>Yields the provider's records, one at a time.</summary>
    /// <param name="ctx">Configuration and logging for this source. Never
    /// <see langword="null"/>.</param>
    /// <param name="ct">Cancels the read. Implementations should honour it
    /// between records and pass it to any I/O they perform.</param>
    /// <returns>A lazily-produced sequence of records, each an instance of
    /// <see cref="RecordType"/>. Returning an
    /// <see cref="IAsyncEnumerable{T}"/> rather than a materialised collection
    /// is deliberate: a source with 50,000 records must not have to fit in
    /// memory before the first document renders.</returns>
    /// <remarks>This is a read. Implementations must not delete, acknowledge,
    /// or otherwise mutate the underlying source, and must not assume the
    /// sequence will be enumerated to the end.</remarks>
    IAsyncEnumerable<object> ReadAsync(ProviderContext ctx, CancellationToken ct);
}

/// <summary>Base class for providers. Derive from this, not from
/// <see cref="IDataProvider"/> directly: it supplies
/// <see cref="RecordType"/> and the bridge the loader needs from the typed
/// sequence to the untyped one.</summary>
/// <typeparam name="TRecord">The record type this provider yields, annotated
/// with <see cref="DrMoustacheRecordAttribute"/> and with each template-visible
/// member marked <see cref="FieldAttribute"/>.</typeparam>
public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    /// <summary>The name a template's <c>source:</c> setting matches against.
    /// Name it for the business concept, never for the transport.</summary>
    public abstract string Name { get; }

    /// <summary>Always <typeparamref name="TRecord"/>.</summary>
    public Type RecordType => typeof(TRecord);

    /// <summary>Yields the provider's records, one at a time.</summary>
    /// <param name="ctx">Configuration and logging for this source. Never
    /// <see langword="null"/>.</param>
    /// <param name="ct">Cancels the read. Honour it between records and pass
    /// it to any I/O performed.</param>
    /// <returns>A lazily-produced sequence of records.</returns>
    /// <remarks>This is a read. Do not delete, acknowledge, or otherwise
    /// mutate the underlying source.</remarks>
    public abstract IAsyncEnumerable<TRecord> ReadAsync(
        ProviderContext ctx, CancellationToken ct);

    /// <summary>Bridges the typed sequence to the untyped one the plugin loader
    /// activates against. Implemented explicitly so it does not clutter the
    /// surface a plugin author writes to.</summary>
    /// <param name="ctx">Passed straight through to
    /// <see cref="ReadAsync(ProviderContext, CancellationToken)"/>.</param>
    /// <param name="ct">Passed straight through, and also applied to the
    /// enumeration itself.</param>
    /// <returns>The same records, boxed as <see cref="object"/>.</returns>
    async IAsyncEnumerable<object> IDataProvider.ReadAsync(
        ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in ReadAsync(ctx, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return record;
    }
}
