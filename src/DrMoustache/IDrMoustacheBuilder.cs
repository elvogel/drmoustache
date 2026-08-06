namespace DrMoustache;

using DrMoustache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the data sources templates bind to.</summary>
public interface IDrMoustacheBuilder
{
    /// <summary>The underlying collection, for further registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>Registers a provider under the name templates reference
    /// with their <c>source:</c> key.</summary>
    /// <typeparam name="TProvider">Constructed by dependency injection, so it
    /// may take whatever it needs in its constructor.</typeparam>
    /// <param name="name">Must be unique within this builder.</param>
    IDrMoustacheBuilder AddSource<TProvider>(string name)
        where TProvider : class, IDataProvider;
}
