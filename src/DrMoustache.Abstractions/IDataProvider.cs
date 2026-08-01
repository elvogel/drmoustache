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

    IAsyncEnumerable<object> ReadAsync(ProviderContext ctx, CancellationToken ct);
}

/// <summary>Base class for providers. Derive from this, not from
/// <see cref="IDataProvider"/> directly.</summary>
public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    public abstract string Name { get; }

    public Type RecordType => typeof(TRecord);

    public abstract IAsyncEnumerable<TRecord> ReadAsync(
        ProviderContext ctx, CancellationToken ct);

    async IAsyncEnumerable<object> IDataProvider.ReadAsync(
        ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in ReadAsync(ctx, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return record;
    }
}
