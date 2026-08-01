# DrMoustache Core Implementation Plan — Part 2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Continues** `2026-08-01-drmoustache-core.md`. Tasks 1–6 (scaffolding, plugin contract, manifest, context scanner, escaper) must be complete before starting Task 7.

**Goal:** Template lexing, frontmatter, value resolution, helpers, rendering, and delivery — completing `DrMoustache.Core`.

**Global Constraints:** as stated in Part 1. They apply to every task here.

---

### Task 7: Template lexer

**Files:**
- Create: `src/DrMoustache.Core/Template/TemplateNode.cs`
- Create: `src/DrMoustache.Core/Template/TemplateLexer.cs`
- Create: `src/DrMoustache.Core/Template/TemplateException.cs`
- Test: `tests/DrMoustache.Core.Tests/Template/TemplateLexerTests.cs`

**Interfaces:**
- Consumes: `MarkdownContext`, `ContextScanner` (Task 5).
- Produces:
  - `abstract record TemplateNode` with cases `TextNode(string Text)`, `InterpolationNode(string Path, MarkdownContext Context, int Line)`, `RawNode(string Path, int Line)`, `HelperNode(string Helper, string Path, string? Argument, MarkdownContext Context, int Line)`, `SectionNode(string Path, bool Inverted, IReadOnlyList<TemplateNode> Children, int Line)`.
  - `static IReadOnlyList<TemplateNode> TemplateLexer.Lex(string body)`.
  - `sealed class TemplateException : Exception` with `int Line`.

Tag forms: `{{path}}`, `{{{path}}}`, `{{helper path}}`, `{{helper path "arg"}}`, `{{#path}}…{{/path}}`, `{{^path}}…{{/path}}`, `{{! comment }}`.

**The standalone-tag rule** (spec): a section tag alone on a line consumes that line's trailing newline **only when the line is not a table row**. `ContextScanner` already tells us whether a position is `TableRow`, so the lexer consults it rather than re-deriving.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Template/TemplateLexerTests.cs`:

```csharp
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
    public void LineNumbersAreOneBased()
        => TemplateLexer.Lex("one\ntwo\n{{name}}").OfType<InterpolationNode>()
            .Single().Line.Should().Be(3);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateLexerTests`
Expected: compilation failure — the `Template` namespace does not exist.

- [ ] **Step 3: Write `TemplateException`**

`src/DrMoustache.Core/Template/TemplateException.cs`:

```csharp
namespace DrMoustache.Core.Template;

/// <summary>A static fault in a template. Raised before any provider is read,
/// because it affects every record.</summary>
public sealed class TemplateException : Exception
{
    public TemplateException() { }

    public TemplateException(string message) : base(message) { }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException) { }

    public TemplateException(string message, int line) : base(message) => Line = line;

    /// <summary>One-based line number, or 0 when not known.</summary>
    public int Line { get; }
}
```

- [ ] **Step 4: Write `TemplateNode`**

`src/DrMoustache.Core/Template/TemplateNode.cs`:

```csharp
namespace DrMoustache.Core.Template;

using DrMoustache.Core.Escaping;

public abstract record TemplateNode;

public sealed record TextNode(string Text) : TemplateNode;

/// <param name="Context">Where this tag sits in the Markdown, captured at lex
/// time so the renderer never re-derives it.</param>
public sealed record InterpolationNode(string Path, MarkdownContext Context, int Line)
    : TemplateNode;

/// <summary>A triple-brace tag. Never escaped.</summary>
public sealed record RawNode(string Path, int Line) : TemplateNode;

public sealed record HelperNode(
    string Helper, string Path, string? Argument, MarkdownContext Context, int Line)
    : TemplateNode;

public sealed record SectionNode(
    string Path, bool Inverted, IReadOnlyList<TemplateNode> Children, int Line)
    : TemplateNode;
```

- [ ] **Step 5: Write `TemplateLexer`**

`src/DrMoustache.Core/Template/TemplateLexer.cs`:

```csharp
namespace DrMoustache.Core.Template;

using System.Text;
using DrMoustache.Core.Escaping;

/// <summary>Turns template text into a node tree, capturing each tag's
/// Markdown context as it goes.</summary>
public static class TemplateLexer
{
    public static IReadOnlyList<TemplateNode> Lex(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var contexts = ContextScanner.Scan(body);
        var index = 0;
        var nodes = ReadNodes(body, contexts, ref index, openSection: null, openLine: 0);

        return nodes;
    }

    private static List<TemplateNode> ReadNodes(
        string body, MarkdownContext[] contexts, ref int index,
        string? openSection, int openLine)
    {
        var nodes = new List<TemplateNode>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            nodes.Add(new TextNode(text.ToString()));
            text.Clear();
        }

        while (index < body.Length)
        {
            var open = body.IndexOf("{{", index, StringComparison.Ordinal);

            if (open < 0)
            {
                text.Append(body, index, body.Length - index);
                index = body.Length;
                break;
            }

            text.Append(body, index, open - index);

            var isRaw = open + 2 < body.Length && body[open + 2] == '{';
            var closeToken = isRaw ? "}}}" : "}}";
            var contentStart = open + (isRaw ? 3 : 2);

            var close = body.IndexOf(closeToken, contentStart, StringComparison.Ordinal);
            if (close < 0)
                throw new TemplateException(
                    $"unterminated tag starting at line {LineOf(body, open)}",
                    LineOf(body, open));

            var content = body[contentStart..close].Trim();
            var line = LineOf(body, open);
            var afterTag = close + closeToken.Length;

            if (isRaw)
            {
                FlushText();
                nodes.Add(new RawNode(content, line));
                index = afterTag;
                continue;
            }

            if (content.StartsWith('!'))
            {
                index = ConsumeStandaloneLine(body, contexts, open, afterTag, text);
                continue;
            }

            if (content.StartsWith('#') || content.StartsWith('^'))
            {
                var inverted = content[0] == '^';
                var path = content[1..].Trim();

                FlushText();
                var next = ConsumeStandaloneLine(body, contexts, open, afterTag, text);
                FlushText();

                var children = ReadNodes(body, contexts, ref next, path, line);
                nodes.Add(new SectionNode(path, inverted, children, line));
                index = next;
                continue;
            }

            if (content.StartsWith('/'))
            {
                var path = content[1..].Trim();

                if (openSection is null)
                    throw new TemplateException(
                        $"closing tag '{{{{/{path}}}}}' at line {line} has no opening tag", line);

                if (!string.Equals(openSection, path, StringComparison.Ordinal))
                    throw new TemplateException(
                        $"mismatched closing tag at line {line}: "
                        + $"expected '{{{{/{openSection}}}}}', found '{{{{/{path}}}}}'", line);

                FlushText();
                index = ConsumeStandaloneLine(body, contexts, open, afterTag, text);
                FlushText();
                return nodes;
            }

            FlushText();
            var context = contexts[open];
            var space = content.IndexOf(' ', StringComparison.Ordinal);

            if (space < 0)
            {
                nodes.Add(new InterpolationNode(content, context, line));
            }
            else
            {
                var helper = content[..space];
                var rest = content[(space + 1)..].Trim();
                var (path, argument) = SplitHelperArguments(rest);
                nodes.Add(new HelperNode(helper, path, argument, context, line));
            }

            index = afterTag;
        }

        if (openSection is not null)
            throw new TemplateException(
                $"section '{{{{#{openSection}}}}}' opened at line {openLine} was not closed",
                openLine);

        FlushText();
        return nodes;
    }

    /// <summary>Applies the standalone-tag rule. A block tag alone on its line
    /// consumes the whole line — but never inside a table row, where the
    /// trailing newline terminates the row.</summary>
    private static int ConsumeStandaloneLine(
        string body, MarkdownContext[] contexts, int tagStart, int afterTag,
        StringBuilder pendingText)
    {
        if (contexts[tagStart] == MarkdownContext.TableRow) return afterTag;

        // Everything before the tag on this line must be whitespace.
        var lineStart = body.LastIndexOf('\n', Math.Max(tagStart - 1, 0)) + 1;
        for (var i = lineStart; i < tagStart; i++)
            if (!char.IsWhiteSpace(body[i])) return afterTag;

        // Everything after the tag up to the newline must be whitespace.
        var i2 = afterTag;
        while (i2 < body.Length && body[i2] != '\n')
        {
            if (!char.IsWhiteSpace(body[i2])) return afterTag;
            i2++;
        }

        // Drop the leading whitespace already buffered for this line.
        var leading = tagStart - lineStart;
        if (leading > 0 && pendingText.Length >= leading)
            pendingText.Length -= leading;

        return i2 < body.Length ? i2 + 1 : body.Length;
    }

    private static (string Path, string? Argument) SplitHelperArguments(string rest)
    {
        var quote = rest.IndexOf('"', StringComparison.Ordinal);
        if (quote < 0) return (rest.Trim(), null);

        var closing = rest.IndexOf('"', quote + 1);
        if (closing < 0) return (rest.Trim(), null);

        return (rest[..quote].Trim(), rest[(quote + 1)..closing]);
    }

    private static int LineOf(string body, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < body.Length; i++)
            if (body[i] == '\n') line++;
        return line;
    }
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateLexerTests`
Expected: 16 tests passed.

