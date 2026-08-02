namespace DrMoustache.Core.Manifest;

/// <summary>A field in a record manifest.</summary>
/// <param name="Path">The camelCase segment name, not the full dotted path.</param>
/// <param name="Type">The field's type classification.</param>
/// <param name="IsNullable">Whether the field value can be null.</param>
/// <param name="Description">Optional documentation for this field.</param>
/// <param name="Children">Members of an <see cref="FieldType.Object"/> or
/// <see cref="FieldType.Collection"/>; empty otherwise.</param>
public sealed record ManifestField(
    string Path,
    FieldType Type,
    bool IsNullable,
    string? Description,
    IReadOnlyList<ManifestField> Children);
