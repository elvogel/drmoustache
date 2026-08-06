namespace DrMoustache;

using DrMoustache.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Try, not Add: calling AddDrMoustache twice — which is exactly how a
        // duplicate source name gets past a builder-local guard — otherwise
        // leaves two renderer registrations behind.
        services.TryAddSingleton<Rendering.IDocumentRenderer, Rendering.DocumentRenderer>();

        return new DrMoustacheBuilder(services);
    }

    private sealed class DrMoustacheBuilder(IServiceCollection services) : IDrMoustacheBuilder
    {
        public IServiceCollection Services { get; } = services;

        public IDrMoustacheBuilder AddSource<TProvider>(string name)
            where TProvider : class, IDataProvider
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            // The service collection, not this builder, is the scope the
            // uniqueness rule lives in. A builder-local set misses a second
            // AddDrMoustache call on the same collection, and there the loser
            // becomes silently unreachable while the "available sources"
            // diagnostic lists the name twice.
            if (IsAlreadyRegistered(name))
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

        private bool IsAlreadyRegistered(string name)
            => Services.Any(d => d.ServiceType == typeof(SourceRegistration)
                && d.ImplementationInstance is SourceRegistration registration
                && string.Equals(registration.Name, name, StringComparison.Ordinal));
    }
}
