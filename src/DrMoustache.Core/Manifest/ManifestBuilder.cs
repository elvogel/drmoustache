namespace DrMoustache.Core.Manifest;

using System.Collections;
using System.Reflection;
using DrMoustache.Abstractions;

/// <summary>Reflects an attributed record type into a <see cref="RecordManifest"/>.
/// Runs once per provider load, not per record.</summary>
public static class ManifestBuilder
{
    private const int MaxDepth = 16;

    public static RecordManifest FromType<T>() => FromType(typeof(T));

    public static RecordManifest FromType(Type recordType)
    {
        ArgumentNullException.ThrowIfNull(recordType);

        var attr = recordType.GetCustomAttribute<DrMoustacheRecordAttribute>()
            ?? throw new ManifestException(
                $"'{recordType.Name}' is not annotated with [DrMoustacheRecord]. "
                + "Add [DrMoustacheRecord(\"some-name\")] to the type.");

        return new RecordManifest(attr.Name, BuildFields(recordType, depth: 0));
    }

    // CA1859: Return type is intentionally IReadOnlyList<T> to prevent accidental mutation of children lists
#pragma warning disable CA1859
    private static IReadOnlyList<ManifestField> BuildFields(Type type, int depth)
#pragma warning restore CA1859
    {
        if (depth >= MaxDepth)
            throw new ManifestException(
                $"Record shape nests deeper than {MaxDepth} levels at '{type.Name}'. "
                + "This usually means a cycle.");

        var nullability = new NullabilityInfoContext();
        var fields = new List<ManifestField>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<FieldAttribute>() is not { } fieldAttr)
                continue;

            var (fieldType, elementType) = Classify(prop.PropertyType);

            var isNullable = nullability.Create(prop).ReadState == NullabilityState.Nullable;

            var children = fieldType switch
            {
                FieldType.Collection => BuildFields(elementType!, depth + 1),
                FieldType.Object => BuildFields(prop.PropertyType, depth + 1),
                _ => [],
            };

            fields.Add(new ManifestField(
                ToCamelCase(prop.Name), fieldType, isNullable, fieldAttr.Description, children));
        }

        return fields;
    }

    private static (FieldType Type, Type? ElementType) Classify(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return (FieldType.String, null);
        if (underlying == typeof(bool)) return (FieldType.Boolean, null);
        if (underlying == typeof(Guid)) return (FieldType.Guid, null);

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly) || underlying == typeof(TimeOnly))
            return (FieldType.Temporal, null);

        if (underlying == typeof(decimal) || underlying == typeof(double)
            || underlying == typeof(float))
            return (FieldType.Decimal, null);

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong))
            return (FieldType.Integer, null);

        if (GetElementType(underlying) is { } element)
            return (FieldType.Collection, element);

        return (FieldType.Object, null);
    }

    private static Type? GetElementType(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return null;

        if (type.IsArray) return type.GetElementType();

        var enumerable = type.GetInterfaces()
            .Concat(type.IsInterface ? [type] : [])
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable?.GetGenericArguments()[0];
    }

    private static string ToCamelCase(string name) =>
        name.Length > 0 && char.IsUpper(name[0])
            ? string.Create(name.Length, name, static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                span[0] = char.ToLowerInvariant(span[0]);
            })
            : name;
}
