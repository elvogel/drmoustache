namespace DrMoustache.Core.Tests.Delivery;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DrMoustache.Abstractions;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;

public class DeliveryPlannerTests
{
    // CA1812: instantiated by the tests below and reflected over by ManifestBuilder.
#pragma warning disable CA1812
    [DrMoustacheRecord("rec")]
    private sealed record Rec(
        [property: Field] string Id,
        [property: Field] string Name);

    /// <summary>Deliberately a record with a collection field: the synthesised
    /// ToString prints 'Tags = System.String[]', so hashing the string form
    /// gave two of these the same short hash and one document overwrote the
    /// other.</summary>
    [DrMoustacheRecord("tagged")]
    private sealed record Tagged(
        [property: Field] string Id,
        [property: Field] IReadOnlyList<string> Tags);

    /// <summary>Deliberately a plain class, not a record: it inherits
    /// object.ToString(), which returns the type name and nothing else, so
    /// every instance hashed identically.</summary>
    [DrMoustacheRecord("plain")]
    private sealed class Plain
    {
        [Field] public required string Id { get; init; }

        [Field] public required string Name { get; init; }
    }

    /// <summary>A decimal field exists to catch culture-sensitive formatting:
    /// object.ToString() renders 12.5 as '12,5' under de-DE, so the same
    /// records hashed differently per machine.</summary>
    [DrMoustacheRecord("priced")]
    private sealed record Priced(
        [property: Field] string Id,
        [property: Field] decimal Total);

    [DrMoustacheRecord("money")]
    private sealed record Money
    {
        [Field] public required string Id { get; init; }
        [Field] public required decimal Amount { get; init; }

        [SetsRequiredMembers]
        public Money(string id, decimal amount) { Id = id; Amount = amount; }
    }
#pragma warning restore CA1812

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

