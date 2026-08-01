namespace DrMoustache.Abstractions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>Everything a provider is given when asked to read.</summary>
public sealed class ProviderContext
{
    /// <summary>The full configuration chain.</summary>
    /// <remarks>A provider can read every setting the host can, including
    /// secrets belonging to unrelated sources. Narrow it per source with
    /// <c>scopeConfig: true</c>.</remarks>
    public required IConfiguration Configuration { get; init; }

    /// <summary>This source's own configuration section, always scoped
    /// regardless of the <c>scopeConfig</c> setting.</summary>
    public required IConfigurationSection Source { get; init; }

    public required ILogger Logger { get; init; }
}
