namespace DrMoustache.Core.Rendering;

using System.Globalization;
using System.Text;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Escaping;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Template;

/// <summary>Binds a node tree to a value and escapes each interpolation
/// according to the Markdown context captured at lex time.</summary>
public static class TemplateRenderer
{
    /// <summary>Refers to the current scope itself, for use inside a section
    /// over scalars.</summary>
    private const string SelfPath = ".";

    /// <param name="nodes">The lexed node tree to render.</param>
    /// <param name="context">The root data bound to the template.</param>
    /// <param name="culture">The culture used to format numbers, dates, and
    /// other <see cref="IFormattable"/> values, including inside helpers.
    /// <see langword="null"/> (the default) renders with
    /// <see cref="CultureInfo.InvariantCulture"/>, so the same template and
    /// data produce byte-identical output regardless of the host's ambient
    /// culture. Pass an explicit culture to opt into localised output.</param>
    public static string Render(
        IReadOnlyList<TemplateNode> nodes, object? context, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var effectiveCulture = culture ?? CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var scopes = new List<object?> { context };
        Write(nodes, scopes, sb, effectiveCulture);
        return sb.ToString();
    }

    private static void Write(
        IReadOnlyList<TemplateNode> nodes, List<object?> scopes, StringBuilder sb,
        CultureInfo culture)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    sb.Append(text.Text);
                    break;

                case RawNode raw:
                    sb.Append(ValueText.Format(Lookup(scopes, raw.Path), culture));
                    break;

                case InterpolationNode interp:
                    sb.Append(MarkdownEscaper.Escape(
                        ValueText.Format(Lookup(scopes, interp.Path), culture), interp.Context));
                    break;

                case HelperNode helper:
                    sb.Append(MarkdownEscaper.Escape(
                        HelperRegistry.Apply(
                            helper.Helper, Lookup(scopes, helper.Path),
                            helper.Argument, helper.Line, culture),
                        helper.Context));
                    break;

                case SectionNode section:
                    WriteSection(section, scopes, sb, culture);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"unhandled node type {node.GetType().Name}");
            }
        }
    }

    private static void WriteSection(
        SectionNode section, List<object?> scopes, StringBuilder sb, CultureInfo culture)
    {
        var value = Lookup(scopes, section.Path);
        var truthy = ValueResolver.IsTruthy(value);

        if (section.Inverted)
        {
            if (!truthy) Write(section.Children, scopes, sb, culture);
            return;
        }

        if (!truthy) return;

        if (ValueResolver.AsSequence(value) is { } sequence)
        {
            foreach (var item in sequence)
            {
                scopes.Add(item);
                Write(section.Children, scopes, sb, culture);
                scopes.RemoveAt(scopes.Count - 1);
            }

            return;
        }

        scopes.Add(value);
        Write(section.Children, scopes, sb, culture);
        scopes.RemoveAt(scopes.Count - 1);
    }

    /// <summary>Walks scopes innermost-first, so a child scope shadows its
    /// parent but the parent stays reachable.</summary>
    private static object? Lookup(List<object?> scopes, string path)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            if (string.Equals(path, SelfPath, StringComparison.Ordinal))
                return scopes[i];

            if (ValueResolver.TryResolve(scopes[i], path, out var value))
                return value;
        }

        return null;
    }
}
