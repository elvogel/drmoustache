namespace DrMoustache.Core.Binding;

using System.Globalization;

/// <summary>The single scalar-to-string formatter shared by the renderer and
/// the helper registry, so the same value renders identically whether or not
/// a helper touched it (e.g. a <see cref="bool"/> renders as <c>true</c>/
/// <c>false</c> either way, never <c>True</c>/<c>False</c>).</summary>
internal static class ValueText
{
    /// <param name="value">The value to render.</param>
    /// <param name="culture">The culture used to format <see cref="IFormattable"/>
    /// values such as numbers and dates.</param>
    public static string Format(object? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return value switch
        {
            null => "",
            string s => s,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, culture),
            _ => value.ToString() ?? "",
        };
    }
}
