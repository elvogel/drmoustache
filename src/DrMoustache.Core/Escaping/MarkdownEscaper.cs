namespace DrMoustache.Core.Escaping;

using System.Buffers;
using System.Text;

/// <summary>Escapes an interpolated value for the Markdown construct
/// containing it.</summary>
/// <remarks>HTML entities are never escaped. Output is Markdown; the
/// downstream renderer handles HTML.</remarks>
public static class MarkdownEscaper
{
    private static readonly SearchValues<char> InlineSpecials =
        SearchValues.Create("*_[]`|");

    public static string Escape(string value, MarkdownContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return value;

        return context switch
        {
            MarkdownContext.FencedCode or MarkdownContext.CodeSpan => value,
            MarkdownContext.TableRow => EscapeTableCell(value),
            MarkdownContext.LineStart => EscapeLineStart(value),
            MarkdownContext.LinkTarget => EscapeLinkTarget(value),
            _ => EscapeInline(value),
        };
    }

    /// <summary>The backtick run needed to wrap <paramref name="value"/> as a
    /// code span without the value's own backticks closing it early.</summary>
    public static string FenceFor(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var longest = 0;
        var run = 0;

        foreach (var c in value)
        {
            if (c == '`') { run++; longest = Math.Max(longest, run); }
            else run = 0;
        }

        return new string('`', longest + 1);
    }

    /// <summary>A table cell is inline context plus newline handling: markdown
    /// trumps Mustache, so interpolated data must not be able to introduce
    /// emphasis, links, or code spans any more than it can outside a table --
    /// hence this shares <see cref="EscapeInlineSpecials"/>'s escaping loop
    /// rather than leaving <c>*</c>/<c>_</c> alone. Block markers (<c>#</c>,
    /// <c>&gt;</c>, <c>-</c>) still pass through: a table cell is inline
    /// context, so a leading '#' there cannot become a heading.</summary>
    private static string EscapeTableCell(string value) =>
        EscapeInlineSpecials(value, convertNewlinesToBreaks: true);

    private static string EscapeLineStart(string value)
    {
        // A leading four-space indent would become a code block. Replace the
        // first space with its numeric entity, which renders as a space but
        // breaks the indent run.
        if (value.StartsWith("    ", StringComparison.Ordinal))
            return "&#32;" + value[1..];

        var first = value[0];

        if (first is '#' or '>' or '-' or '*' or '+')
            return @"\" + value;

        // An ordered-list marker: digits followed by '.' or ')'.
        var digits = 0;
        while (digits < value.Length && char.IsAsciiDigit(value[digits])) digits++;

        if (digits > 0 && digits < value.Length && value[digits] is '.' or ')')
            return string.Concat(value.AsSpan(0, digits), @"\", value.AsSpan(digits));

        return value;
    }

    private static string EscapeLinkTarget(string value)
    {
        var sb = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            switch (c)
            {
                case ' ': sb.Append("%20"); break;
                case '(': sb.Append("%28"); break;
                case ')': sb.Append("%29"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeInline(string value) =>
        EscapeInlineSpecials(value, convertNewlinesToBreaks: false);

    /// <summary>Backslash-escapes the inline specials shared by
    /// <see cref="EscapeInline"/> and <see cref="EscapeTableCell"/>.
    /// <paramref name="convertNewlinesToBreaks"/> additionally turns
    /// <c>\r\n</c>/<c>\n</c> into <c>&lt;br&gt;</c>, which only applies inside
    /// a table cell -- a newline elsewhere is left as an ordinary line
    /// break.</summary>
    private static string EscapeInlineSpecials(string value, bool convertNewlinesToBreaks)
    {
        if (!convertNewlinesToBreaks && value.AsSpan().IndexOfAny(InlineSpecials) < 0) return value;

        var sb = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (convertNewlinesToBreaks && c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n') i++;
                sb.Append("<br>");
                continue;
            }

            if (InlineSpecials.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }
}
