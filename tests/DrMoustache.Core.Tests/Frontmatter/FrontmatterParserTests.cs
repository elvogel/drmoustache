namespace DrMoustache.Core.Tests.Frontmatter;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class FrontmatterParserTests
{
    private const string Fanout = """
        ---
        drmoustache:
          source: dlq
          delivery: fanout
          output: dlq/{{reason}}/{{sequenceNumber}}.md
          onCollision: suffix
        title: "{{orderId}}"
        layout: message
        ---

        # Order {{orderId}}
        """;

    [Fact]
    public void ReadsTheReservedBlock()
    {
        var t = FrontmatterParser.Parse(Fanout);
        t.Config.Source.Should().Be("dlq");
        t.Config.Delivery.Should().Be(DeliveryMode.Fanout);
        t.Config.Output.Should().Be("dlq/{{reason}}/{{sequenceNumber}}.md");
        t.Config.OnCollision.Should().Be(CollisionPolicy.Suffix);
    }

    [Fact]
    public void KeepsOtherKeysAsPassthroughText()
    {
        var t = FrontmatterParser.Parse(Fanout);
        t.PassthroughFrontmatter.Should().Contain("title:").And.Contain("layout: message");
        t.PassthroughFrontmatter.Should().NotContain("drmoustache");
        t.PassthroughFrontmatter.Should().NotContain("source: dlq");
    }

    [Fact]
    public void ReturnsTheBodySeparately()
        => FrontmatterParser.Parse(Fanout).Body.TrimStart()
            .Should().StartWith("# Order {{orderId}}");

    [Fact]
    public void CollateDoesNotRequireACollisionPolicy()
    {
        var t = FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: collate
              output: reports/deadletter.md
            ---
            body
            """);
        t.Config.Delivery.Should().Be(DeliveryMode.Collate);
        t.Config.OnCollision.Should().BeNull();
    }

    [Fact]
    public void FanoutWithoutCollisionPolicyIsAnError()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: fanout
              output: "{{id}}.md"
            ---
            body
            """);

        act.Should().Throw<TemplateException>()
            .WithMessage("*onCollision*required*fanout*")
            .And.Message.Should().Contain("error | suffix | ordinal | overwrite");
    }

    [Fact]
    public void LeadingDoubleBraceWithoutQuotesGivesAnActionableError()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: collate
              output: out.md
            title: {{orderId}}
            ---
            body
            """);

        act.Should().Throw<TemplateException>()
            .WithMessage("*must be quoted*")
            .And.Message.Should().Contain("flow mapping").And.Contain(@"title: ""{{orderId}}""");
    }

    [Fact]
    public void MustacheNotAtTheStartOfAValueNeedsNoQuotes()
    {
        var t = FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: fanout
              output: dlq/{{id}}.md
              onCollision: error
            ---
            body
            """);
        t.Config.Output.Should().Be("dlq/{{id}}.md");
    }

    [Fact]
    public void MissingFrontmatterIsAnError()
    {
        var act = () => FrontmatterParser.Parse("# just markdown");
        act.Should().Throw<TemplateException>().WithMessage("*frontmatter*");
    }

    [Fact]
    public void MissingReservedBlockIsAnError()
    {
        var act = () => FrontmatterParser.Parse("---\ntitle: x\n---\nbody");
        act.Should().Throw<TemplateException>().WithMessage("*drmoustache*");
    }

    [Fact]
    public void UnterminatedFrontmatterIsAnError()
    {
        var act = () => FrontmatterParser.Parse("---\ndrmoustache:\n  source: x\nbody");
        act.Should().Throw<TemplateException>().WithMessage("*not terminated*");
    }

    [Fact]
    public void CrlfFrontmatterIsHandled()
    {
        var t = FrontmatterParser.Parse(
            "---\r\ndrmoustache:\r\n  source: dlq\r\n  delivery: collate\r\n"
            + "  output: o.md\r\n---\r\nbody\r\n");
        t.Config.Source.Should().Be("dlq");
        t.Body.Trim().Should().Be("body");
    }

    [Fact]
    public void UnknownDeliveryModeIsAnError()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: x
              delivery: sideways
              output: o.md
            ---
            body
            """);
        act.Should().Throw<TemplateException>().WithMessage("*sideways*fanout*collate*");
    }

    [Fact]
    public void LeadingDoubleBraceInsideTheReservedBlockGivesAnActionableError()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: fanout
              output: {{sequenceNumber}}.md
              onCollision: suffix
            ---
            body
            """);

        act.Should().Throw<TemplateException>()
            .WithMessage("*must be quoted*")
            .Which.Message.Should()
                .Contain("flow mapping")
                .And.Contain("output: \"{{sequenceNumber}}.md\"")
                .And.NotContain("could not read");
    }

    [Fact]
    public void MustacheMidValueInsideTheReservedBlockNeedsNoQuotes()
    {
        var t = FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: dlq
              delivery: collate
              output: dlq/{{sequenceNumber}}.md
            ---
            body
            """);
        t.Config.Output.Should().Be("dlq/{{sequenceNumber}}.md");
    }

    [Fact]
    public void UnknownDeliveryModeErrorReportsItsOwnLine()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            drmoustache:
              source: x
              delivery: sideways
              output: o.md
            ---
            body
            """);

        // line 1: ---, line 2: drmoustache:, line 3: source, line 4: delivery
        act.Should().Throw<TemplateException>().Which.Line.Should().Be(4);
    }

    [Fact]
    public void FanoutWithoutCollisionPolicyErrorReportsTheReservedBlockHeaderLine()
    {
        var act = () => FrontmatterParser.Parse("""
            ---
            title: x
            drmoustache:
              source: dlq
              delivery: fanout
              output: o.md
            ---
            body
            """);

        // line 1: ---, line 2: title, line 3: drmoustache: (the header, since
        // onCollision is absent there is no key line to point at instead)
        act.Should().Throw<TemplateException>().Which.Line.Should().Be(3);
    }
}
