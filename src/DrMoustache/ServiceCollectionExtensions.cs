namespace DrMoustache;

using DrMoustache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Adds DrMoustache to a service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the renderer and returns a builder for adding sources.</summary>
    public static IDrMoustacheBuilder AddDrMoustache(
        this IServiceCollection services,
        Action<DrMoustacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null) services.AddOptions<DrMoustacheOptions>();
        else services.AddOptions<DrMoustacheOptions>().Configure(configure);

        services.AddSingleton<Rendering.IDocumentRenderer, Rendering.DocumentRenderer>();

        return new DrMoustacheBuilder(services);
    }

    private sealed class DrMoustacheBuilder(IServiceCollection services) : IDrMoustacheBuilder
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public IServiceCollection Services { get; } = services;

        public IDrMoustacheBuilder AddSource<TProvider>(string name)
            where TProvider : class, IDataProvider
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!_names.Add(name))
            {
                throw new InvalidOperationException(
                    $"a source named '{name}' is already registered. "
                    + "Source names must be unique, because a template's "
                    + "'source:' key resolves against exactly one provider.");
            }

            Services.AddKeyedSingleton<IDataProvider, TProvider>(name);
            Services.AddSingleton(new SourceRegistration(name, typeof(TProvider)));

            return this;
        }
    }
}
