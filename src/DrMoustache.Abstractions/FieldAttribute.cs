namespace DrMoustache.Abstractions;

/// <summary>Exposes a property to templates.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    public FieldAttribute() { }

    /// <param name="description">Shown in validation errors when a template
    /// references a near-miss field name.</param>
    public FieldAttribute(string description) => Description = description;

    public string? Description { get; }
}
