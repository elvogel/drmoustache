namespace DrMoustache.Core.Template;

/// <summary>A static fault in a template. Raised before any provider is read,
/// because it affects every record.</summary>
public sealed class TemplateException : Exception
{
    public TemplateException() { }

    public TemplateException(string message) : base(message) { }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException) { }

    public TemplateException(string message, int line) : base(message) => Line = line;

    /// <summary>One-based line number, or 0 when not known.</summary>
    public int Line { get; }
}
