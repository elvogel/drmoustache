namespace DrMoustache.Output;

/// <summary>A document could not be written.</summary>
public sealed class OutputException : Exception
{
    public OutputException() { }

    public OutputException(string message) : base(message) { }

    public OutputException(string message, Exception innerException)
        : base(message, innerException) { }
}
