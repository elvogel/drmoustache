namespace DrMoustache.Discovery;

/// <summary>A fault attributable to one template file.</summary>
public sealed class RenderException : Exception
{
    public RenderException() { }

    public RenderException(string message) : base(message) { }

    public RenderException(string message, Exception innerException)
        : base(message, innerException) { }
}
