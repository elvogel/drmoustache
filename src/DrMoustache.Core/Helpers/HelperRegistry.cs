namespace DrMoustache.Core.Helpers;

using System.Globalization;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Template;

/// <summary>The closed set of value transforms callable from a template.</summary>
/// <remarks>There is no registration API. A value that needs computing should
/// be computed in the provider, which is why providers exist.</remarks>
public static class HelperRegistry
{
    private static readonly string[] Names =
        ["date", "format", "upper", "lower", "join", "default"];

    public static bool IsKnown(string name) =>
        Array.IndexOf(Names, name) >= 0;

#pragma warning disable CA1062 // Validate parameters is non-null — intentionally accepts null values for all helpers except 'default'
    public static string Apply(string name, object? value, string? argument, int line)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name switch
        {
            "date" => Date(value, argument, line),
            "format" => Format(value, argument, line),
            "upper" => Stringify(value).ToUpperInvariant(),
#pragma warning disable CA1308 // Use ToUpperInvariant — intentionally using ToLowerInvariant for 'lower' helper semantics
            "lower" => Stringify(value).ToLowerInvariant(),
#pragma warning restore CA1308
            "join" => Join(value, argument, line),
            "default" => Default(value, argument),
            _ => throw new TemplateException(
                $"unknown helper '{name}' (line {line})\n"
                + $"       available: {string.Join(", ", Names)}", line),
        };
    }
#pragma warning restore CA1062

    private static string Date(object? value, string? format, int line) => value switch
    {
        null => "",
        DateTimeOffset d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        DateTime d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        DateOnly d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        TimeOnly t => t.ToString(format ?? "O", CultureInfo.CurrentCulture),
        _ => throw new TemplateException(
            $"'date' needs a temporal value but got "
            + $"{value.GetType().Name} (line {line})", line),
    };

    private static string Format(object? value, string? format, int line)
    {
        if (value is null) return "";

        if (value is DateTime or DateTimeOffset or DateOnly or TimeOnly)
            throw new TemplateException(
                $"'format' does not format temporal values — use 'date' instead (line {line})", line);

        if (value is not string && value is IFormattable formattable)
            return formattable.ToString(format, CultureInfo.CurrentCulture);

        throw new TemplateException(
            $"'format' needs a numeric value but got "
            + $"{value.GetType().Name} (line {line})", line);
    }

    private static string Join(object? value, string? separator, int line)
    {
        if (value is null) return "";

        var sequence = ValueResolver.AsSequence(value)
            ?? throw new TemplateException(
                $"'join' needs a collection but got "
                + $"{value.GetType().Name} (line {line})", line);

        return string.Join(separator ?? ", ", sequence.Select(Stringify));
    }

    private static string Default(object? value, string? fallback)
    {
        var text = Stringify(value);
        return text.Length == 0 ? fallback ?? "" : text;
    }

    private static string Stringify(object? value) => value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };
}