        // The frontmatter is re-serialised by YamlDotNet rather than spliced
        // together as text, so the author's own quoting is not preserved --
        // only the value is, and the block always parses.
        result.Documents[0].Content.Should()
            .StartWith("---\ntitle: Alpha\n---\n").And.EndWith("body");
    }

    /// <summary>The passthrough block is YAML, not Markdown. Markdown-escaping
    /// it produced documents no YAML parser could read: 'Acme *Corp*' became
    /// 'title: "Acme \*Corp\*"' (an unknown escape), a value containing a quote
    /// ended the scalar early, and 'C:\temp' was read back as 'C:&lt;TAB&gt;emp'.
    /// Every one of these must now survive a real parse.</summary>
    [Theory]
    [InlineData("Acme *Corp*")]
    [InlineData("order [42]")]
    [InlineData("the \"big\" order")]
    [InlineData(@"C:\temp")]
    [InlineData("under_score and `backtick`")]
    [InlineData("pipe | pipe")]
    [InlineData("two\nlines")]
    [InlineData("{ looks like flow }")]
    [InlineData("- looks like a sequence")]
    [InlineData("")]
    public void PassthroughFrontmatterRoundTripsThroughAYamlParser(string value)
    {
        var result = DeliveryPlanner.Plan(
            Fanout(CollisionPolicy.Error),
            TemplateLexer.Lex("out/{{id}}.md"),
            TemplateLexer.Lex("body"),
            passthrough: "title: \"{{name}}\"\nlayout: message",
            [new Rec("a", value)]);

        var content = result.Documents[0].Content;
        var frontmatter = content[4..content.IndexOf("\n---\n", StringComparison.Ordinal)];

        var parsed = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, string>>(frontmatter);

        parsed.Should().ContainKey("title");
        parsed["title"].Should().Be(value);
        parsed["layout"].Should().Be("message");
    }

    /// <summary>Parsing the passthrough block as YAML before interpolation is
    /// what makes the emitted frontmatter safe, but it also means the block must
    /// already be valid YAML. An unquoted tag is read as a flow mapping, and
    /// used to emit 'title:\n  ? name:\n  :' -- the value gone, with no
    /// exception and no warning. The design says this form fails, and names the
    /// error it should produce.</summary>
    [Theory]
    [InlineData("title: {{name}}")]
    [InlineData("  nested: {{name}}")]
    public void AnUnquotedFrontmatterTagIsRejectedWithTheSpecsGuidance(string passthrough)
    {
        var act = () => DeliveryPlanner.Plan(
            Fanout(CollisionPolicy.Error),
            TemplateLexer.Lex("out/{{id}}.md"),
            TemplateLexer.Lex("body"),
            passthrough,
            [new Rec("a", "Alpha")]);

        act.Should().Throw<DeliveryException>()
            .Which.Message.Should()
            .Contain("value must be quoted")
            .And.Contain("YAML reads a leading '{' as a flow mapping")
            .And.Contain("{{name}}\"");
    }

    /// <summary>The same mistake where a line scan cannot see it. It reaches the
    /// renderer as a non-scalar mapping key instead.</summary>
    [Fact]
    public void AnUnquotedTagInsideAFlowSequenceIsRejectedToo()
    {
        var act = () => DeliveryPlanner.Plan(
            Fanout(CollisionPolicy.Error),
            TemplateLexer.Lex("out/{{id}}.md"),
            TemplateLexer.Lex("body"),
            "tags: [a, {{name}}]",
            [new Rec("a", "Alpha")]);

        act.Should().Throw<DeliveryException>()
            .Which.Message.Should()
            .Contain("value must be quoted").And.Contain("{{name}}");
    }

    [Fact]
    public void TheQuotedFormStillRoundTrips()
        => DeliveryPlanner.Plan(
                Fanout(CollisionPolicy.Error),
                TemplateLexer.Lex("out/{{id}}.md"),
                TemplateLexer.Lex("body"),
                "title: \"{{name}}\"",
                [new Rec("a", "Alpha")])
            .Documents[0].Content.Should().StartWith("---\ntitle: Alpha\n---\n");

    [Fact]
    public void PassthroughFrontmatterKeepsNonScalarStructure()
    {
        var result = DeliveryPlanner.Plan(
            Fanout(CollisionPolicy.Error),
            TemplateLexer.Lex("out/{{id}}.md"),
            TemplateLexer.Lex("body"),
            passthrough: "tags: [deadletter, \"{{name}}\"]",
            [new Rec("a", "Acme *Corp*")]);

        var content = result.Documents[0].Content;
        var frontmatter = content[4..content.IndexOf("\n---\n", StringComparison.Ordinal)];

        new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, List<string>>>(frontmatter)["tags"]
            .Should().BeEquivalentTo(["deadletter", "Acme *Corp*"]);
    }

    [Fact]
    public void SuffixHashDistinguishesRecordsDifferingOnlyInACollectionField()
    {
        var config = new RenderConfig(
            "s", DeliveryMode.Fanout, "out/{{id}}.md", CollisionPolicy.Suffix);

        var result = DeliveryPlanner.Plan(
            config, TemplateLexer.Lex(config.Output), TemplateLexer.Lex("x"),
            passthrough: "",
            [new Tagged("same", ["a"]), new Tagged("same", ["b"])]);

        result.Documents.Select(d => d.Path).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SuffixHashDistinguishesInstancesOfAPlainClass()
    {
        var config = new RenderConfig(
            "s", DeliveryMode.Fanout, "out/{{id}}.md", CollisionPolicy.Suffix);

        var result = DeliveryPlanner.Plan(
            config, TemplateLexer.Lex(config.Output), TemplateLexer.Lex("x"),
            passthrough: "",
            [
                new Plain { Id = "same", Name = "A" },
                new Plain { Id = "same", Name = "B" },
            ]);

        result.Documents.Select(d => d.Path).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SuffixHashDoesNotVaryWithTheAmbientCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = PricedPaths();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            PricedPaths().Should().BeEquivalentTo(invariant,
                "a filename that changes with the host's locale is not deterministic");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        static IEnumerable<string> PricedPaths()
        {
            var config = new RenderConfig(
                "s", DeliveryMode.Fanout, "out/{{id}}.md", CollisionPolicy.Suffix);

            return DeliveryPlanner.Plan(
                    config, TemplateLexer.Lex(config.Output), TemplateLexer.Lex("x"),
                    passthrough: "",
                    [new Priced("same", 12.5m), new Priced("same", 1234.75m)])
                .Documents.Select(d => d.Path).ToList();
        }
    }

    /// <summary>'a', 'a', 'a-2' produced a.md, a-2.md, a-2.md: the generated
    /// ordinal landed on a path a third record legitimately owned, and the
    /// writer silently overwrote one of them.</summary>
    [Fact]
    public void OrdinalSuffixNeverLandsOnAnAlreadyClaimedPath()
    {
        var result = Plan(Fanout(CollisionPolicy.Ordinal), "x",
            new Rec("a", "A"), new Rec("a", "B"), new Rec("a-2", "C"));

        result.Documents.Should().HaveCount(3);
        result.Documents.Select(d => d.Path).Should().OnlyHaveUniqueItems();
        result.Documents.Select(d => d.Path).Should()
            .BeEquivalentTo(["out/a.md", "out/a-3.md", "out/a-2.md"]);
    }

    [Theory]
    [InlineData(CollisionPolicy.Ordinal)]
    [InlineData(CollisionPolicy.Suffix)]
    [InlineData(CollisionPolicy.Overwrite)]
    public void EveryPlannedDocumentPathIsDistinct(CollisionPolicy policy)
    {
        var result = Plan(Fanout(policy), "x",
            new Rec("a", "A"), new Rec("a", "B"), new Rec("a-2", "C"),
            new Rec("a-2", "D"), new Rec("b", "E"));

        result.Documents.Select(d => d.Path).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Both values slug to nothing, so the path assembled to
    /// 'out//.md', which the sanitiser reduced to 'out/.md' -- a hidden file
    /// with no name, produced without a word.</summary>
    [Fact]
    public void AnEmptiedSlugSegmentProducesNoDotfileAndIsWarnedAbout()
    {
        var config = new RenderConfig(
            "s", DeliveryMode.Fanout, "out/{{id}}/{{name}}.md", CollisionPolicy.Error);
        var result = Plan(config, "x", new Rec("", ""));

        result.Documents[0].Path.Split('/').Should()
            .OnlyContain(segment => !segment.StartsWith('.'));

        result.Warnings.Should().HaveCount(2)
            .And.Contain(w => w.Contains("'id'", StringComparison.Ordinal))
            .And.Contain(w => w.Contains("'name'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptiedSlugWarnsOncePerFieldNotOncePerRecord()
        => Plan(
                new RenderConfig(
                    "s", DeliveryMode.Fanout, "out/{{name}}-{{id}}.md",
                    CollisionPolicy.Ordinal),
                "x", new Rec("a", ""), new Rec("b", ""), new Rec("c", ""))
            .Warnings.Should().ContainSingle();

    [Fact]
    public void NoFrontmatterIsEmittedWhenPassthroughIsEmpty()
        => Plan(Fanout(CollisionPolicy.Error), "body", new Rec("a", "A"))
            .Documents[0].Content.Should().Be("body");

    [Fact]
    public void CultureFlowsThroughToRenderedContent()
    {
        var config = new RenderConfig("s", DeliveryMode.Collate, "all.md", null);

        var invariant = DeliveryPlanner.Plan(
            config, TemplateLexer.Lex(config.Output),
            TemplateLexer.Lex("{{#records}}{{amount}}{{/records}}"),
            passthrough: "", [new Money("x", 1234.5m)]);

        var german = DeliveryPlanner.Plan(
            config, TemplateLexer.Lex(config.Output),
            TemplateLexer.Lex("{{#records}}{{amount}}{{/records}}"),
            passthrough: "", [new Money("x", 1234.5m)],
            CultureInfo.GetCultureInfo("de-DE"));

        invariant.Documents[0].Content.Should().Be("1234.5");
        german.Documents[0].Content.Should().Be("1234,5");
    }

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