`SectionTagOnATableRowDoesNotConsumeTheLine` is the one that matters most — if it fails, table fan-out emits a single run-on line instead of one row per record.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache.Core/Template tests/DrMoustache.Core.Tests/Template
git commit -m "$(cat <<'EOF'
Add template lexer

Captures each tag's Markdown context at lex time so the renderer never
re-derives it.

The standalone-tag rule deviates from spec Mustache: a block tag alone on
its line consumes that line, except inside a table row where the trailing
newline terminates the row. This is what makes table fan-out work.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 8: Frontmatter and render configuration

**Files:**
- Create: `src/DrMoustache.Core/Frontmatter/DeliveryMode.cs`
- Create: `src/DrMoustache.Core/Frontmatter/CollisionPolicy.cs`
- Create: `src/DrMoustache.Core/Frontmatter/RenderConfig.cs`
- Create: `src/DrMoustache.Core/Frontmatter/ParsedTemplate.cs`
- Create: `src/DrMoustache.Core/Frontmatter/FrontmatterParser.cs`
- Test: `tests/DrMoustache.Core.Tests/Frontmatter/FrontmatterParserTests.cs`

**Interfaces:**
- Consumes: `TemplateException` (Task 7).
- Produces:
  - `enum DeliveryMode { Fanout, Collate }`, `enum CollisionPolicy { Error, Suffix, Ordinal, Overwrite }`
  - `sealed record RenderConfig(string Source, DeliveryMode Delivery, string Output, CollisionPolicy? OnCollision)`
  - `sealed record ParsedTemplate(RenderConfig Config, string PassthroughFrontmatter, string Body)`
  - `static ParsedTemplate FrontmatterParser.Parse(string content)`

`PassthroughFrontmatter` is the raw YAML of every key *except* `drmoustache`, kept as text because it is itself a template (rendered per record in Task 11).

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Frontmatter/FrontmatterParserTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~FrontmatterParserTests`
Expected: compilation failure — the `Frontmatter` namespace does not exist.

- [ ] **Step 3: Write the enums and models**

`src/DrMoustache.Core/Frontmatter/DeliveryMode.cs`:

```csharp
namespace DrMoustache.Core.Frontmatter;

public enum DeliveryMode
{
    /// <summary>One document per record.</summary>
    Fanout,

    /// <summary>All records into one document.</summary>
    Collate,
}
```

`src/DrMoustache.Core/Frontmatter/CollisionPolicy.cs`:

```csharp
namespace DrMoustache.Core.Frontmatter;

/// <summary>What to do when two records produce the same output path. There is
/// deliberately no default — choosing is the user's decision, not the tool's.</summary>
public enum CollisionPolicy
{
    /// <summary>Fail the render, naming the colliding values.</summary>
    Error,

    /// <summary>Append a short hash of the record's declared field values.
    /// Deterministic across runs regardless of arrival order.</summary>
    Suffix,

    /// <summary>Append -2, -3 by encounter order. Not stable if the provider's
    /// order changes.</summary>
    Ordinal,

    /// <summary>Last record wins; earlier ones are discarded.</summary>
    Overwrite,
}
```

`src/DrMoustache.Core/Frontmatter/RenderConfig.cs`:

```csharp
namespace DrMoustache.Core.Frontmatter;

/// <param name="OnCollision">Required for <see cref="DeliveryMode.Fanout"/>,
/// null for <see cref="DeliveryMode.Collate"/>.</param>
public sealed record RenderConfig(
    string Source,
    DeliveryMode Delivery,
    string Output,
    CollisionPolicy? OnCollision);
```

`src/DrMoustache.Core/Frontmatter/ParsedTemplate.cs`:

```csharp
namespace DrMoustache.Core.Frontmatter;

/// <param name="PassthroughFrontmatter">Raw YAML of every key except
/// <c>drmoustache</c>. Kept as text because it is itself a template,
/// interpolated per record.</param>
public sealed record ParsedTemplate(
    RenderConfig Config,
    string PassthroughFrontmatter,
    string Body);
```

- [ ] **Step 4: Write `FrontmatterParser`**

`src/DrMoustache.Core/Frontmatter/FrontmatterParser.cs`:

```csharp
namespace DrMoustache.Core.Frontmatter;

using System.Text;
using DrMoustache.Core.Template;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>Splits a template into its reserved configuration block, its
/// passthrough frontmatter, and its body.</summary>
public static class FrontmatterParser
{
    private const string ReservedKey = "drmoustache";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ParsedTemplate Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var (frontmatter, body, frontmatterStartLine) = SplitFrontmatter(content);
        var (reserved, passthrough) = SplitReservedBlock(frontmatter, frontmatterStartLine);

