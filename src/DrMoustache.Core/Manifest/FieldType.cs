#pragma warning disable CA1720 // Identifiers should not contain type names; these are part of the public contract

namespace DrMoustache.Core.Manifest;

/// <summary>Escaping, validation, and helper arity all key off this.</summary>
public enum FieldType
{
    /// <summary>The only type that can produce Markdown-significant output.</summary>
    String,
    Integer,
    Decimal,
    Boolean,
    Temporal,
    Guid,
    /// <summary>A nested shape. Cannot be interpolated directly.</summary>
    Object,
    /// <summary>A sequence. Usable only as a section.</summary>
    Collection,
}
