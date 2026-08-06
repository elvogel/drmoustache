namespace DrMoustache.Tests.Discovery;

using System.Diagnostics.CodeAnalysis;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Discovery;
using FluentAssertions;
using Xunit;

[SuppressMessage("Design", "CA1063", Justification = "Test fixture; simplified disposal is appropriate")]
public class TemplateFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drm-disc-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string Valid = """
        ---
        drmoustache:
          source: stations
          delivery: fanout
          output: stations/{{name}}.md
          onCollision: error
        ---
        # {{name}}
        """;

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void FindsATemplateWithAReservedBlock()
    {
        Write("a.md", Valid);

        TemplateFile.Discover(_dir).Should().ContainSingle()
            .Which.Path.Should().EndWith("a.md");
    }

    [Fact]
    public void SkipsMarkdownWithNoReservedBlock()
    {
        Write("a.md", Valid);
        Write("hand-written.md", "# just a page");

        TemplateFile.Discover(_dir).Should().ContainSingle();
    }

    [Fact]
    public void SkipsPartialsDirectories()
    {
        Write("a.md", Valid);
        Write("_partials/p.md", Valid);
        Write("nested/_shared/q.md", Valid);

        TemplateFile.Discover(_dir).Should().ContainSingle();
    }

    [Fact]
    public void FindsTemplatesRecursively()
    {
        Write("nested/deep/a.md", Valid);

        TemplateFile.Discover(_dir).Should().ContainSingle();
    }

    [Fact]
    public void IgnoresNonMarkdownFiles()
    {
        Write("a.md", Valid);
        Write("notes.txt", Valid);

        TemplateFile.Discover(_dir).Should().ContainSingle();
    }

    [Fact]
    public void ReturnsTemplatesInAStableOrder()
    {
        Write("b.md", Valid);
        Write("a.md", Valid);
        Write("c.md", Valid);

        TemplateFile.Discover(_dir).Select(t => Path.GetFileName(t.Path))
            .Should().ContainInOrder("a.md", "b.md", "c.md");
    }

    [Fact]
    public void ExposesTheParsedConfiguration()
    {
        Write("a.md", Valid);

        var template = TemplateFile.Discover(_dir).Single();

        template.Parsed.Config.Source.Should().Be("stations");
        template.Parsed.Config.Delivery.Should().Be(DeliveryMode.Fanout);
    }

    [Fact]
    public void AMalformedTemplateIsReportedByPath()
    {
        Write("bad.md", """
            ---
            drmoustache:
              source: stations
              delivery: sideways
              output: x.md
            ---
            body
            """);

        var act = () => TemplateFile.Discover(_dir);

        act.Should().Throw<RenderException>()
            .WithMessage("*bad.md*")
            .And.Message.Should().Contain("sideways");
    }

    [Fact]
    public void AMissingDirectoryIsReportedByPath()
    {
        var missing = Path.Combine(_dir, "nope");

        var act = () => TemplateFile.Discover(missing);

        act.Should().Throw<RenderException>().WithMessage($"*{"nope"}*");
    }

    [Fact]
    public void AnEmptyDirectoryYieldsNothingAndDoesNotThrow()
        => TemplateFile.Discover(_dir).Should().BeEmpty();
}
