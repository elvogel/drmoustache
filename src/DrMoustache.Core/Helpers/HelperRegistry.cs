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

    /// <param name="name">The helper name, e.g. <c>"upper"</c>.</param>
    /// <param name="value">The value passed through the helper.</param>
    /// <param name="argument">The helper's string argument, e.g. a format string.</param>
    /// <param name="line">The source line, used for diagnostics.</param>
    /// <param name="culture">The culture used to format numbers and dates.
    /// <see langword="null"/> (the default) uses
    /// <see cref="CultureInfo.InvariantCulture"/>, matching
    /// <see cref="Rendering.TemplateRenderer.Render"/>'s default so a
    /// template's own interpolations and its helper output agree.</param>
#pragma warning disable CA1062 // Validate parameters is non-null — intentionally accepts null values for all helpers except 'default'
    public static string Apply(
        string name, object? value, string? argument, int line, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var effectiveCulture = culture ?? CultureInfo.InvariantCulture;

        return name switch
        {
            "date" => Date(value, argument, line, effectiveCulture),
            "format" => Format(value, argument, line, effectiveCulture),
            "upper" => ValueText.Format(value, effectiveCulture).ToUpperInvariant(),
#pragma warning disable CA1308 // Use ToUpperInvariant — intentionally using ToLowerInvariant for 'lower' helper semantics
            "lower" => ValueText.Format(value, effectiveCulture).ToLowerInvariant(),
#pragma warning restore CA1308
            "join" => Join(value, argument, line, effectiveCulture),
            "default" => Default(value, argument, effectiveCulture),
            _ => throw new TemplateException(
                $"unknown helper '{name}' (line {line})\n"
                + $"       available: {string.Join(", ", Names)}", line),
        };
    }
#pragma warning restore CA1062

    private static string Date(object? value, string? format, int line, CultureInfo culture) => value switch
    {
        null => "",
        DateTimeOffset d => d.ToString(format ?? "O", culture),
        DateTime d => d.ToString(format ?? "O", culture),
        DateOnly d => d.ToString(format ?? "O", culture),
        TimeOnly t => t.ToString(format ?? "O", culture),
        _ => throw new TemplateException(
            $"'date' needs a temporal value but got "
            + $"{value.GetType().Name} (line {line})", line),
    };

    private static string Format(object? value, string? format, int line, CultureInfo culture)
    {
        if (value is null) return "";

        if (value is DateTime or DateTimeOffset or DateOnly or TimeOnly)
            throw new TemplateException(
                $"'format' does not format temporal values — use 'date' instead (line {line})", line);

        if (value is not string && value is IFormattable formattable)
            return formattable.ToString(format, culture);

        throw new TemplateException(
            $"'format' needs a numeric value but got "
            + $"{value.GetType().Name} (line {line})", line);
    }

    private static string Join(object? value, string? separator, int line, CultureInfo culture)
    {
        if (value is null) return "";

        var sequence = ValueResolver.AsSequence(value)
            ?? throw new TemplateException(
                $"'join' needs a collection but got "
                + $"{value.GetType().Name} (line {line})", line);

        return string.Join(separator ?? ", ", sequence.Select(v => ValueText.Format(v, culture)));
    }

    private static string Default(object? value, string? fallback, CultureInfo culture)
    {
        var text = ValueText.Format(value, culture);
        return text.Length == 0 ? fallback ?? "" : text;
    }
}
