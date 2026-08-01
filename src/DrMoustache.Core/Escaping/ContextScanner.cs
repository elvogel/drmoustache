namespace DrMoustache.Core.Escaping;

/// <summary>Labels every character position of a template with the Markdown
/// construct containing it.</summary>
/// <remarks>A template is not valid Markdown until expanded — <c>{{#items}}|
/// {{name}} |</c> is a paragraph to any parser and only becomes a table row
/// after the section expands. So this is a lexical state machine, not a
/// Markdown parse.</remarks>
public static class ContextScanner
{
    public static MarkdownContext[] Scan(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var result = new MarkdownContext[template.Length];
        if (template.Length == 0) return result;

        var i = 0;
        var fenceChar = '\0';
        var fenceLength = 0;

        // Code-span state is hoisted here (rather than being local to
        // ScanTextLine) because CommonMark allows a code span to cross a
        // line break: `ab\ncd` is one span. It is reset by InterruptsParagraph
        // below whenever the line about to be scanned interrupts the
        // paragraph the span started in.
        var codeSpanTicks = 0;

        while (i < template.Length)
        {
            var lineEnd = FindLineEnd(template, i);
            var line = template.AsSpan(i, lineEnd - i);
            MarkdownContext lineBreakContext;

            if (fenceLength > 0)
            {
                if (IsClosingFence(line, fenceChar, fenceLength))
                {
                    Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                    fenceChar = '\0';
                    fenceLength = 0;
                }
                else
                {
                    Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                }

                lineBreakContext = MarkdownContext.FencedCode;
            }
            else if (TryReadFenceOpen(line, out var openChar, out var openLength))
            {
                if (InterruptsParagraph(entersFence: true, isBlank: false, isTableRow: false))
                    codeSpanTicks = 0;

                Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                fenceChar = openChar;
                fenceLength = openLength;
                lineBreakContext = MarkdownContext.FencedCode;
            }
            else
            {
                var isTableRow = LooksLikeTableRow(template, i, lineEnd);

                if (InterruptsParagraph(entersFence: false, isBlank: IsBlankLine(line), isTableRow))
                    codeSpanTicks = 0;

                ScanTextLine(template, i, lineEnd, result, ref codeSpanTicks, isTableRow);
                lineBreakContext = isTableRow ? MarkdownContext.TableRow : MarkdownContext.Inline;
            }

            var lineBreakStart = lineEnd;
            i = SkipLineBreak(template, lineEnd);
            Fill(result, lineBreakStart, i, lineBreakContext);
        }

        return result;
    }

    /// <summary>A code span cannot cross a construct that interrupts the
    /// paragraph containing it. Blank line, fence open, and table row are the
    /// three such constructs this scanner distinguishes as their own
    /// context — a leaked <see cref="MarkdownContext.CodeSpan"/> only causes
    /// damage where it overwrites a context the scanner would otherwise
    /// assign correctly, which is exactly <see cref="MarkdownContext.FencedCode"/>
    /// and <see cref="MarkdownContext.TableRow"/>. Headings, blockquotes, and
    /// list items also interrupt a paragraph in CommonMark, but this scanner
    /// gives them no context of their own — a leak onto one of those lines
    /// still lands on LineStart/Inline, the bounded one-paragraph damage the
    /// blank-line case already accepts, so they are deliberately excluded
    /// here.</summary>
    private static bool InterruptsParagraph(bool entersFence, bool isBlank, bool isTableRow)
        => entersFence || isBlank || isTableRow;

    private static bool IsBlankLine(ReadOnlySpan<char> line)
    {
        foreach (var c in line)
            if (!char.IsWhiteSpace(c)) return false;
        return true;
    }

