namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Xunit;

public class ContextScannerTests
{
    /// <summary>Context at the position of the marker character '@',
    /// which is stripped before scanning.</summary>
    private static MarkdownContext At(string templateWithMarker)
    {
        var index = templateWithMarker.IndexOf('@', StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, "the test must place a '@' marker");
        var template = templateWithMarker.Remove(index, 1);
        return ContextScanner.Scan(template)[index];
    }

    [Fact]
    public void PlainProseIsInline()
        => At("some text @here").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void StartOfLineIsLineStart()
        => At("first line\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void FirstCharacterOfDocumentIsLineStart()
        => At("@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void LineStartExtendsToColumnThree()
        => At("line\n   @value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void ColumnFourIsNoLongerLineStart()
        => At("line\n    @value").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void InsideFencedCodeIsFencedCode()
        => At("```\n@value\n```").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void AfterAClosedFenceIsNotFencedCode()
        => At("```\ncode\n```\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void TildeFencesAreRecognised()
        => At("~~~\n@value\n~~~").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void ShorterFenceDoesNotCloseALongerOne()
        => At("````\n```\n@value\n````").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void InsideCodeSpanIsCodeSpan()
        => At("text `@value` more").Should().Be(MarkdownContext.CodeSpan);

    [Fact]
    public void AfterCodeSpanIsInline()
        => At("text `code` @value").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void TableRowIsDetected()
        => At("| a | @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void SectionTagAtLineStartIsTransparentForTableDetection()
        => At("{{#items}}| a | @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void ClosingSectionTagIsAlsoTransparent()
        => At("{{/prev}}{{#items}}| @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void InvertedSectionTagIsAlsoTransparent()
        => At("{{^empty}}| @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void PipeInsideFencedCodeIsNotATableRow()
        => At("```\n| a | @value |\n```").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void LinkTargetIsDetected()
        => At("see [the docs](@value) here").Should().Be(MarkdownContext.LinkTarget);

    [Fact]
    public void LinkTextIsNotLinkTarget()
        => At("see [@value](http://x) here").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void CrlfLineEndingsAreHandled()
        => At("first\r\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void ScanReturnsOneEntryPerCharacter()
        => ContextScanner.Scan("abc").Should().HaveCount(3);

    [Fact]
    public void EmptyTemplateReturnsEmptyArray()
        => ContextScanner.Scan("").Should().BeEmpty();

    [Fact]
    public void CodeSpanSurvivesLineBreak()
        => At("`ab\nc@d`").Should().Be(MarkdownContext.CodeSpan);

    [Fact]
    public void BlankLineResetsCodeSpanState()
        => At("`a\n\n@b").Should().NotBe(MarkdownContext.CodeSpan);

    [Fact]
    public void UnterminatedCodeSpanDoesNotLeakPastItsParagraph()
        => At("`a\n\n@b").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void NewlineInsideFencedBlockIsFencedCode()
        => At("```\ncode@\nmore\n```").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void NewlineTerminatingTableRowIsTableRow()
        => At("| a | b |@\nnext line").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void UnterminatedCodeSpanDoesNotLeakAcrossAFence()
        => At("`a\n```\ncode\n```\n@b").Should().NotBe(MarkdownContext.CodeSpan);

    [Fact]
    public void UnterminatedCodeSpanDoesNotLeakOntoAFollowingTableRow()
        => At("`a\n| @h1 | h2 |").Should().Be(MarkdownContext.TableRow);

    // --- I6: an unmatched backtick run is literal text ---------------------
    //
    // CommonMark reads a backtick run with no closing run as ordinary text,
    // but the scanner opened a span on any run and never reconsidered. Because
    // CodeSpan escapes nothing, a stray backtick in prose switched escaping
    // OFF for everything up to the next paragraph interrupt -- silently, and
    // for tags the scanner would otherwise have labelled correctly.

    [Fact]
    public void AnUnmatchedBacktickRunDoesNotDisableEscapingAfterIt()
        => At("a ` stray backtick @here").Should().NotBe(MarkdownContext.CodeSpan);

    [Fact]
    public void AnUnmatchedBacktickRunIsItselfLiteralText()
        => At("a @` stray backtick here").Should().NotBe(MarkdownContext.CodeSpan);

    [Fact]
    public void AnUnmatchedBacktickRunAtEndOfDocumentIsUndone()
        => At("prose ` then @more").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void AMatchedBacktickRunIsStillACodeSpan()
        => At("a `@b` c").Should().Be(MarkdownContext.CodeSpan);

    [Fact]
    public void AnUnmatchedRunOnAnEarlierLineDoesNotSwallowALaterParagraph()
        => At("a ` stray\nstill the same @paragraph")
            .Should().NotBe(MarkdownContext.CodeSpan);
}