        return new ParsedTemplate(ReadConfig(reserved, frontmatterStartLine), passthrough, body);
    }

    private static (string Frontmatter, string Body, int StartLine) SplitFrontmatter(
        string content)
    {
        var normalised = content.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalised.StartsWith("---\n", StringComparison.Ordinal))
            throw new TemplateException(
                "template has no YAML frontmatter; it must begin with a '---' line", 1);

        var end = normalised.IndexOf("\n---", 3, StringComparison.Ordinal);

        while (end >= 0)
        {
            var afterMarker = end + 4;
            var restOfLine = normalised.AsSpan(afterMarker);
            var lineBreak = restOfLine.IndexOf('\n');
            var trailing = lineBreak < 0 ? restOfLine : restOfLine[..lineBreak];

            if (trailing.IsWhiteSpace()) break;

            end = normalised.IndexOf("\n---", afterMarker, StringComparison.Ordinal);
        }

        if (end < 0)
            throw new TemplateException(
                "frontmatter was not terminated; add a closing '---' line", 1);

        var frontmatter = normalised[4..end];
        var bodyStart = normalised.IndexOf('\n', end + 4);
        var body = bodyStart < 0 ? "" : normalised[(bodyStart + 1)..];

        return (frontmatter, body, StartLine: 2);
    }

    /// <summary>Separates the <c>drmoustache</c> block from every other key by
    /// indentation, so the passthrough half survives as raw text.</summary>
    private static (string Reserved, string Passthrough) SplitReservedBlock(
        string frontmatter, int startLine)
    {
        var lines = frontmatter.Split('\n');
        var reserved = new StringBuilder();
        var passthrough = new StringBuilder();

        var inReserved = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith(ReservedKey + ":", StringComparison.Ordinal))
            {
                inReserved = true;
                continue;
            }

            var isIndented = line.Length > 0 && char.IsWhiteSpace(line[0]);

            if (inReserved && (isIndented || line.Trim().Length == 0))
            {
                reserved.Append(line.TrimStart()).Append('\n');
                continue;
            }

            inReserved = false;
            GuardLeadingBrace(line, startLine + i);
            passthrough.Append(line).Append('\n');
        }

        if (reserved.Length == 0)
            throw new TemplateException(
                $"template has no '{ReservedKey}:' block in its frontmatter", startLine);

        return (reserved.ToString(), passthrough.ToString().TrimEnd('\n'));
    }

    /// <summary>YAML reads a leading '{' as a flow mapping, so a value starting
    /// with '{{' fails to parse. Detect it and say what to write instead.</summary>
    private static void GuardLeadingBrace(string line, int lineNumber)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return;

        var value = line[(colon + 1)..].Trim();
        if (!value.StartsWith("{{", StringComparison.Ordinal)) return;

        var key = line[..colon].Trim();

        throw new TemplateException(
            $"value must be quoted (line {lineNumber})\n"
            + $"       {key}: {value}\n"
            + $"       {new string(' ', key.Length + 2)}^ YAML reads a leading '{{' "
            + "as a flow mapping\n"
            + $"       write:  {key}: \"{value}\"",
            lineNumber);
    }

    private static RenderConfig ReadConfig(string reservedYaml, int startLine)
    {
        ReservedBlock block;

        try
        {
            block = Yaml.Deserialize<ReservedBlock>(reservedYaml) ?? new ReservedBlock();
        }
        catch (YamlException ex)
        {
            throw new TemplateException(
                $"could not read the '{ReservedKey}' block: {ex.Message}", startLine);
        }

        if (string.IsNullOrWhiteSpace(block.Source))
            throw new TemplateException("'source' is required", startLine);

        if (string.IsNullOrWhiteSpace(block.Output))
            throw new TemplateException("'output' is required", startLine);

        var delivery = block.Delivery?.ToLowerInvariant() switch
        {
            "fanout" => DeliveryMode.Fanout,
            "collate" => DeliveryMode.Collate,
            null => throw new TemplateException("'delivery' is required", startLine),
            var other => throw new TemplateException(
                $"unknown delivery '{other}'; choose one: fanout | collate", startLine),
        };

        CollisionPolicy? collision = block.OnCollision?.ToLowerInvariant() switch
        {
            "error" => CollisionPolicy.Error,
            "suffix" => CollisionPolicy.Suffix,
            "ordinal" => CollisionPolicy.Ordinal,
            "overwrite" => CollisionPolicy.Overwrite,
            null => null,
            var other => throw new TemplateException(
                $"unknown onCollision '{other}'; "
                + "choose one: error | suffix | ordinal | overwrite", startLine),
        };

        if (delivery == DeliveryMode.Fanout && collision is null)
            throw new TemplateException(
                "'onCollision' is required when delivery is 'fanout'\n"
                + "       choose one: error | suffix | ordinal | overwrite", startLine);

        return new RenderConfig(block.Source!, delivery, block.Output!, collision);
    }

    private sealed class ReservedBlock
    {
        public string? Source { get; set; }
        public string? Delivery { get; set; }
        public string? Output { get; set; }
        public string? OnCollision { get; set; }
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~FrontmatterParserTests`
Expected: 12 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Core/Frontmatter tests/DrMoustache.Core.Tests/Frontmatter
git commit -m "$(cat <<'EOF'
Add frontmatter parser and render configuration

The reserved drmoustache block is split out by indentation so the
passthrough half survives as raw text -- it is itself a template.

onCollision has no default: choosing a collision policy is the user's
decision, so omitting it on fanout is an error listing the four options.

A value beginning '{{' is caught before YAML sees it, since YAML reads a
leading brace as a flow mapping.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 9: Value resolution

**Files:**
- Create: `src/DrMoustache.Core/Binding/ValueResolver.cs`
- Test: `tests/DrMoustache.Core.Tests/Binding/ValueResolverTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `static bool ValueResolver.TryResolve(object? root, string path, out object? value)`
  - `static bool ValueResolver.IsTruthy(object? value)` — section presence test: null is false, `false` is false, an empty string is false, an empty sequence is false, everything else is true.
  - `static IEnumerable<object?>? ValueResolver.AsSequence(object? value)` — non-null only for a non-string `IEnumerable`.

Resolution walks dotted paths over public properties, matching case-insensitively so `{{orderId}}` finds `OrderId`. `IDictionary<string, object?>` is also supported, so a caller embedding the library can pass property bags.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Binding/ValueResolverTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Binding;

using DrMoustache.Core.Binding;
using FluentAssertions;
using Xunit;

public class ValueResolverTests
{
    private sealed record Inner(string Id, decimal Total);

    private sealed record Outer(
        string OrderId, string? PromoCode, Inner Order, IReadOnlyList<string> Tags);

    private static Outer Sample() =>
        new("ORD-1", null, new Inner("i-1", 12.5m), ["a", "b"]);

    [Fact]
    public void ResolvesTopLevelProperty()
    {
        ValueResolver.TryResolve(Sample(), "orderId", out var v).Should().BeTrue();
        v.Should().Be("ORD-1");
    }

    [Fact]
    public void MatchesCaseInsensitively()
        => ValueResolver.TryResolve(Sample(), "OrderId", out _).Should().BeTrue();

    [Fact]
    public void ResolvesNestedPath()
    {
        ValueResolver.TryResolve(Sample(), "order.total", out var v).Should().BeTrue();
        v.Should().Be(12.5m);
    }

    [Fact]
    public void ResolvesNullPropertyAsPresentButNull()
    {
        ValueResolver.TryResolve(Sample(), "promoCode", out var v).Should().BeTrue();
        v.Should().BeNull();
    }

    [Fact]
    public void FailsOnUnknownPath()
        => ValueResolver.TryResolve(Sample(), "nope", out _).Should().BeFalse();

    [Fact]
    public void FailsWhenDescendingThroughNull()
        => ValueResolver.TryResolve(Sample(), "promoCode.length", out _).Should().BeFalse();

    [Fact]
    public void ResolvesDictionaryEntries()
    {
        var bag = new Dictionary<string, object?> { ["name"] = "x" };
        ValueResolver.TryResolve(bag, "name", out var v).Should().BeTrue();
        v.Should().Be("x");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData(0, true)]
    public void TruthinessFollowsMustacheRules(object? value, bool expected)
        => ValueResolver.IsTruthy(value).Should().Be(expected);

    [Fact]
    public void EmptySequenceIsFalsy()
        => ValueResolver.IsTruthy(Array.Empty<string>()).Should().BeFalse();

    [Fact]
    public void NonEmptySequenceIsTruthy()
        => ValueResolver.IsTruthy(new[] { "a" }).Should().BeTrue();

    [Fact]
    public void SequencesAreEnumerable()
    {
        ValueResolver.TryResolve(Sample(), "tags", out var v).Should().BeTrue();
        ValueResolver.AsSequence(v).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void StringsAreNotSequences()
        => ValueResolver.AsSequence("abc").Should().BeNull();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ValueResolverTests`
Expected: compilation failure — `ValueResolver` does not exist.

- [ ] **Step 3: Write `ValueResolver`**

`src/DrMoustache.Core/Binding/ValueResolver.cs`:

```csharp
namespace DrMoustache.Core.Binding;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

/// <summary>Resolves dotted template paths against a record.</summary>
public static class ValueResolver
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Cache = new();

    public static bool TryResolve(object? root, string path, out object? value)
    {
        ArgumentNullException.ThrowIfNull(path);

        value = null;
        var current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is null) return false;

            if (current is IDictionary<string, object?> bag)
            {
                if (!bag.TryGetValue(segment, out current)) return false;
                continue;
            }

            var property = Cache.GetOrAdd((current.GetType(), segment), static key =>
                key.Item1.GetProperty(key.Item2,
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy));

            if (property is null) return false;

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    /// <summary>Section presence test. Null, false, empty string, and empty
    /// sequence are falsy; everything else is truthy.</summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true,
    };

    /// <summary>The value as a sequence, or null when it is not one. Strings
    /// are deliberately not sequences.</summary>
    public static IEnumerable<object?>? AsSequence(object? value) =>
        value is IEnumerable e and not string ? e.Cast<object?>() : null;
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ValueResolverTests`
Expected: 17 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Core/Binding tests/DrMoustache.Core.Tests/Binding
git commit -m "$(cat <<'EOF'
Add value resolver

Dotted-path resolution over properties and dictionaries, with a
PropertyInfo cache keyed on (type, segment). Mustache truthiness rules for
section presence.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 10: Helpers

**Files:**
- Create: `src/DrMoustache.Core/Helpers/HelperRegistry.cs`
- Test: `tests/DrMoustache.Core.Tests/Helpers/HelperRegistryTests.cs`

**Interfaces:**
- Consumes: `TemplateException` (Task 7).
- Produces: `static bool HelperRegistry.IsKnown(string name)` and `static string HelperRegistry.Apply(string name, object? value, string? argument, int line)`.

Closed set: `date`, `format`, `upper`, `lower`, `join`, `default`. There is no registration API — extensibility is the provider's job.

Helper output is escaped by the caller (Task 11) using the same context rules as a plain interpolation. Helpers never bypass escaping.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Helpers/HelperRegistryTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Helpers;

using System.Globalization;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class HelperRegistryTests
{
    [Theory]
    [InlineData("date")]
    [InlineData("format")]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("join")]
    [InlineData("default")]
    public void KnownHelpersAreRecognised(string name)
        => HelperRegistry.IsKnown(name).Should().BeTrue();

    [Fact]
    public void UnknownHelperIsNotRecognised()
        => HelperRegistry.IsKnown("frobnicate").Should().BeFalse();

    [Fact]
    public void ApplyingAnUnknownHelperThrows()
    {
        var act = () => HelperRegistry.Apply("frobnicate", "x", null, 3);
        act.Should().Throw<TemplateException>()
            .WithMessage("*frobnicate*")
            .And.Message.Should().Contain("date").And.Contain("default");
    }

    [Fact]
    public void DateFormatsTemporalValues()
        => HelperRegistry.Apply(
                "date", new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.Zero),
                "yyyy-MM-dd HH:mm", 1)
            .Should().Be("2026-08-01 14:30");

    [Fact]
    public void DateWithoutAnArgumentUsesRoundTripFormat()
        => HelperRegistry.Apply("date", new DateOnly(2026, 8, 1), null, 1)
            .Should().Be("2026-08-01");

    [Fact]
    public void DateRejectsNonTemporalValues()
    {
        var act = () => HelperRegistry.Apply("date", "not a date", "yyyy", 7);
        act.Should().Throw<TemplateException>().WithMessage("*date*line 7*");
    }

    [Fact]
    public void FormatFormatsNumbers()
        => HelperRegistry.Apply("format", 1234.5m, "N2", 1)
            .Should().Be(1234.5m.ToString("N2", CultureInfo.CurrentCulture));

    [Fact]
    public void FormatRejectsNonNumericValues()
    {
        var act = () => HelperRegistry.Apply("format", "abc", "N2", 4);
        act.Should().Throw<TemplateException>().WithMessage("*format*line 4*");
    }

    [Fact]
    public void UpperAndLowerUseInvariantCasing()
    {
        HelperRegistry.Apply("upper", "abc", null, 1).Should().Be("ABC");
        HelperRegistry.Apply("lower", "ABC", null, 1).Should().Be("abc");
    }

    [Fact]
    public void JoinUsesTheSuppliedSeparator()
        => HelperRegistry.Apply("join", new[] { "a", "b", "c" }, ", ", 1)
            .Should().Be("a, b, c");

    [Fact]
    public void JoinDefaultsToCommaSpace()
        => HelperRegistry.Apply("join", new[] { "a", "b" }, null, 1).Should().Be("a, b");

    [Fact]
    public void JoinRejectsNonSequences()
    {
        var act = () => HelperRegistry.Apply("join", 42, ", ", 9);
        act.Should().Throw<TemplateException>().WithMessage("*join*line 9*");
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("x", "x")]
    public void DefaultSubstitutesForNullAndEmpty(object? value, string expected)
        => HelperRegistry.Apply("default", value, "none", 1).Should().Be(expected);

    [Fact]
    public void NullValuesRenderEmptyForNonDefaultHelpers()
        => HelperRegistry.Apply("upper", null, null, 1).Should().BeEmpty();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~HelperRegistryTests`
Expected: compilation failure — `HelperRegistry` does not exist.

- [ ] **Step 3: Write `HelperRegistry`**

`src/DrMoustache.Core/Helpers/HelperRegistry.cs`:

```csharp
namespace DrMoustache.Core.Helpers;

using System.Globalization;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Template;

/// <summary>The closed set of value transforms callable from a template.</summary>
/// <remarks>There is no registration API. A value that needs computing should
/// be computed in the provider, which is why providers exist.</remarks>
public static class HelperRegistry
{
    private static readonly string[] Names =
        ["date", "format", "upper", "lower", "join", "default"];

    public static bool IsKnown(string name) =>
        Array.IndexOf(Names, name) >= 0;

    public static string Apply(string name, object? value, string? argument, int line)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name switch
        {
            "date" => Date(value, argument, line),
            "format" => Format(value, argument, line),
            "upper" => Stringify(value).ToUpperInvariant(),
            "lower" => Stringify(value).ToLowerInvariant(),
            "join" => Join(value, argument, line),
            "default" => Default(value, argument),
            _ => throw new TemplateException(
                $"unknown helper '{name}' (line {line})\n"
                + $"       available: {string.Join(", ", Names)}", line),
        };
    }

    private static string Date(object? value, string? format, int line) => value switch
    {
        null => "",
        DateTimeOffset d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        DateTime d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        DateOnly d => d.ToString(format ?? "O", CultureInfo.CurrentCulture),
        TimeOnly t => t.ToString(format ?? "O", CultureInfo.CurrentCulture),
        _ => throw new TemplateException(
            $"'date' needs a temporal value but got "
            + $"{value.GetType().Name} (line {line})", line),
    };

    private static string Format(object? value, string? format, int line)
    {
        if (value is null) return "";

        if (value is IFormattable formattable and not string
            && value is not DateTime and not DateTimeOffset
            and not DateOnly and not TimeOnly)
            return formattable.ToString(format, CultureInfo.CurrentCulture);

        throw new TemplateException(
            $"'format' needs a numeric value but got "
            + $"{value.GetType().Name} (line {line})", line);
    }

    private static string Join(object? value, string? separator, int line)
    {
        if (value is null) return "";

        var sequence = ValueResolver.AsSequence(value)
            ?? throw new TemplateException(
                $"'join' needs a collection but got "
                + $"{value.GetType().Name} (line {line})", line);

        return string.Join(separator ?? ", ", sequence.Select(Stringify));
    }

    private static string Default(object? value, string? fallback)
    {
        var text = Stringify(value);
        return text.Length == 0 ? fallback ?? "" : text;
    }

    private static string Stringify(object? value) => value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~HelperRegistryTests`
Expected: 22 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Core/Helpers tests/DrMoustache.Core.Tests/Helpers
git commit -m "$(cat <<'EOF'
Add helper registry

A closed set -- date, format, upper, lower, join, default -- with no
registration API. Extensibility belongs in the provider.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 11: Renderer

**Files:**
- Create: `src/DrMoustache.Core/Rendering/RenderedDocument.cs`
- Create: `src/DrMoustache.Core/Rendering/TemplateRenderer.cs`
- Test: `tests/DrMoustache.Core.Tests/Rendering/TemplateRendererTests.cs`

**Interfaces:**
- Consumes: `TemplateNode` and subtypes, `TemplateLexer` (Task 7); `MarkdownEscaper`, `MarkdownContext` (Tasks 5–6); `ValueResolver` (Task 9); `HelperRegistry` (Task 10).
- Produces:
  - `sealed record RenderedDocument(string Path, string Content)` — `Path` is assigned by the delivery planner (Task 12) and is empty here.
  - `static string TemplateRenderer.Render(IReadOnlyList<TemplateNode> nodes, object? context)`

Escaping happens here: each `InterpolationNode` uses the `MarkdownContext` captured at lex time. `RawNode` bypasses escaping. `HelperNode` output is escaped like an interpolation.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Rendering/TemplateRendererTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Rendering;

using DrMoustache.Core.Rendering;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class TemplateRendererTests
{
    private sealed record Item(string Name, int Qty);

    private sealed record Model(
        string Title, string? Note, decimal Total, IReadOnlyList<Item> Items);

    private static string Render(string template, object? model)
        => TemplateRenderer.Render(TemplateLexer.Lex(template), model);

    [Fact]
    public void InterpolatesAValue()
        => Render("Hello {{title}}", new Model("world", null, 0, []))
            .Should().Be("Hello world");

    [Fact]
    public void EscapesAccordingToContext()
        => Render("| {{title}} |", new Model("a | b", null, 0, []))
            .Should().Be(@"| a \| b |");

    [Fact]
    public void DoesNotEscapeInsideFencedCode()
        => Render("```\n{{title}}\n```", new Model("a | b", null, 0, []))
            .Should().Contain("a | b");

    [Fact]
    public void TripleBracesBypassEscaping()
        => Render("| {{{title}}} |", new Model("a | b", null, 0, []))
            .Should().Be("| a | b |");

    [Fact]
    public void EscapesLeadingBlockMarkersAtLineStart()
        => Render("{{title}}", new Model("#1 pick", null, 0, []))
            .Should().Be(@"\#1 pick");

    [Fact]
    public void NullValuesRenderEmpty()
        => Render("[{{note}}]", new Model("x", null, 0, [])).Should().Be("[]");

    [Fact]
    public void UnknownPathsRenderEmpty()
        => Render("[{{nope}}]", new Model("x", null, 0, [])).Should().Be("[]");

    [Fact]
    public void SectionsIterateCollections()
        => Render("{{#items}}{{name}};{{/items}}",
                new Model("x", null, 0, [new("a", 1), new("b", 2)]))
            .Should().Be("a;b;");

    [Fact]
    public void SectionsProduceOneTableRowPerItem()
    {
        var output = Render(
            "| Item | Qty |\n|---|---|\n{{#items}}| {{name}} | {{qty}} |\n{{/items}}",
            new Model("x", null, 0, [new("a", 1), new("b", 2)]));

        output.Should().Be("| Item | Qty |\n|---|---|\n| a | 1 |\n| b | 2 |\n");
    }

    [Fact]
    public void EmptySectionsRenderNothing()
        => Render("[{{#items}}x{{/items}}]", new Model("x", null, 0, []))
            .Should().Be("[]");

    [Fact]
    public void InvertedSectionsRenderWhenFalsy()
        => Render("{{^items}}none{{/items}}", new Model("x", null, 0, []))
            .Should().Be("none");

    [Fact]
    public void InvertedSectionsAreSkippedWhenTruthy()
        => Render("{{^items}}none{{/items}}", new Model("x", null, 0, [new("a", 1)]))
            .Should().BeEmpty();

    [Fact]
    public void NonCollectionTruthyValuesEnterTheSectionOnce()
        => Render("{{#note}}[{{.}}]{{/note}}", new Model("x", "here", 0, []))
            .Should().Be("[here]");

    [Fact]
    public void HelperOutputIsEscapedLikeAnInterpolation()
        => Render("| {{upper title}} |", new Model("a | b", null, 0, []))
            .Should().Be(@"| A \| B |");

    [Fact]
    public void ChildScopeFallsBackToTheParent()
        => Render("{{#items}}{{title}}-{{name}};{{/items}}",
                new Model("T", null, 0, [new("a", 1)]))
            .Should().Be("T-a;");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateRendererTests`
Expected: compilation failure — `TemplateRenderer` does not exist.

- [ ] **Step 3: Write `RenderedDocument`**

`src/DrMoustache.Core/Rendering/RenderedDocument.cs`:

```csharp
namespace DrMoustache.Core.Rendering;

/// <param name="Path">Relative output path, assigned by the delivery planner.</param>
/// <remarks>Documents are produced whole, in memory. Nothing reaches disk until
/// a document is complete, so a mid-render failure leaves no partial file.</remarks>
public sealed record RenderedDocument(string Path, string Content);
```

- [ ] **Step 4: Write `TemplateRenderer`**

`src/DrMoustache.Core/Rendering/TemplateRenderer.cs`:

```csharp
namespace DrMoustache.Core.Rendering;

using System.Globalization;
using System.Text;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Escaping;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Template;

/// <summary>Binds a node tree to a value and escapes each interpolation
/// according to the Markdown context captured at lex time.</summary>
public static class TemplateRenderer
{
    /// <summary>Refers to the current scope itself, for use inside a section
    /// over scalars.</summary>
    private const string SelfPath = ".";

    public static string Render(IReadOnlyList<TemplateNode> nodes, object? context)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var sb = new StringBuilder();
        var scopes = new List<object?> { context };
        Write(nodes, scopes, sb);
        return sb.ToString();
    }

    private static void Write(
        IReadOnlyList<TemplateNode> nodes, List<object?> scopes, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    sb.Append(text.Text);
                    break;

                case RawNode raw:
                    sb.Append(Stringify(Lookup(scopes, raw.Path)));
                    break;

                case InterpolationNode interp:
                    sb.Append(MarkdownEscaper.Escape(
                        Stringify(Lookup(scopes, interp.Path)), interp.Context));
                    break;

                case HelperNode helper:
                    sb.Append(MarkdownEscaper.Escape(
                        HelperRegistry.Apply(
                            helper.Helper, Lookup(scopes, helper.Path),
                            helper.Argument, helper.Line),
                        helper.Context));
                    break;

                case SectionNode section:
                    WriteSection(section, scopes, sb);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"unhandled node type {node.GetType().Name}");
            }
        }
    }

    private static void WriteSection(
        SectionNode section, List<object?> scopes, StringBuilder sb)
    {
        var value = Lookup(scopes, section.Path);
        var truthy = ValueResolver.IsTruthy(value);

        if (section.Inverted)
        {
            if (!truthy) Write(section.Children, scopes, sb);
            return;
        }

        if (!truthy) return;

        if (ValueResolver.AsSequence(value) is { } sequence)
        {
            foreach (var item in sequence)
            {
                scopes.Add(item);
                Write(section.Children, scopes, sb);
                scopes.RemoveAt(scopes.Count - 1);
            }

            return;
        }

        scopes.Add(value);
        Write(section.Children, scopes, sb);
        scopes.RemoveAt(scopes.Count - 1);
    }

    /// <summary>Walks scopes innermost-first, so a child scope shadows its
    /// parent but the parent stays reachable.</summary>
    private static object? Lookup(List<object?> scopes, string path)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            if (string.Equals(path, SelfPath, StringComparison.Ordinal))
                return scopes[i];

            if (ValueResolver.TryResolve(scopes[i], path, out var value))
                return value;
        }

        return null;
    }

    private static string Stringify(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateRendererTests`
Expected: 15 tests passed.

`SectionsProduceOneTableRowPerItem` is the integration point between the lexer's standalone-tag rule and the scanner's table detection. If it fails, re-check Task 5 Step 5 and Task 7 Step 6 before changing the renderer.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Core/Rendering tests/DrMoustache.Core.Tests/Rendering
git commit -m "$(cat <<'EOF'
Add template renderer

Escapes each interpolation using the context captured at lex time. Triple
braces bypass escaping; helper output does not.

Scope lookup walks innermost-first so a section's children shadow the
parent without losing access to it.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 12: Delivery planner, template validator, and architecture test

**Files:**
- Create: `src/DrMoustache.Core/Delivery/Slug.cs`
- Create: `src/DrMoustache.Core/Delivery/DeliveryPlanner.cs`
- Create: `src/DrMoustache.Core/Validation/TemplateValidator.cs`
- Test: `tests/DrMoustache.Core.Tests/Delivery/DeliveryPlannerTests.cs`
- Test: `tests/DrMoustache.Core.Tests/Validation/TemplateValidatorTests.cs`
- Test: `tests/DrMoustache.Core.Tests/ArchitectureTests.cs`

**Interfaces:**
- Consumes: `RenderConfig`, `DeliveryMode`, `CollisionPolicy` (Task 8); `TemplateRenderer`, `RenderedDocument` (Task 11); `TemplateLexer`, node types (Task 7); `RecordManifest` (Task 3).
- Produces:
  - `static string Slug.Make(string value)`
  - `sealed record DeliveryResult(IReadOnlyList<RenderedDocument> Documents, IReadOnlyList<string> Warnings)`
  - `static DeliveryResult DeliveryPlanner.Plan(RenderConfig config, IReadOnlyList<TemplateNode> outputPattern, IReadOnlyList<TemplateNode> body, string passthrough, IReadOnlyList<object> records)`
  - `static IReadOnlyList<string> TemplateValidator.Validate(IReadOnlyList<TemplateNode> nodes, RecordManifest manifest)`

- [ ] **Step 1: Write the failing delivery test**

`tests/DrMoustache.Core.Tests/Delivery/DeliveryPlannerTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DeliveryPlannerTests`
Expected: compilation failure — the `Delivery` namespace does not exist.

- [ ] **Step 3: Write `Slug`**

`src/DrMoustache.Core/Delivery/Slug.cs`:

```csharp
namespace DrMoustache.Core.Delivery;

using System.Text;

/// <summary>Makes a value safe to use as a path segment.</summary>
public static class Slug
{
    public static string Make(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var c in value.Trim())
        {
            if (char.IsWhiteSpace(c) || c is '/' or '\\' or '_')
            {
                pendingSeparator = sb.Length > 0;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '-' or '.')
            {
                if (pendingSeparator)
                {
                    sb.Append('-');
                    pendingSeparator = false;
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            // Everything else — ':' '*' '?' '"' '<' '>' '|' and control
            // characters — is dropped without leaving a separator.
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Write `DeliveryPlanner`**

`src/DrMoustache.Core/Delivery/DeliveryPlanner.cs`:

```csharp
namespace DrMoustache.Core.Delivery;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Rendering;
using DrMoustache.Core.Template;

public sealed record DeliveryResult(
    IReadOnlyList<RenderedDocument> Documents,
    IReadOnlyList<string> Warnings);

/// <summary>A fault while distributing records across output paths.</summary>
public sealed class DeliveryException : Exception
{
    public DeliveryException() { }

    public DeliveryException(string message) : base(message) { }

    public DeliveryException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Distributes records across output documents.</summary>
public static class DeliveryPlanner
{
    public static DeliveryResult Plan(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(records);

        return config.Delivery == DeliveryMode.Collate
            ? Collate(config, body, passthrough, records)
            : Fanout(config, outputPattern, body, passthrough, records);
    }

    private static DeliveryResult Collate(
        RenderConfig config, IReadOnlyList<TemplateNode> body,
        string passthrough, IReadOnlyList<object> records)
    {
        var root = new CollateScope(records.Count, records);
        var content = Compose(body, passthrough, root);

        return new DeliveryResult([new RenderedDocument(config.Output, content)], []);
    }

    private static DeliveryResult Fanout(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records)
    {
        var policy = config.OnCollision
            ?? throw new DeliveryException(
                "'onCollision' is required when delivery is 'fanout'");

        var byPath = new Dictionary<string, List<(object Record, string Content)>>(
            StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var record in records)
        {
            var path = SlugifyPath(TemplateRenderer.Render(outputPattern, record));

            if (!byPath.TryGetValue(path, out var bucket))
            {
                bucket = [];
                byPath[path] = bucket;
                order.Add(path);
            }

            bucket.Add((record, Compose(body, passthrough, record)));
        }

        var documents = new List<RenderedDocument>();
        var warnings = new List<string>();

        foreach (var path in order)
        {
            var bucket = byPath[path];

            if (bucket.Count == 1)
            {
                documents.Add(new RenderedDocument(path, bucket[0].Content));
                continue;
            }

            switch (policy)
            {
                case CollisionPolicy.Error:
                    throw new DeliveryException(
                        $"output collision: '{path}' produced by "
                        + $"{bucket.Count} records. Add a more distinguishing field "
                        + "to 'output', or set 'onCollision'.");

                case CollisionPolicy.Overwrite:
                    documents.Add(new RenderedDocument(path, bucket[^1].Content));
                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "kept the last (onCollision: overwrite)");
                    break;

                case CollisionPolicy.Ordinal:
                    for (var i = 0; i < bucket.Count; i++)
                        documents.Add(new RenderedDocument(
                            i == 0 ? path : Insert(path, $"-{i + 1}"), bucket[i].Content));

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "numbered by arrival order (onCollision: ordinal)");
                    break;

                case CollisionPolicy.Suffix:
                    foreach (var (record, content) in bucket)
                        documents.Add(new RenderedDocument(
                            Insert(path, "." + ShortHash(record)), content));

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "disambiguated by record hash (onCollision: suffix)");
                    break;

                default:
                    throw new DeliveryException($"unhandled collision policy '{policy}'");
            }
        }

        return new DeliveryResult(documents, warnings);
    }

    private static string Compose(
        IReadOnlyList<TemplateNode> body, string passthrough, object? scope)
    {
        var content = TemplateRenderer.Render(body, scope);

        if (string.IsNullOrWhiteSpace(passthrough)) return content;

        var frontmatter = TemplateRenderer.Render(
            TemplateLexer.Lex(passthrough), scope);

        return $"---\n{frontmatter.TrimEnd('\n')}\n---\n{content}";
    }

    private static string SlugifyPath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Slug.Make));

    private static string Insert(string path, string suffix)
    {
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');

        return dot > slash && dot >= 0
            ? string.Concat(path.AsSpan(0, dot), suffix, path.AsSpan(dot))
            : path + suffix;
    }

    /// <summary>Six hex characters of SHA-256 over the record's string form.
    /// Deterministic across runs, so filenames do not churn when the provider
    /// changes its ordering.</summary>
    private static string ShortHash(object record)
    {
        var text = record.ToString() ?? "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return string.Concat(bytes.Take(3)
            .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>Root scope for collate delivery.</summary>
    private sealed record CollateScope(int Count, IReadOnlyList<object> Records);
}
```

`CollateScope` exposes `Count` and `Records`, which `ValueResolver` finds case-insensitively as `{{count}}` and `{{#records}}`.

`ShortHash` relies on `record.ToString()`, which C# `record` types synthesise to include all public properties. A provider using a plain `class` should override `ToString()`; note this in `docs/plugin-architecture.md`.

- [ ] **Step 5: Run the delivery tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DeliveryPlannerTests`
Expected: 16 tests passed.

- [ ] **Step 6: Write the validator test**

`tests/DrMoustache.Core.Tests/Validation/TemplateValidatorTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Validation;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;
using DrMoustache.Core.Validation;
using FluentAssertions;
using Xunit;

public class TemplateValidatorTests
{
    private sealed record Inner
    {
        [Field] public required string Id { get; init; }
    }

    [DrMoustacheRecord("thing")]
    private sealed record Thing
    {
        [Field("the customer-facing order number")]
        public required string OrderId { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public string? Note { get; init; }
        [Field] public required Inner Order { get; init; }
        [Field] public IReadOnlyList<Inner> Items { get; init; } = [];
    }

    private static IReadOnlyList<string> Validate(string template)
        => TemplateValidator.Validate(
            TemplateLexer.Lex(template), ManifestBuilder.FromType<Thing>());

    [Fact]
    public void AcceptsAValidTemplate()
        => Validate("{{orderId}} {{order.id}} {{#items}}{{id}}{{/items}}")
            .Should().BeEmpty();

    [Fact]
    public void RejectsUnknownFieldsAndSuggestsANearMiss()
        => Validate("{{orderid}}").Should().ContainSingle()
            .Which.Should().Contain("orderid")
            .And.Contain("did you mean 'orderId'")
            .And.Contain("the customer-facing order number");

    [Fact]
    public void RejectsDirectObjectInterpolation()
        => Validate("{{order}}").Should().ContainSingle()
            .Which.Should().Contain("is an object")
            .And.Contain("{{order.id}}");

    [Fact]
    public void RejectsUnknownHelpers()
        => Validate("{{frobnicate orderId}}").Should().ContainSingle()
            .Which.Should().Contain("frobnicate");

    [Fact]
    public void RejectsDateHelperOnNonTemporalFields()
        => Validate("""{{date orderId "yyyy"}}""").Should().ContainSingle()
            .Which.Should().Contain("date").And.Contain("orderId");

    [Fact]
    public void AllowsRawTagsWithoutTypeChecking()
        => Validate("{{{order}}}").Should().BeEmpty();

    [Fact]
    public void ResolvesFieldsInsideCollectionSections()
        => Validate("{{#items}}{{id}}{{/items}}").Should().BeEmpty();

    [Fact]
    public void RejectsUnknownFieldsInsideSections()
        => Validate("{{#items}}{{nope}}{{/items}}").Should().ContainSingle();

    [Fact]
    public void AllowsDotSelfReference()
        => Validate("{{#note}}{{.}}{{/note}}").Should().BeEmpty();
}
```

- [ ] **Step 7: Write `TemplateValidator`**

`src/DrMoustache.Core/Validation/TemplateValidator.cs`:

```csharp
namespace DrMoustache.Core.Validation;

using DrMoustache.Core.Helpers;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;

/// <summary>Checks a template against a manifest without reading any data.
/// Backs the CLI's <c>check</c> verb.</summary>
public static class TemplateValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<TemplateNode> nodes, RecordManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        Walk(nodes, manifest, prefix: null, errors);
        return errors;
    }

    private static void Walk(
        IReadOnlyList<TemplateNode> nodes, RecordManifest manifest,
        string? prefix, List<string> errors)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case InterpolationNode i:
                    CheckInterpolation(i.Path, i.Line, manifest, prefix, errors);
                    break;

                case HelperNode h:
                    CheckHelper(h, manifest, prefix, errors);
                    break;

                case SectionNode s:
                    if (Resolve(manifest, prefix, s.Path) is null)
                        errors.Add(Unknown(s.Path, s.Line, manifest, prefix));
                    else
                        Walk(s.Children, manifest, Combine(prefix, s.Path), errors);
                    break;

                // RawNode bypasses escaping and type checking by design.
                default:
                    break;
            }
        }
    }

    private static void CheckInterpolation(
        string path, int line, RecordManifest manifest,
        string? prefix, List<string> errors)
    {
        if (path == ".") return;

        var field = Resolve(manifest, prefix, path);

        if (field is null)
        {
            errors.Add(Unknown(path, line, manifest, prefix));
            return;
        }

        if (field.Type is FieldType.Object)
            errors.Add(
                $"cannot interpolate '{path}' directly (line {line})\n"
                + $"       '{path}' is an object — reference a field:  "
                + string.Join(", ", field.Children.Take(3)
                    .Select(c => $"{{{{{path}.{c.Path}}}}}")));

        if (field.Type is FieldType.Collection)
            errors.Add(
                $"cannot interpolate '{path}' directly (line {line})\n"
                + $"       '{path}' is a collection — use a section:  "
                + $"{{{{#{path}}}}}…{{{{/{path}}}}}");
    }

    private static void CheckHelper(
        HelperNode helper, RecordManifest manifest, string? prefix, List<string> errors)
    {
        if (!HelperRegistry.IsKnown(helper.Helper))
        {
            errors.Add(
                $"unknown helper '{helper.Helper}' (line {helper.Line})\n"
                + "       available: date, format, upper, lower, join, default");
            return;
        }

        var field = Resolve(manifest, prefix, helper.Path);

        if (field is null)
        {
            errors.Add(Unknown(helper.Path, helper.Line, manifest, prefix));
            return;
        }

        var mismatch = helper.Helper switch
        {
            "date" when field.Type is not FieldType.Temporal => "a temporal field",
            "format" when field.Type is not (FieldType.Integer or FieldType.Decimal)
                => "a numeric field",
            "join" when field.Type is not FieldType.Collection => "a collection",
            _ => null,
        };

        if (mismatch is not null)
            errors.Add(
                $"'{helper.Helper}' needs {mismatch}, but '{helper.Path}' is "
                + $"{field.Type} (line {helper.Line})");
    }

    private static ManifestField? Resolve(
        RecordManifest manifest, string? prefix, string path)
    {
        if (manifest.TryResolve(Combine(prefix, path), out var scoped)) return scoped;
        if (manifest.TryResolve(path, out var root)) return root;
        return null;
    }

    private static string Combine(string? prefix, string path) =>
        prefix is null ? path : $"{prefix}.{path}";

    private static string Unknown(
        string path, int line, RecordManifest manifest, string? prefix)
    {
        var message =
            $"unknown field '{path}' (line {line})\n"
            + $"       record '{manifest.Name}' has no such field";

        var suggestion = manifest.AllPaths()
            .Select(p => (Path: p, Distance: Distance(Leaf(p), Leaf(path))))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .Select(x => x.Path)
            .FirstOrDefault();

        if (suggestion is null) return message;

        manifest.TryResolve(suggestion, out var field);
        var description = field?.Description is { } d ? $"  ({d})" : "";

        return $"{message}\n       did you mean '{suggestion}'?{description}";
    }

    private static string Leaf(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    /// <summary>Levenshtein distance, case-insensitive, for near-miss suggestions.</summary>
    private static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
```

- [ ] **Step 8: Run the validator tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateValidatorTests`
Expected: 9 tests passed.

- [ ] **Step 9: Write the architecture test**

`tests/DrMoustache.Core.Tests/ArchitectureTests.cs`:

```csharp
namespace DrMoustache.Core.Tests;

using System.Reflection;
using FluentAssertions;
using Xunit;

public class ArchitectureTests
{
    private static Assembly Core => typeof(DrMoustache.Core.Escaping.MarkdownEscaper).Assembly;

    [Theory]
    [InlineData("System.Net")]
    [InlineData("System.Net.Http")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Azure.Messaging.ServiceBus")]
    [InlineData("Azure.Core")]
    public void CoreReferencesNoIoOrTransportAssembly(string forbidden)
        => Core.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(forbidden);

    [Theory]
    [InlineData("System.IO")]
    [InlineData("File.")]
    [InlineData("Directory.")]
    public void CoreSourcesDoNotTouchTheFilesystem(string forbidden)
    {
        var offenders = CoreSourceFiles()
            .Where(f => File.ReadAllText(f).Contains(forbidden, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            $"Core performs no I/O, so '{forbidden}' must not appear in its sources");
    }

    // 'row' is deliberately absent from this list. TableRow is a legitimate
    // Markdown construct, not a transport concept -- do not "fix" the omission.
    [Theory]
    [InlineData("message")]
    [InlineData("queue")]
    [InlineData("endpoint")]
    public void CoreSourcesContainNoTransportNouns(string noun)
    {
        var offenders = CoreSourceFiles()
            .Where(f => File.ReadAllText(f).Contains(noun, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            $"'{noun}' is a transport concept and must not leak into Core");
    }

    private static IEnumerable<string> CoreSourceFiles()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(FindSourceRoot(), "src", "DrMoustache.Core"),
                "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty("the scan must actually find Core's sources");
        return files;
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }
}
```

The test project *may* touch `System.IO` — only `DrMoustache.Core` is constrained.

Note the `CoreSourceFiles` helper asserts the scan is non-empty. Without that guard, a wrong path would make every architecture test pass vacuously — which is exactly the failure mode these tests exist to prevent.

The reasoning for excluding `row` is carried as a comment in the test file itself, so a later reader does not "fix" the omission.

- [ ] **Step 10: Run the full suite**

Run: `dotnet test DrMoustache.slnx`
Expected: all tests pass; zero build warnings.

If `CoreSourcesContainNoTransportNouns` fails, rename the offending identifier rather than weakening the test — it is the guard on the boundary the whole design rests on.

- [ ] **Step 11: Commit**

```bash
git add src/DrMoustache.Core/Delivery src/DrMoustache.Core/Validation tests
git commit -m "$(cat <<'EOF'
Add delivery planner, template validator, and architecture test

Fanout and collate delivery with the four collision policies. Suffix hashes
the record so filenames stay stable when the provider changes ordering.

The validator backs `check`: it resolves every referenced field against the
manifest and offers a near-miss suggestion using the field description.

The architecture test guards the boundary the design rests on -- Core
references no transport assembly and its sources carry no transport nouns.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

## Self-Review

**1. Spec coverage.** Every section of the spec maps to a task:

| Spec section | Task |
|---|---|
| Provider contract, read-only | 2 |
| Manifest, attributed POCO, name mapping, what types do | 3, 4 |
| Template format, YAML leading-brace rule | 8 |
| Delivery: fanout, collate, `onCollision` required | 8, 12 |
| Escaping: contexts, rules table, overrides, standalone tags | 5, 6, 7 |
| Helpers, closed set | 10 |
| Errors: template-static vs record-level | 8, 12 (static); record-level collection is CLI-side |
| Testing: table-driven, round-trip, golden-file, architecture | 6, 12 |
| Repository conventions | 1 |

**Deferred to Plan 2 (CLI), and correctly so:** configuration chain and `scopeConfig`, plugin acquisition and `AssemblyLoadContext`, the `render`/`init`/`check` verbs, record-level error collection and `--strict`, file writing, golden-file fixtures that exercise the whole pipeline, and `docs/plugin-architecture.md`.

**2. Placeholder scan.** No `TBD`, `TODO`, or "add error handling" instructions. Every code step contains complete, compilable code.

**3. Type consistency.** Verified across tasks: `MarkdownContext` (5) is consumed by `MarkdownEscaper` (6), `InterpolationNode`/`HelperNode` (7), and `TemplateRenderer` (11) with the same casing. `RecordManifest.TryResolve` (3) is used by `ManifestBuilder` tests (4) and `TemplateValidator` (12) with matching `out ManifestField?` nullability. `RenderConfig` (8) is consumed by `DeliveryPlanner.Plan` (12) with matching property names. `ValueResolver.AsSequence` (9) is used by `HelperRegistry.Join` (10) and `TemplateRenderer.WriteSection` (11).

One genuine gap found and closed during review: `TemplateRenderer` needed a `{{.}}` self-reference for sections over scalars (Task 11 test `NonCollectionTruthyValuesEnterTheSectionOnce`), and `TemplateValidator` had to be taught to allow it (Task 12 test `AllowsDotSelfReference`). Both are now specified.
