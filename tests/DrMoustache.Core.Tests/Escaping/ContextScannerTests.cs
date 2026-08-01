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
}
