namespace DrMoustache.Core.Tests.Delivery;

using DrMoustache.Core.Delivery;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class DeliveryPlannerTests
{
    private sealed record Rec(string Id, string Name);

    private static DeliveryResult Plan(
        RenderConfig config, string body, params Rec[] records)
        => DeliveryPlanner.Plan(
            config,
            TemplateLexer.Lex(config.Output),
            TemplateLexer.Lex(body),
            passthrough: "",
            records.Cast<object>().ToList());

    private static RenderConfig Fanout(CollisionPolicy policy)
        => new("s", DeliveryMode.Fanout, "out/{{id}}.md", policy);

    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData("Ünïcode Ok", "ünïcode-ok")]
    [InlineData("  trim  ", "trim")]
    [InlineData("multi   space", "multi-space")]
    [InlineData("a:b*c?d", "abcd")]
    public void SlugifiesSafely(string input, string expected)
        => Slug.Make(input).Should().Be(expected);

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    public void SlugifyProducesNoDotOnlySegment(string input)
        => Slug.Make(input).Should().BeEmpty();

    [Fact]
    public void SlugifyStripsLeadingDotsSoNoHiddenFileIsProduced()
        => Slug.Make(".hidden").Should().Be("hidden");

    [Fact]
    public void FanoutSlugifiesEachInterpolatedValueBeforeSplicingIntoTheOutputPattern()
    {
        // A record value containing a path separator must collapse into a
        // single path segment, not invent a directory the template author
        // never wrote.
        var result = Plan(Fanout(CollisionPolicy.Error), "x", new Rec("a/b", "A"));

        result.Documents.Should().ContainSingle();
        result.Documents[0].Path.Should().Be("out/a-b.md");
    }

    [Fact]
    public void FanoutCannotEscapeTheOutputDirectoryViaDotDotSegments()
    {
        // A record value of "../../etc/passwd" must not let data write
        // outside the directory the 'output' pattern's own literal text
        // established. Every resulting path segment must stay under 'out/'.
        var result = Plan(Fanout(CollisionPolicy.Error), "x",
            new Rec("../../etc/passwd", "A"));

        result.Documents.Should().ContainSingle();
        var path = result.Documents[0].Path;

        path.Should().StartWith("out/");
        path.Split('/').Should().OnlyContain(segment => segment != "." && segment != "..");
    }

    [Fact]
    public void FanoutProducesNoDotSegmentWhenTheIdIsOnlyDots()
    {
        var result = Plan(Fanout(CollisionPolicy.Error), "x", new Rec("..", "A"));

        result.Documents.Should().ContainSingle();
        result.Documents[0].Path.Split('/').Should().OnlyContain(segment => segment != "..");
    }

    [Fact]
    public void FanoutStillHonoursLiteralMultiSegmentOutputPatterns()
    {
        // Directory structure the template author actually wrote in the
        // 'output' pattern's literal text must still work.
        var config = new RenderConfig(
            "s", DeliveryMode.Fanout, "out/{{id}}/{{name}}.md", CollisionPolicy.Error);
        var result = Plan(config, "x", new Rec("a", "b"));

        result.Documents.Should().ContainSingle();
        result.Documents[0].Path.Should().Be("out/a/b.md");
    }

    [Fact]
    public void FanoutCollapsesABackslashInAValueToo()
    {
        var result = Plan(Fanout(CollisionPolicy.Error), "x", new Rec(@"a\b", "A"));

        result.Documents.Should().ContainSingle();
        result.Documents[0].Path.Should().Be("out/a-b.md");
    }

    [Fact]
    public void FanoutProducesOneDocumentPerRecord()
    {
        var result = Plan(Fanout(CollisionPolicy.Error), "# {{name}}",
            new Rec("a", "Alpha"), new Rec("b", "Beta"));

        result.Documents.Should().HaveCount(2);
        result.Documents[0].Path.Should().Be("out/a.md");
        result.Documents[0].Content.Should().Contain("# Alpha");
    }

    [Fact]
    public void CollateProducesOneDocumentForAllRecords()
    {
        var config = new RenderConfig("s", DeliveryMode.Collate, "all.md", null);
        var result = DeliveryPlanner.Plan(
            config,
            TemplateLexer.Lex(config.Output),
            TemplateLexer.Lex("{{count}}:{{#records}}{{name}};{{/records}}"),
            passthrough: "",
            [new Rec("a", "Alpha"), new Rec("b", "Beta")]);

        result.Documents.Should().ContainSingle();
        result.Documents[0].Path.Should().Be("all.md");
        result.Documents[0].Content.Should().Be("2:Alpha;Beta;");
    }

    [Fact]
    public void CollisionErrorPolicyThrows()
    {
        var act = () => Plan(Fanout(CollisionPolicy.Error), "x",
            new Rec("same", "A"), new Rec("same", "B"));

        act.Should().Throw<DeliveryException>()
            .WithMessage("*out/same.md*2 records*");
    }

    [Fact]
    public void CollisionSuffixPolicyIsStableAcrossOrdering()
    {
        var forward = Plan(Fanout(CollisionPolicy.Suffix), "x",
            new Rec("same", "A"), new Rec("same", "B"));
        var reversed = Plan(Fanout(CollisionPolicy.Suffix), "x",
            new Rec("same", "B"), new Rec("same", "A"));

        forward.Documents.Select(d => d.Path).Should()
            .BeEquivalentTo(reversed.Documents.Select(d => d.Path),
                "suffixes hash the record, so order must not change filenames");
    }

    [Fact]
    public void CollisionSuffixPolicyWarns()
        => Plan(Fanout(CollisionPolicy.Suffix), "x",
                new Rec("same", "A"), new Rec("same", "B"))
            .Warnings.Should().ContainSingle().Which.Should().Contain("collision");

    [Fact]
    public void CollisionOrdinalPolicyAppendsCounters()
        => Plan(Fanout(CollisionPolicy.Ordinal), "x",
                new Rec("same", "A"), new Rec("same", "B"))
            .Documents.Select(d => d.Path).Should()
            .BeEquivalentTo(["out/same.md", "out/same-2.md"]);

    [Fact]
    public void CollisionOverwritePolicyKeepsTheLastRecord()
    {
        var result = Plan(Fanout(CollisionPolicy.Overwrite), "{{name}}",
            new Rec("same", "A"), new Rec("same", "B"));

        result.Documents.Should().ContainSingle();
        result.Documents[0].Content.Should().Be("B");
    }

    [Fact]
    public void PassthroughFrontmatterIsRenderedPerRecord()
    {
        var result = DeliveryPlanner.Plan(
            Fanout(CollisionPolicy.Error),
            TemplateLexer.Lex("out/{{id}}.md"),
            TemplateLexer.Lex("body"),
            passthrough: "title: \"{{name}}\"",
            [new Rec("a", "Alpha")]);

        result.Documents[0].Content.Should()
            .StartWith("---\ntitle: \"Alpha\"\n---\n").And.EndWith("body");
    }

    [Fact]
    public void NoFrontmatterIsEmittedWhenPassthroughIsEmpty()
        => Plan(Fanout(CollisionPolicy.Error), "body", new Rec("a", "A"))
            .Documents[0].Content.Should().Be("body");

    [Fact]
    public void InsertSuffixesTheFilenameNotADotInADirectoryName()
    {
        // "a.b/same" has a dot in the *directory* segment and none in the
        // filename. The suffix must land at the end of the path, not be
        // spliced in front of the directory's dot.
        var config = new RenderConfig(
            "s", DeliveryMode.Fanout, "a.b/{{id}}", CollisionPolicy.Ordinal);
        var result = Plan(config, "x", new Rec("same", "A"), new Rec("same", "B"));

        result.Documents.Select(d => d.Path).Should()
            .BeEquivalentTo(["a.b/same", "a.b/same-2"]);
    }
}
