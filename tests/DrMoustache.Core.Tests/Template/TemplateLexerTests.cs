namespace DrMoustache.Core.Tests.Template;

using DrMoustache.Core.Escaping;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class TemplateLexerTests
{
    [Fact]
    public void PlainTextBecomesASingleTextNode()
        => TemplateLexer.Lex("hello world").Should().ContainSingle()
            .Which.Should().BeOfType<TextNode>()
            .Which.Text.Should().Be("hello world");

    [Fact]
    public void InterpolationCarriesItsPath()
        => TemplateLexer.Lex("a {{name}} b").OfType<InterpolationNode>()
            .Should().ContainSingle().Which.Path.Should().Be("name");

    [Fact]
    public void InterpolationCarriesItsMarkdownContext()
        => TemplateLexer.Lex("| a | {{name}} |").OfType<InterpolationNode>()
            .Should().ContainSingle().Which.Context.Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void DottedPathsAreKept()
        => TemplateLexer.Lex("{{order.total}}").OfType<InterpolationNode>()
            .Should().ContainSingle().Which.Path.Should().Be("order.total");

    [Fact]
    public void TripleBracesBecomeRawNodes()
        => TemplateLexer.Lex("{{{body}}}").OfType<RawNode>()
            .Should().ContainSingle().Which.Path.Should().Be("body");

    [Fact]
    public void HelperWithoutArgument()
    {
        var node = TemplateLexer.Lex("{{upper reason}}").OfType<HelperNode>().Single();
        node.Helper.Should().Be("upper");
        node.Path.Should().Be("reason");
        node.Argument.Should().BeNull();
    }

    [Fact]
    public void HelperWithQuotedArgument()
    {
        var node = TemplateLexer.Lex("""{{date placedAt "yyyy-MM-dd"}}""")
            .OfType<HelperNode>().Single();
        node.Helper.Should().Be("date");
        node.Path.Should().Be("placedAt");
        node.Argument.Should().Be("yyyy-MM-dd");
    }

    [Fact]
    public void SectionsNestTheirChildren()
    {
        var section = TemplateLexer.Lex("{{#items}}x {{name}}{{/items}}")
            .OfType<SectionNode>().Single();
        section.Path.Should().Be("items");
        section.Inverted.Should().BeFalse();
        section.Children.OfType<InterpolationNode>().Single().Path.Should().Be("name");
    }

    [Fact]
    public void InvertedSectionsAreMarked()
        => TemplateLexer.Lex("{{^empty}}none{{/empty}}").OfType<SectionNode>()
            .Single().Inverted.Should().BeTrue();

    [Fact]
    public void CommentsAreDropped()
        => TemplateLexer.Lex("a{{! ignore me }}b").OfType<TextNode>()
            .Select(n => n.Text).Should().BeEquivalentTo(["a", "b"]);

    [Fact]
    public void StandaloneSectionTagConsumesItsLine()
    {
        var nodes = TemplateLexer.Lex("before\n{{#items}}\nx\n{{/items}}\nafter");
        var flat = string.Concat(nodes.OfType<TextNode>().Select(n => n.Text));
        flat.Should().Be("before\nafter");
    }

    [Fact]
    public void IndentedStandaloneSectionTagLeavesNoStrayWhitespaceTextNode()
    {
        var nodes = TemplateLexer.Lex("before\n  {{#items}}\nx\n  {{/items}}\nafter");
        var flat = string.Concat(nodes.OfType<TextNode>().Select(n => n.Text));
        flat.Should().Be("before\nafter");
    }

    [Fact]
    public void SectionTagOnATableRowDoesNotConsumeTheLine()
    {
        var nodes = TemplateLexer.Lex("{{#items}}| {{name}} |\n{{/items}}");
        var section = nodes.OfType<SectionNode>().Single();
        var text = string.Concat(section.Children.OfType<TextNode>().Select(n => n.Text));
        text.Should().Contain("|").And.EndWith("\n",
            "the newline terminating each table row must survive");
    }

    [Fact]
    public void UnclosedSectionIsAnError()
    {
        var act = () => TemplateLexer.Lex("{{#items}}no close");
        act.Should().Throw<TemplateException>().WithMessage("*items*not closed*");
    }

    [Fact]
    public void MismatchedCloseIsAnError()
    {
        var act = () => TemplateLexer.Lex("{{#a}}{{/b}}");
        act.Should().Throw<TemplateException>().WithMessage("*expected*a*found*b*");
    }

    [Fact]
    public void UnterminatedTagIsAnError()
    {
        var act = () => TemplateLexer.Lex("a {{name");
        act.Should().Throw<TemplateException>().WithMessage("*unterminated*");
    }

    [Fact]
    public void UnterminatedQuotedArgumentIsAnError()
    {
        var act = () => TemplateLexer.Lex("""{{date placedAt "oops}}""");
        act.Should().Throw<TemplateException>().WithMessage("*unterminated*quoted*");
    }

    [Fact]
    public void LineNumbersAreOneBased()
        => TemplateLexer.Lex("one\ntwo\n{{name}}").OfType<InterpolationNode>()
            .Single().Line.Should().Be(3);
}
