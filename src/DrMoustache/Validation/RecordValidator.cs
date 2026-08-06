namespace DrMoustache.Validation;

using DrMoustache.Core.Binding;
using DrMoustache.Core.Manifest;

/// <summary>Checks a record against the shape its own manifest declares.</summary>
/// <remarks>C#'s <c>required</c> is a compile-time guarantee only. A provider
/// that deserializes or reflects can still yield null in a non-nullable field,
/// and the renderer would emit an empty string — a report silently missing data
/// it claims to contain. This catches that before anything is written.</remarks>
internal static class RecordValidator
{
    /// <returns>Null when the record is usable, otherwise the reason.</returns>
    internal static string? Validate(object record, RecordManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(manifest);

        return Check(record, manifest.Fields, prefix: null);
    }

    private static string? Check(
        object? scope, IReadOnlyList<ManifestField> fields, string? prefix)
    {
        foreach (var field in fields)
        {
            var path = prefix is null ? field.Path : $"{prefix}.{field.Path}";

            if (!ValueResolver.TryResolve(scope, field.Path, out var value))
                continue;

            if (value is null)
            {
                if (field.IsNullable) continue;

                var description = field.Description is { } d ? $" ({d})" : "";
                return $"'{path}' was null but the record declares it required{description}";
            }

            if (field.Type is FieldType.Object
                && Check(value, field.Children, path) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
