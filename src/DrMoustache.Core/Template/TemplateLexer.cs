namespace DrMoustache.Core.Template;

using System.Text;
using DrMoustache.Core.Escaping;

/// <summary>Turns template text into a node tree, capturing each tag's
/// Markdown context as it goes.</summary>
public static class TemplateLexer
{
    public static IReadOnlyList<TemplateNode> Lex(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var contexts = ContextScanner.Scan(body);
        var index = 0;
        var nodes = ReadNodes(body, contexts, ref index, openSection: null, openLine: 0);

        return nodes;
    }

    private static List<TemplateNode> ReadNodes(
        string body, MarkdownContext[] contexts, ref int index,
        string? openSection, int openLine)
    {
        var nodes = new List<TemplateNode>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            nodes.Add(new TextNode(text.ToString()));
            text.Clear();
        }

        while (index < body.Length)
        {
            var open = body.IndexOf("{{", index, StringComparison.Ordinal);

            if (open < 0)
            {
                text.Append(body, index, body.Length - index);
                index = body.Length;
                break;
            }

            text.Append(body, index, open - index);

            var isRaw = open + 2 < body.Length && body[open + 2] == '{';
            var closeToken = isRaw ? "}}}" : "}}";
            var contentStart = open + (isRaw ? 3 : 2);

            var close = body.IndexOf(closeToken, contentStart, StringComparison.Ordinal);
            if (close < 0)
                throw new TemplateException(
                    $"unterminated tag starting at line {LineOf(body, open)}",
                    LineOf(body, open));

            var content = body[contentStart..close].Trim();
            var line = LineOf(body, open);
            var afterTag = close + closeToken.Length;

            if (isRaw)
            {
                FlushText();
                nodes.Add(new RawNode(content, line));
                index = afterTag;
                continue;
            }

            if (content.StartsWith('!'))
            {
                // ConsumeStandaloneLine trims already-buffered leading whitespace
                // off `text` before we flush, so it must run first.
                index = ConsumeStandaloneLine(body, open, afterTag, text);
                FlushText();
                continue;
            }

            if (content.StartsWith('#') || content.StartsWith('^'))
            {
                var inverted = content[0] == '^';
                var path = content[1..].Trim();

                var next = ConsumeStandaloneLine(body, open, afterTag, text);
                FlushText();

                var children = ReadNodes(body, contexts, ref next, path, line);
                nodes.Add(new SectionNode(path, inverted, children, line));
                index = next;
                continue;
            }

            if (content.StartsWith('/'))
            {
                var path = content[1..].Trim();

                if (openSection is null)
                    throw new TemplateException(
                        $"closing tag '{{{{/{path}}}}}' at line {line} has no opening tag", line);

                if (!string.Equals(openSection, path, StringComparison.Ordinal))
                    throw new TemplateException(
                        $"mismatched closing tag at line {line}: "
                        + $"expected '{{{{/{openSection}}}}}', found '{{{{/{path}}}}}'", line);

                index = ConsumeStandaloneLine(body, open, afterTag, text);
                FlushText();
                return nodes;
            }

            FlushText();
            var context = contexts[open];
            var space = content.IndexOf(' ', StringComparison.Ordinal);

            if (space < 0)
            {
                nodes.Add(new InterpolationNode(content, context, line));
            }
            else
            {
                var helper = content[..space];
                var rest = content[(space + 1)..].Trim();
                var (path, argument) = SplitHelperArguments(helper, rest, line);
                nodes.Add(new HelperNode(helper, path, argument, context, line));
            }

            index = afterTag;
        }

        if (openSection is not null)
            throw new TemplateException(
                $"section '{{{{#{openSection}}}}}' opened at line {openLine} was not closed",
                openLine);

        FlushText();
        return nodes;
    }

    /// <summary>Applies the standalone-tag rule. A block tag alone on its line
    /// consumes the whole line — but never inside a table row, where the
    /// trailing newline terminates the row.</summary>
    private static int ConsumeStandaloneLine(
        string body, int tagStart, int afterTag, StringBuilder pendingText)
    {
        // No explicit TableRow check is needed here: a line the scanner
        // classifies as TableRow always contains a literal '|' outside the
        // leading whitespace-and-section-tag run (that's what makes it a
        // table row), and that '|' always falls in either the before-tag or
        // after-tag span below. Either whitespace scan therefore already
        // fails on a table row, so it can never look standalone.

        // Everything before the tag on this line must be whitespace.
        var lineStart = body.LastIndexOf('\n', Math.Max(tagStart - 1, 0)) + 1;
        for (var i = lineStart; i < tagStart; i++)
            if (!char.IsWhiteSpace(body[i])) return afterTag;

        // Everything after the tag up to the newline must be whitespace.
        var i2 = afterTag;
        while (i2 < body.Length && body[i2] != '\n')
        {
            if (!char.IsWhiteSpace(body[i2])) return afterTag;
            i2++;
        }

        // Drop the leading whitespace already buffered for this line.
        var leading = tagStart - lineStart;
        if (leading > 0 && pendingText.Length >= leading)
            pendingText.Length -= leading;

        return i2 < body.Length ? i2 + 1 : body.Length;
    }

    private static (string Path, string? Argument) SplitHelperArguments(
        string helper, string rest, int line)
    {
        var quote = rest.IndexOf('"', StringComparison.Ordinal);
        if (quote < 0) return (rest.Trim(), null);

        var closing = rest.IndexOf('"', quote + 1);
        if (closing < 0)
            throw new TemplateException(
                $"unterminated quoted argument in tag '{{{{{helper} {rest}}}}}' "
                + $"at line {line}", line);

        return (rest[..quote].Trim(), rest[(quote + 1)..closing]);
    }

    private static int LineOf(string body, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < body.Length; i++)
            if (body[i] == '\n') line++;
        return line;
    }
}
