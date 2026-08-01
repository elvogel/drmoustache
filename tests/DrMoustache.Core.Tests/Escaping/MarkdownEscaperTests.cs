namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Xunit;

public class MarkdownEscaperTests
{
    [Theory]
    [InlineData("anything | at * all #", MarkdownContext.FencedCode)]
    [InlineData("anything | at * all #", MarkdownContext.CodeSpan)]
    public void CodeContextsEscapeNothing(string value, MarkdownContext context)
        => MarkdownEscaper.Escape(value, context).Should().Be(value);

    [Fact]
    public void TableRowEscapesPipes()
        => MarkdownEscaper.Escape("fast | cheap", MarkdownContext.TableRow)
            .Should().Be(@"fast \| cheap");

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    public void TableRowConvertsNewlinesToBreaks(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.TableRow)
            .Should().Be("a<br>b");

    [Fact]
    public void TableRowLeavesHashesAndEmphasisAlone()
        => MarkdownEscaper.Escape("#1 *top*", MarkdownContext.TableRow)
            .Should().Be("#1 *top*");

    [Theory]
    [InlineData("#1 pick", @"\#1 pick")]
    [InlineData("> quoted", @"\> quoted")]
    [InlineData("- item", @"\- item")]
    [InlineData("* item", @"\* item")]
    [InlineData("+ item", @"\+ item")]
    [InlineData("1. first", @"1\. first")]
    public void LineStartEscapesLeadingBlockMarkers(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LineStart).Should().Be(expected);

    [Fact]
    public void LineStartLeavesLaterCharactersAlone()
        => MarkdownEscaper.Escape("ok # later", MarkdownContext.LineStart)
            .Should().Be("ok # later");

    [Fact]
    public void LineStartEscapesLeadingFourSpaceIndent()
        => MarkdownEscaper.Escape("    indented", MarkdownContext.LineStart)
            .Should().Be("&#32;   indented");

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a(b", "a%28b")]
    [InlineData("a)b", "a%29b")]
    public void LinkTargetPercentEncodes(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LinkTarget).Should().Be(expected);

    [Theory]
    [InlineData("a*b", @"a\*b")]
    [InlineData("a_b", @"a\_b")]
    [InlineData("a[b", @"a\[b")]
    [InlineData("a]b", @"a\]b")]
    [InlineData("a`b", @"a\`b")]
    [InlineData("a|b", @"a\|b")]
    public void InlineEscapesEmphasisLinkAndPipe(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(expected);

    [Theory]
    [InlineData("a#b")]
    [InlineData("a-b")]
    [InlineData("a>b")]
    public void InlineLeavesBlockMarkersAlone(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(value);

    [Theory]
    [InlineData(MarkdownContext.Inline)]
    [InlineData(MarkdownContext.LineStart)]
    [InlineData(MarkdownContext.TableRow)]
    public void HtmlEntitiesAreNeverEscaped(MarkdownContext context)
        => MarkdownEscaper.Escape("O'Brien & Sons <Ltd>", context)
            .Should().Contain("&").And.Contain("<Ltd>").And.Contain("'");

    [Theory]
    [InlineData("plain", "`")]
    [InlineData("has ` one", "``")]
    [InlineData("has `` two", "```")]
    public void FenceForWidensPastTheLongestRun(string value, string expected)
        => MarkdownEscaper.FenceFor(value).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyAndWhitespaceValuesSurvive(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(value);

    [Fact]
    public void UnicodeIsUntouched()
        => MarkdownEscaper.Escape("café — naïve 日本語", MarkdownContext.Inline)
            .Should().Be("café — naïve 日本語");
}
