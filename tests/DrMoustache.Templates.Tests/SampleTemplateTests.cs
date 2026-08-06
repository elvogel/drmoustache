namespace DrMoustache.Templates.Tests;

using FluentAssertions;
using Xunit;

public class SampleTemplateTests(TemplateFixture fixture) : IClassFixture<TemplateFixture>
{
    [Fact]
    public void BuildsAndRuns()
    {
        var project = fixture.Instantiate("drmoustache-sample", "SampleRun");

        var (exitCode, output) = TemplateFixture.TryRun("dotnet", ["run"], project);

        exitCode.Should().Be(0, output);
        output.Should().Contain("rendered 4 documents");
    }

    [Fact]
    public void FansOutOnePageAndDisambiguatesTheCollision()
    {
        var project = fixture.Instantiate("drmoustache-sample", "SampleFanout");

        var (exitCode, output) = TemplateFixture.TryRun("dotnet", ["run"], project);
        exitCode.Should().Be(0, output);

        output.Should().Contain("collision",
            "two beta rows share a station name, and a resolved collision must warn");

        var stations = Directory.EnumerateFiles(
            Path.Combine(project, "dist", "stations")).ToList();

        stations.Should().HaveCount(3, "one document per record, none lost to the collision");
    }

    [Fact]
    public void TheCollatedTableSurvivesHostileData()
    {
        var project = fixture.Instantiate("drmoustache-sample", "SampleTable");

        var (exitCode, output) = TemplateFixture.TryRun("dotnet", ["run"], project);
        exitCode.Should().Be(0, output);

        var summary = File.ReadAllText(Path.Combine(project, "dist", "summary.md"));

        summary.Should().Contain("# 3 observations");

        // The beta note is 'sensor drift | readings *suspect*'. Both the pipe
        // and the asterisks must survive as literal characters: the pipe
        // escaped so it cannot split the row, the asterisks escaped so data
        // cannot inject emphasis.
        summary.Should().Contain(@"\|", "the note's pipe must be escaped");
        summary.Should().Contain(@"\*", "the note's asterisks must be escaped");

        var rows = summary.Split('\n')
            .Where(l => l.TrimStart().StartsWith('|'))
            .ToList();

        // Header, delimiter, three data rows.
        rows.Should().HaveCount(5, "an unescaped pipe would have split a row in two");
    }

    [Fact]
    public void CheckPassesOnBothTemplatesIncludingTheCollateOne()
    {
        var project = fixture.Instantiate("drmoustache-sample", "SampleCheck");

        var (exitCode, output) = TemplateFixture.TryRun(
            "dotnet", ["run", "--", "--check"], project);

        // summary.md is a collate template. If the renderer validated it as
        // fanout it would report 'count' and 'records' as unknown fields.
        exitCode.Should().Be(0, output);
        output.Should().Contain("checked 2 templates");
    }
}
