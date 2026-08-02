namespace DrMoustache.Core.Tests.Escaping;

using System.Text;
using DrMoustache.Core.Escaping;
using FluentAssertions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Xunit;

public class RoundTripTests
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static TheoryData<string> HostileValues() =>
    [
        "plain",
        "fast | cheap",
        "|leading pipe",
        "trailing pipe|",
        "||double||",
        "#1 pick",
        "*emphasis*",
        "_underscore_",
        "[link](http://x)",
        "back`tick",
        "back``ticks",
        @"back\slash",
        // A backslash immediately before a special was the gap that let the
        // missing '\' escape through: '\|pipe' escaped to '\\|pipe', whose
        // leading pair CommonMark consumes as a literal backslash, leaving the
        // pipe live to shatter the row.
        @"\|pipe",
        @"\*emph",
        @"back\slash|pipe",
        @"back\\slash|pipe",
        @"trailing backslash\",
        // Block structure smuggled in behind a newline.
        "ok\n# Pwned",
        "ok\n| a | b |\n|---|---|",
        "ok\n    indented",
        "ok\n> quoted",
        "ok\n```\nfenced",
        "ok\n1. listed",
        "ok\n---",
        "ok\n===",
        "a\nnewline",
        "a\r\nwindows newline",
        "   leading spaces",
        "trailing spaces   ",
        "O'Brien & Sons <Ltd>",
        "café — naïve 日本語",
        "{{not a tag}}",
        "- dash start",
        "> quote start",
        "1. ordered start",
        "",
    ];

    [Theory]
    [MemberData(nameof(HostileValues))]
    public void TableCellSurvivesRoundTrip(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var escaped = MarkdownEscaper.Escape(value, MarkdownContext.TableRow);
        var markdown = $"| Header |\n|--------|\n| {escaped} |\n";

        var document = Markdown.Parse(markdown, Pipeline);
        var table = document.Descendants<Table>().Should().ContainSingle().Subject;

        // Row 0 is the header; row 1 holds the value.
        table.Should().HaveCount(2, "the table must have a header and exactly one body row");

        var cellText = ExtractText((TableRow)table[1]);
        Normalise(cellText).Should().Be(Normalise(value));
    }

    /// <summary>The same property one context out. A value interpolated
    /// mid-paragraph must come back unchanged and must not have restructured
    /// the document around it: exactly one paragraph, and the literal text the
    /// value went in as.</summary>
    [Theory]
    [MemberData(nameof(HostileValues))]
    public void InlineSurvivesRoundTrip(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var escaped = MarkdownEscaper.Escape(value, MarkdownContext.Inline);
        var document = Markdown.Parse($"prefix {escaped} suffix\n", Pipeline);

        document.Should().AllBeOfType<ParagraphBlock>(
            "an interpolated value must not introduce a heading, list, quote, "
            + "table, or code block");

        Normalise(ExtractText(document)).Should().Be(Normalise($"prefix {value} suffix"));
    }

    /// <summary>And one context further out again: the value's own first
    /// character sits at column 0, where a block marker would otherwise be
    /// live.</summary>
    [Theory]
    [MemberData(nameof(HostileValues))]
    public void LineStartSurvivesRoundTrip(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var escaped = MarkdownEscaper.Escape(value, MarkdownContext.LineStart);
        var document = Markdown.Parse($"{escaped} tail\n", Pipeline);

        document.Should().AllBeOfType<ParagraphBlock>(
            "an interpolated value must not introduce a heading, list, quote, "
            + "table, or code block");

        Normalise(ExtractText(document)).Should().Be(Normalise($"{value} tail"));
    }

    /// <summary>Concatenates the literal text of every paragraph in the
    /// document, the same way <see cref="ExtractText(TableRow)"/> does for a
    /// cell: it is the parser, not a string diff, that must resolve the
    /// escaping back to the original value.</summary>
    private static string ExtractText(MarkdownDocument document)
    {
        var sb = new StringBuilder();

        foreach (var paragraph in document.Descendants<ParagraphBlock>())
            AppendInlines(sb, paragraph.Inline);

        return sb.ToString();
    }

    /// <summary>Walks the cell's parsed inline tree rather than reading
    /// <c>LeafBlock.Lines</c>. Two reasons: (1) table-cell paragraphs are
    /// parsed from a re-sliced buffer, so <c>Lines</c> is never populated for
    /// them here -- it's empty even for a cell needing no escaping at all;
    /// and (2) reading source text verbatim would prove the wrong property.
    /// The whole point of this test is that Markdig's parser -- not a string
    /// diff -- resolves the escaping back to the original value: a literal
    /// backslash-star (<c>\*</c>) must parse back to a plain <c>*</c>, not
    /// survive as two characters. Walking inlines lets the parser do that
    /// resolution for us.</summary>
    private static string ExtractText(TableRow row)
    {
        var cell = (TableCell)row[0];
        var paragraph = cell.Descendants<ParagraphBlock>().FirstOrDefault();

        var sb = new StringBuilder();
        AppendInlines(sb, paragraph?.Inline);
        return sb.ToString();
    }

    private static void AppendInlines(StringBuilder sb, ContainerInline? container)
    {
        if (container is null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case HtmlInline html:
                    // The only raw HTML a value can produce here is the <br>
                    // a newline is converted to, or HTML the value itself
                    // contained verbatim (HTML entities are never escaped).
                    sb.Append(html.Tag);
                    break;
                case HtmlEntityInline entity:
                    // A four-space indent at column 0 is broken by replacing
                    // the first space with '&#32;', which the parser decodes
                    // straight back to the space it stands for.
                    sb.Append(entity.Transcoded.ToString());
                    break;
                case LineBreakInline:
                    // Outside a table a newline in a value stays a newline.
                    sb.Append('\n');
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected inline type: {inline.GetType()}. "
                        + "MarkdownEscaper should have escaped whatever produced it.");
            }
        }
    }

    /// <summary>Newlines legitimately become &lt;br&gt; in a cell, and
    /// whitespace around a line is not significant to Markdown. Everything else
    /// must survive byte for byte.</summary>
    private static string Normalise(string s) => string.Join(
        '\n',
        s.Replace("<br>", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim()))
        .Trim();
}
