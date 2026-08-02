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

    // Markdown trumps Mustache: interpolated data must not change the document's
    // formatting, so emphasis characters are escaped in a cell. Block markers like
    // '#' stay literal because a table cell is inline context -- a leading '#'
    // there cannot become a heading.
    [Fact]
    public void TableRowEscapesEmphasisButNotBlockMarkers()
        => MarkdownEscaper.Escape("#1 *top*", MarkdownContext.TableRow)
            .Should().Be(@"#1 \*top\*");

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

    // --- C1: the backslash is a special in its own right -------------------
    //
    // '\|pipe' used to escape to '\\|pipe'. CommonMark consumes the leading
    // pair as one literal backslash, which leaves the '|' live -- so a value
    // carrying a backslash before a pipe (a Windows path, a regex, escaped log
    // text) still shattered the table row the whole design exists to protect.

    [Theory]
    [InlineData(MarkdownContext.Inline)]
    [InlineData(MarkdownContext.TableRow)]
    [InlineData(MarkdownContext.LineStart)]
    public void ABackslashIsItselfEscaped(MarkdownContext context)
        => MarkdownEscaper.Escape(@"back\slash", context).Should().Be(@"back\\slash");

    [Theory]
    [InlineData(MarkdownContext.Inline)]
    [InlineData(MarkdownContext.TableRow)]
    public void ABackslashBeforeAPipeCannotReviveThePipe(MarkdownContext context)
        => MarkdownEscaper.Escape(@"\|pipe", context).Should().Be(@"\\\|pipe");

    // --- C2: a newline must not smuggle in block structure -----------------
    //
    // The line-start rules used to apply to the value's first character only,
    // so 'ok\n# Pwned' interpolated mid-paragraph introduced a live heading and
    // 'ok\n| a | b |\n|---|---|' introduced a live table.

    [Theory]
    [InlineData("ok\n# Pwned", "ok\n\\# Pwned")]
    [InlineData("ok\n> quoted", "ok\n\\> quoted")]
    [InlineData("ok\n- item", "ok\n\\- item")]
    [InlineData("ok\n1. item", "ok\n1\\. item")]
    [InlineData("ok\n    indented", "ok\n&#32;   indented")]
    [InlineData("ok\n~~~", "ok\n\\~~~")]
    [InlineData("ok\n===", "ok\n\\===")]
    public void EveryLineOfAMultiLineValueGetsTheBlockRules(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(expected);

    // A lone '\r' is one of CommonMark's three line terminators. Splitting the
    // value on '\n' alone treated 'ok\r# Pwned' as a single line, so the block
    // rules never re-fired and the heading stayed live -- C2 verbatim, arriving
    // via a different terminator.
    [Theory]
    [InlineData("ok\r# Pwned", "ok\r\\# Pwned")]
    [InlineData("ok\r> quote", "ok\r\\> quote")]
    [InlineData("ok\r\r# double", "ok\r\r\\# double")]
    [InlineData("ok\r\n# crlf", "ok\r\n\\# crlf")]
    [InlineData("ok\n\r# lf then cr", "ok\n\r\\# lf then cr")]
    public void ALoneCarriageReturnAlsoStartsANewLine(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(expected);

    [Fact]
    public void ACrLfPairIsOneLineBreakNotTwo()
        => MarkdownEscaper.Escape("a\r\nb", MarkdownContext.Inline).Should().Be("a\r\nb");

    // The same '\n' assumption in the link-destination path: a raw newline ends
    // the line, putting whatever follows at column 0 as live block syntax.
    // '[text]({{v}})' with 'x\n> quote' produced a real blockquote, and with
    // 'x\n```' a real fenced code block that swallowed the rest of the document.
    [Theory]
    [InlineData("x\n> quote", "x%0A>%20quote")]
    [InlineData("x\r# heading", "x%0D#%20heading")]
    [InlineData("x\r\n```", "x%0D%0A```")]
    public void ALinkTargetPercentEncodesLineTerminators(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LinkTarget).Should().Be(expected);

    [Fact]
    public void ANewlineCannotIntroduceATable()
        => MarkdownEscaper.Escape("ok\n| a | b |", MarkdownContext.Inline)
            .Should().Be("ok\n\\| a \\| b \\|");

    // --- I5: line start does the block work AND the inline work ------------
    //
    // The same value escaped at column 0 used to keep live emphasis, live
    // links, and a live pipe, so data reshaped the page depending only on
    // which column it happened to land in.

    [Fact]
    public void LineStartAlsoEscapesInlineSpecials()
        => MarkdownEscaper.Escape("a *b* [c](http://d)", MarkdownContext.LineStart)
            .Should().Be(@"a \*b\* \[c\](http://d)");

    [Fact]
    public void LineStartEscapesPipesLikeEverywhereElse()
        => MarkdownEscaper.Escape("a | b", MarkdownContext.LineStart)
            .Should().Be(@"a \| b");

    [Fact]
    public void LineStartEscapesTheBlockMarkerAndTheRestOfTheLine()
        => MarkdownEscaper.Escape("- a *b*", MarkdownContext.LineStart)
            .Should().Be(@"\- a \*b\*");

    // --- I9: a number is not an ordered list -------------------------------
    //
    // CommonMark requires whitespace after the marker, so '1234.5' was never a
    // list and must not collect a spurious backslash: these artifacts are
    // committed and diffed.

    [Theory]
    [InlineData("1234.5", "1234.5")]
    [InlineData("2026.08", "2026.08")]
    [InlineData("1)2", "1)2")]
    [InlineData("1.", @"1\.")]
    [InlineData("1. x", @"1\. x")]
    [InlineData("1) x", @"1\) x")]
    public void AnOrderedListMarkerNeedsTrailingWhitespace(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LineStart).Should().Be(expected);

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
