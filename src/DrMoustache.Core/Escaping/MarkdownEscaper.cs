namespace DrMoustache.Core.Escaping;

using System.Buffers;
using System.Text;

/// <summary>Escapes an interpolated value for the Markdown construct
/// containing it.</summary>
/// <remarks>HTML entities are never escaped. Output is Markdown; the
/// downstream renderer handles HTML.</remarks>
public static class MarkdownEscaper
{
    /// <summary><c>\</c> is a special in its own right, and must be listed
    /// first among equals: without it, a value of <c>\|pipe</c> escapes to
    /// <c>\\|pipe</c>, whose leading pair CommonMark consumes as one literal
    /// backslash — leaving the <c>|</c> live to shatter a table row. With it
    /// the value emits <c>\\\|pipe</c> and the pipe stays inert.</summary>
    private static readonly SearchValues<char> InlineSpecials =
        SearchValues.Create(@"*_[]`|\");

    /// <summary>The fast-path probe. A value containing none of these needs no
    /// work at all in any of the escaping paths, including a table cell (whose
    /// only extra job is newline → <c>&lt;br&gt;</c>) and a multi-line value
    /// (whose only extra job is re-applying block rules after each
    /// newline).</summary>
    private static readonly SearchValues<char> InlineSpecialsOrNewline =
        SearchValues.Create("*_[]`|\\\r\n");

    /// <summary>Characters that open a block-level construct when they are the
    /// first character of a line: ATX heading, blockquote, the three bullet
    /// markers, a tilde fence, and a setext underline.</summary>
    private const string BlockMarkers = "#>-*+~=";

    public static string Escape(string value, MarkdownContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return value;

        return context switch
        {
            MarkdownContext.FencedCode or MarkdownContext.CodeSpan => value,
            MarkdownContext.TableRow => EscapeTableCell(value),
            MarkdownContext.LineStart => EscapeBlockAware(value, blockRulesOnFirstLine: true),
            MarkdownContext.LinkTarget => EscapeLinkTarget(value),
            _ => EscapeBlockAware(value, blockRulesOnFirstLine: false),
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
    /// emphasis, links, or code spans any more than it can outside a table.
    /// Block markers (<c>#</c>, <c>&gt;</c>, <c>-</c>) still pass through: a
    /// table cell is inline context, so a leading '#' there cannot become a
    /// heading — and because every newline becomes <c>&lt;br&gt;</c>, no part
    /// of the value can ever reach column 0 either.</summary>
    private static string EscapeTableCell(string value)
    {
        if (value.AsSpan().IndexOfAny(InlineSpecialsOrNewline) < 0) return value;

        var sb = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c is '\r' or '\n')
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

    /// <summary>Escapes a value that will be laid down as ordinary document
    /// text, line by line.
    ///
    /// Two rules compose here. Every line gets the inline specials escaped —
    /// including the first, which is why a value landing at column 0 is no
    /// less protected against emphasis, links, and pipes than the same value
    /// landing mid-line. And every line after the first additionally gets the
    /// block-marker rules, because the newline the value itself carried put
    /// the following character at column 0: without this, a value of
    /// <c>ok\n# Pwned</c> interpolated mid-paragraph introduces a live
    /// heading, and one carrying a pipe row introduces a live table.
    ///
    /// <paramref name="blockRulesOnFirstLine"/> distinguishes the two
    /// contexts: <see cref="MarkdownContext.LineStart"/> means the value's own
    /// first character is already at column 0, so line one gets the block
    /// rules too.</summary>
    private static string EscapeBlockAware(string value, bool blockRulesOnFirstLine)
    {
        if (value.AsSpan().IndexOfAny(InlineSpecialsOrNewline) < 0
            && !(blockRulesOnFirstLine && NeedsBlockEscape(value)))
            return value;

        var sb = new StringBuilder(value.Length + 8);
        var applyBlockRules = blockRulesOnFirstLine;
        var start = 0;

        while (start <= value.Length)
        {
            var breakAt = value.IndexOf('\n', start);
            var lineEnd = breakAt < 0 ? value.Length : breakAt;

            AppendLine(sb, value.AsSpan(start, lineEnd - start), applyBlockRules);

            if (breakAt < 0) break;

            sb.Append('\n');
            start = breakAt + 1;
            applyBlockRules = true;
        }

        return sb.ToString();
    }

    /// <summary>Whether a single-line value with no inline specials would still
    /// need work because it opens a block construct.</summary>
    private static bool NeedsBlockEscape(ReadOnlySpan<char> line)
        => BlockRuleLength(line) > 0;

    /// <summary>How many characters at the head of <paramref name="line"/> the
    /// block rules consume, or 0 when the line opens no block construct. The
    /// caller emits those characters itself, already escaped, so the inline
    /// pass does not double-escape a backslash it just wrote.</summary>
    private static int BlockRuleLength(ReadOnlySpan<char> line)
    {
        if (line.Length == 0) return 0;

        // A leading four-space indent would become a code block.
        if (line.StartsWith("    ", StringComparison.Ordinal)) return 1;

        if (BlockMarkers.Contains(line[0], StringComparison.Ordinal)) return 1;

        // An ordered-list marker: digits, then '.' or ')', then whitespace.
        // CommonMark requires that whitespace, so '1234.5' was never a list
        // and must not collect a spurious backslash.
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;

        if (digits == 0 || digits >= line.Length) return 0;
        if (line[digits] is not ('.' or ')')) return 0;
        if (digits + 1 < line.Length && !char.IsWhiteSpace(line[digits + 1])) return 0;

        return digits + 1;
    }

    private static void AppendLine(
        StringBuilder sb, ReadOnlySpan<char> line, bool applyBlockRules)
    {
        var i = 0;

        if (applyBlockRules)
        {
            var consumed = BlockRuleLength(line);

            if (consumed == 1 && line.StartsWith("    ", StringComparison.Ordinal))
            {
                // Replace the first space with its numeric entity: renders as a
                // space, but breaks the four-character indent run.
                sb.Append("&#32;");
                i = 1;
            }
            else if (consumed > 0)
            {
                // Everything before the marker is digits, which need no
                // escaping; the marker itself is emitted already escaped.
                sb.Append(line[..(consumed - 1)]).Append('\\').Append(line[consumed - 1]);
                i = consumed;
            }
        }

        for (; i < line.Length; i++)
        {
            if (InlineSpecials.Contains(line[i])) sb.Append('\\');
            sb.Append(line[i]);
        }
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
}
