namespace DrMoustache;

/// <summary>Records that a source name was registered.</summary>
/// <remarks>Keyed dependency injection cannot enumerate its own keys, so this
/// exists to let a failure name the sources that <em>are</em> registered rather
/// than only the one that is not. Deliberate, not redundant.</remarks>
/// <param name="Name">The name a template's <c>source:</c> resolves against.</param>
/// <param name="ProviderType">The provider registered under that name.</param>
public sealed record SourceRegistration(string Name, Type ProviderType);