    private static void ScanTextLine(
        string template, int start, int end, MarkdownContext[] result,
        ref int codeSpanTicks, bool isTableRow)
    {
        var baseContext = isTableRow ? MarkdownContext.TableRow : MarkdownContext.Inline;

        var i = start;

        // inLinkTarget is intentionally NOT hoisted across lines like
        // codeSpanTicks is: a link destination spanning a line break is
        // vanishingly rare, and resetting it per line keeps an unterminated
        // destination from leaking into the rest of the document. This is a
        // deliberate choice, not an oversight.
        var inLinkTarget = false;

        while (i < end)
        {
            var c = template[i];

            if (codeSpanTicks > 0)
            {
                if (c == '`')
                {
                    var run = CountRun(template, i, '`', end);
                    if (run == codeSpanTicks)
                    {
                        Fill(result, i, i + run, MarkdownContext.CodeSpan);
                        codeSpanTicks = 0;
                        i += run;
                        continue;
                    }
                }

                result[i] = MarkdownContext.CodeSpan;
                i++;
                continue;
            }

            if (c == '`')
            {
                var run = CountRun(template, i, '`', end);
                Fill(result, i, i + run, MarkdownContext.CodeSpan);
                codeSpanTicks = run;
                i += run;
                continue;
            }

            if (inLinkTarget)
            {
                result[i] = MarkdownContext.LinkTarget;
                if (c == ')') inLinkTarget = false;
                i++;
                continue;
            }

            if (c == '(' && i > start && template[i - 1] == ']')
            {
                inLinkTarget = true;
                result[i] = MarkdownContext.LinkTarget;
                i++;
                continue;
            }

            result[i] = !isTableRow && i - start < 4
                ? MarkdownContext.LineStart
                : baseContext;

            i++;
        }
    }

    /// <summary>A line is a table row when its first meaningful character is a
    /// pipe. Leading section tags are transparent, so
    /// <c>{{#items}}| a | b |</c> counts.</summary>
    private static bool LooksLikeTableRow(string template, int start, int end)
    {
        var i = start;

        while (i < end)
        {
            if (char.IsWhiteSpace(template[i])) { i++; continue; }

            if (TryMeasureSectionTag(template, i, end, out var length))
            {
                i += length;
                continue;
            }

            return template[i] == '|';
        }

        return false;
    }

    /// <summary>Measures a section-open, inverted-section, or section-close tag
    /// at <paramref name="index"/>. Interpolation tags do not count — only
    /// block tags are transparent for table detection.</summary>
    private static bool TryMeasureSectionTag(
        string template, int index, int end, out int length)
    {
        length = 0;

        if (index + 3 >= end) return false;
        if (template[index] != '{' || template[index + 1] != '{') return false;
        if (template[index + 2] is not ('#' or '^' or '/')) return false;

        var close = template.IndexOf("}}", index + 3, StringComparison.Ordinal);
        if (close < 0 || close + 2 > end) return false;

        length = close + 2 - index;
        return true;
    }

    private static bool TryReadFenceOpen(
        ReadOnlySpan<char> line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;

        var i = 0;
        while (i < line.Length && line[i] == ' ' && i < 4) i++;
        if (i >= line.Length) return false;

        var c = line[i];
        if (c is not ('`' or '~')) return false;

        var run = 0;
        while (i + run < line.Length && line[i + run] == c) run++;
        if (run < 3) return false;

        fenceChar = c;
        fenceLength = run;
        return true;
    }

    private static bool IsClosingFence(
        ReadOnlySpan<char> line, char fenceChar, int openLength)
    {
        var i = 0;
        while (i < line.Length && line[i] == ' ' && i < 4) i++;

        var run = 0;
        while (i + run < line.Length && line[i + run] == fenceChar) run++;
        if (run < openLength) return false;

        for (var j = i + run; j < line.Length; j++)
            if (!char.IsWhiteSpace(line[j])) return false;

        return true;
    }

    private static int CountRun(string s, int index, char c, int end)
    {
        var run = 0;
        while (index + run < end && s[index + run] == c) run++;
        return run;
    }

    private static int FindLineEnd(string s, int from)
    {
        var i = s.IndexOf('\n', from);
        if (i < 0) return s.Length;
        return i > from && s[i - 1] == '\r' ? i - 1 : i;
    }

    private static int SkipLineBreak(string s, int lineEnd)
    {
        var i = lineEnd;
        if (i < s.Length && s[i] == '\r') i++;
        if (i < s.Length && s[i] == '\n') i++;
        return i == lineEnd ? s.Length : i;
    }

    private static void Fill(
        MarkdownContext[] result, int start, int end, MarkdownContext context)
    {
        for (var i = start; i < end && i < result.Length; i++)
            result[i] = context;
    }
}
