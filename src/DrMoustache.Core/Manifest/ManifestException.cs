namespace DrMoustache.Core.Manifest;

/// <summary>Raised when a record type cannot be turned into a manifest.</summary>
public sealed class ManifestException : Exception
{
    public ManifestException() { }

    public ManifestException(string message) : base(message) { }

    public ManifestException(string message, Exception innerException)
        : base(message, innerException) { }
}
