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
        if (paragraph?.Inline is null) return string.Empty;

        var sb = new StringBuilder();

        foreach (var inline in paragraph.Inline)
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
                default:
                    throw new InvalidOperationException(
                        $"Unexpected inline type in table cell: {inline.GetType()}. "
                        + "MarkdownEscaper should have escaped whatever produced it.");
            }
        }

        return sb.ToString();
    }

    /// <summary>Newlines legitimately become &lt;br&gt; in a cell, and
    /// surrounding whitespace is not significant. Everything else must survive
    /// byte for byte.</summary>
    private static string Normalise(string s) => s
        .Replace("<br>", "\n", StringComparison.Ordinal)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Trim();
}
