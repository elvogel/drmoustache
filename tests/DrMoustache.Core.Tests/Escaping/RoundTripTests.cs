namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
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

        var cellText = ExtractText(markdown, (TableRow)table[1]);
        Normalise(cellText).Should().Be(Normalise(value));
    }

    private static string ExtractText(string source, TableRow row)
    {
        // Table-cell paragraphs are parsed from a re-sliced buffer, so
        // LeafBlock.Lines is never populated for them (unlike ordinary
        // paragraphs). The block's source Span still points at the original
        // markdown, so recover the verbatim cell text from there instead.
        // An empty cell has no descendant block at all.
        var cell = (TableCell)row[0];
        var block = cell.Descendants<LeafBlock>().FirstOrDefault();
        return block is null ? string.Empty : source.Substring(block.Span.Start, block.Span.Length);
    }

    /// <summary>Newlines legitimately become &lt;br&gt; in a cell, and
    /// surrounding whitespace is not significant. Everything else must survive
    /// byte for byte.</summary>
    private static string Normalise(string s) => s
        .Replace("<br>", "\n", StringComparison.Ordinal)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace(@"\|", "|", StringComparison.Ordinal)
        .Trim();
}
