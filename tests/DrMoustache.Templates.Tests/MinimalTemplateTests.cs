namespace DrMoustache.Templates.Tests;

using FluentAssertions;
using Xunit;

public class MinimalTemplateTests(TemplateFixture fixture) : IClassFixture<TemplateFixture>
{
    [Fact]
    public void ScaffoldsTheExpectedFiles()
    {
        var project = fixture.Instantiate("drmoustache", "MinScaffold");

        File.Exists(Path.Combine(project, "MinScaffold.csproj")).Should().BeTrue(
            "sourceName must rename the project file");
        File.Exists(Path.Combine(project, "Program.cs")).Should().BeTrue();
        File.Exists(Path.Combine(project, "MyProvider.cs")).Should().BeTrue();
        File.Exists(Path.Combine(project, "templates", "report.md")).Should().BeTrue();
        File.Exists(Path.Combine(project, "appsettings.json")).Should().BeTrue();
    }

    [Fact]
    public void RenamesTheNamespaceToo()
    {
        var project = fixture.Instantiate("drmoustache", "MinNamespace");

        File.ReadAllText(Path.Combine(project, "Program.cs"))
            .Should().Contain("namespace MinNamespace")
            .And.NotContain("DrMoustache.Minimal");
    }

    [Fact]
    public void BuildsAndRunsAndProducesADocument()
    {
        var project = fixture.Instantiate("drmoustache", "MinRun");

        var (exitCode, output) = TemplateFixture.TryRun("dotnet", ["run"], project);

        exitCode.Should().Be(0, output);
        output.Should().Contain("rendered 1 document");

        var rendered = Path.Combine(project, "dist", "reports", "sample-1.md");
        File.Exists(rendered).Should().BeTrue(output);

        var text = File.ReadAllText(rendered);
        text.Should().StartWith("---");
        text.Should().Contain("title: Placeholder");
        text.Should().Contain("# Placeholder");
        text.Should().Contain("Value: 42");
    }

    [Fact]
    public void CheckPasses()
    {
        var project = fixture.Instantiate("drmoustache", "MinCheck");

        var (exitCode, output) = TemplateFixture.TryRun(
            "dotnet", ["run", "--", "--check"], project);

        exitCode.Should().Be(0, output);
        output.Should().Contain("no problems found");
    }

    [Fact]
    public void ReferencesTheVersionThisRepositoryActuallyPacks()
    {
        // The scaffold names its DrMoustache version literally, and nothing
        // forces it to agree with Directory.Build.props. When they drift, a
        // fresh restore fails for strangers while still working for anyone
        // with the old version cached -- silent and environment-dependent.
        var root = TemplateFixture.RepositoryRoot;

        var declared = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(Path.Combine(root, "Directory.Build.props")),
            @"<Version>([^<]+)</Version>").Groups[1].Value;

        declared.Should().NotBeNullOrWhiteSpace("Directory.Build.props must set <Version>");

        var scaffold = File.ReadAllText(Path.Combine(
            root, "templates", "content", "DrMoustache.Minimal",
            "DrMoustache.Minimal.csproj"));

        scaffold.Should().Contain($"Include=\"DrMoustache\" Version=\"{declared}\"",
            "the scaffold must reference the version this repository packs");
    }
}
