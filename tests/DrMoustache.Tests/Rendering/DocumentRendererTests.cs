namespace DrMoustache.Tests.Rendering;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using DrMoustache.Discovery;
using DrMoustache.Rendering;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class DocumentRendererTests : IDisposable
{
    private readonly string _templates = Directory.CreateTempSubdirectory("drm-r-t-").FullName;
    private readonly string _output = Directory.CreateTempSubdirectory("drm-r-o-").FullName;

    public void Dispose()
    {
        foreach (var d in new[] { _templates, _output })
        {
            try { Directory.Delete(d, recursive: true); }
            catch (IOException) { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    [DrMoustacheRecord("station")]
    private sealed record Station
    {
        [Field] public required string Name { get; init; }
        [Field] public required int Reading { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class StationProvider : DataProvider<Station>
    {
        internal static Station[] Rows { get; set; } =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = "beta", Reading = 2 }];

        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var row in Rows) yield return row;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class ThrowingProvider : DataProvider<Station>
    {
        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Station { Name = "good", Reading = 1 };
            throw new InvalidOperationException("the well ran dry");
        }
    }

    /// <summary>Dies before yielding anything, as an unreachable database would.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class DeadProvider : DataProvider<Station>
    {
        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw new InvalidOperationException("could not connect");
#pragma warning disable CS0162 // Unreachable: an iterator needs a yield to be one.
            yield break;
#pragma warning restore CS0162
        }
    }

    /// <summary>Produces one good record, one unusable one, then dies — so a
    /// single run carries both a skip and a fault.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class FaultAndSkipProvider : DataProvider<Station>
    {
        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Station { Name = "alpha", Reading = 1 };
            yield return new Station { Name = null!, Reading = 2 };
            throw new InvalidOperationException("the pipe burst");
        }
    }

    /// <summary>Counts how many times it was enumerated, to prove a source two
    /// templates share is drained exactly once.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class CountingProvider : DataProvider<Station>
    {
        internal int Reads { get; private set; }

        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            Reads++;
            yield return new Station { Name = "alpha", Reading = 1 };
            yield return new Station { Name = null!, Reading = 2 };
        }
    }

    private const string Fanout = """
        ---
        drmoustache:
          source: stations
          delivery: fanout
          output: stations/{{name}}.md
          onCollision: error
        title: "{{name}}"
        ---

        # {{name}}

        Reading: {{reading}}
        """;

    private void Write(string name, string content)
    {
        var path = Path.Combine(_templates, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private IDocumentRenderer Renderer<TProvider>(bool strict = false)
        where TProvider : class, IDataProvider
    {
        var services = new ServiceCollection();

        services.AddDrMoustache(o =>
        {
            o.TemplateDirectory = _templates;
            o.OutputDirectory = _output;
            o.Strict = strict;
        })
        .AddSource<TProvider>("stations");

        return services.BuildServiceProvider().GetRequiredService<IDocumentRenderer>();
    }

    [Fact]
    public async Task RendersOneDocumentPerRecord()
    {
        StationProvider.Rows =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = "beta", Reading = 2 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.DocumentsWritten.Should().Be(2);
        File.Exists(Path.Combine(_output, "stations", "alpha.md")).Should().BeTrue();
        File.Exists(Path.Combine(_output, "stations", "beta.md")).Should().BeTrue();
    }

    [Fact]
    public async Task WritesBodyAndPassthroughFrontmatter()
    {
        StationProvider.Rows = [new() { Name = "alpha", Reading = 42 }];
        Write("a.md", Fanout);

        await Renderer<StationProvider>().RenderAsync(TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(
            Path.Combine(_output, "stations", "alpha.md"),
            TestContext.Current.CancellationToken);

        text.Should().StartWith("---");
        text.Should().Contain("title: alpha");
        text.Should().Contain("# alpha");
        text.Should().Contain("Reading: 42");
    }

    [Fact]
    public async Task CollateProducesOneDocument()
    {
        StationProvider.Rows =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = "beta", Reading = 2 }];

        Write("all.md", """
            ---
            drmoustache:
              source: stations
              delivery: collate
              output: all.md
            ---
            # {{count}} stations

            {{#records}}- {{name}}
            {{/records}}
            """);

        var result = await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.DocumentsWritten.Should().Be(1);

        var text = await File.ReadAllTextAsync(
            Path.Combine(_output, "all.md"), TestContext.Current.CancellationToken);

        text.Should().Contain("# 2 stations").And.Contain("- alpha").And.Contain("- beta");
    }

    [Fact]
    public async Task AProviderFaultKeepsWhatWasAlreadyRead()
    {
        Write("a.md", Fanout);

        var result = await Renderer<ThrowingProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.DocumentsWritten.Should().Be(1);

        // A dead read is a Fault, not a RecordSkip: how many records were
        // never seen is unknown, so Strict has no say in whether it is fatal.
        result.Skips.Should().BeEmpty();
        result.Faults.Should().ContainSingle()
            .Which.Should().Contain("stations")
            .And.Contain("the well ran dry")
            .And.Contain("InvalidOperationException");

        File.Exists(Path.Combine(_output, "stations", "good.md")).Should().BeTrue();
    }

    [Fact]
    public async Task AProviderFaultFailsTheRunEvenWhenLenient()
    {
        // A scheduled job whose database was unreachable must not exit 0
        // having produced nothing. Strict governs skips; it does not govern
        // this.
        Write("a.md", Fanout);

        var result = await Renderer<DeadProvider>(strict: false)
            .RenderAsync(TestContext.Current.CancellationToken);

        result.DocumentsWritten.Should().Be(0);
        result.Faults.Should().ContainSingle();
        result.Success.Should().BeFalse(result.Summary);
    }

    [Fact]
    public async Task ARecordSkipAloneStillSucceedsWhenLenient()
    {
        // The counterpart to the test above: Strict still means what it meant.
        StationProvider.Rows =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = null!, Reading = 2 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>(strict: false)
            .RenderAsync(TestContext.Current.CancellationToken);

        result.Skips.Should().ContainSingle();
        result.Faults.Should().BeEmpty();
        result.Success.Should().BeTrue(result.Summary);
    }

    [Fact]
    public async Task SummaryDistinguishesAFaultFromASkip()
    {
        Write("a.md", Fanout);

        var result = await Renderer<FaultAndSkipProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.Skips.Should().ContainSingle();
        result.Faults.Should().ContainSingle();

        var summary = result.Summary;

        summary.Should().Contain("1 skipped").And.Contain("1 fault");
        summary.Should().Contain("FAULT").And.Contain("the pipe burst");
        summary.Should().Contain("skipped  stations record 1");
    }

    [Fact]
    public async Task ARecordViolatingItsManifestIsSkipped()
    {
        StationProvider.Rows =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = null!, Reading = 2 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.DocumentsWritten.Should().Be(1);
        result.Skips.Should().ContainSingle()
            .Which.Reason.Should().Contain("name").And.Contain("required");
    }

    [Fact]
    public async Task StrictFailsOnASkipButLenientDoesNot()
    {
        StationProvider.Rows = [new() { Name = null!, Reading = 1 }];
        Write("a.md", Fanout);

        (await Renderer<StationProvider>(strict: false)
            .RenderAsync(TestContext.Current.CancellationToken))
            .Success.Should().BeTrue();

        (await Renderer<StationProvider>(strict: true)
            .RenderAsync(TestContext.Current.CancellationToken))
            .Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnregisteredSourceIsReportedWithTheRegisteredOnes()
    {
        Write("a.md", Fanout.Replace("source: stations", "source: nowhere",
            StringComparison.Ordinal));

#pragma warning disable CA2007 // Lambda in test context; xUnit1030 forbids ConfigureAwait(false) in test methods
        var act = async () => await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);
#pragma warning restore CA2007

        (await act.Should().ThrowAsync<RenderException>())
            .WithMessage("*nowhere*")
            .And.Message.Should().Contain("stations");
    }

    [Fact]
    public async Task CheckValidatesWithoutReadingOrWriting()
    {
        StationProvider.Rows = [new() { Name = "alpha", Reading = 1 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.TemplatesChecked.Should().Be(1);
        Directory.EnumerateFiles(_output, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task CheckReportsAnUnknownField()
    {
        Write("a.md", Fanout.Replace("{{reading}}", "{{readign}}", StringComparison.Ordinal));

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Problems.Should().ContainSingle()
            .Which.Should().Contain("readign").And.Contain("a.md");
    }

    [Fact]
    public async Task CheckReportsAMalformedOutputPattern()
    {
        // The output pattern is a template too, and check must lex it. Without
        // this, a malformed pattern passes check and then fails the render —
        // which is exactly the false green check exists to prevent.
        Write("a.md", Fanout.Replace(
            "output: stations/{{name}}.md",
            "output: stations/{{#name}}.md",
            StringComparison.Ordinal));

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Problems.Should().ContainSingle()
            .Which.Should().Contain("a.md").And.Contain("not closed");
    }

    [Fact]
    public async Task CheckReportsAnUnknownFieldInTheOutputPattern()
    {
        Write("a.md", Fanout.Replace(
            "output: stations/{{name}}.md",
            "output: stations/{{naem}}.md",
            StringComparison.Ordinal));

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Problems.Should().ContainSingle()
            .Which.Should().Contain("a.md").And.Contain("naem");
    }

    [Fact]
    public async Task CheckAcceptsACollateTemplate()
    {
        // A collate template validated as fanout reports 'count' and 'records'
        // as unknown fields and never descends into the loop body. This is the
        // regression guard for known-issues R5.
        Write("all.md", """
            ---
            drmoustache:
              source: stations
              delivery: collate
              output: all.md
            ---
            # {{count}} stations

            {{#records}}- {{name}}
            {{/records}}
            """);

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.Summary);
    }

    [Fact]
    public async Task RenderFailsOnAnInvalidTemplateBeforeReadingAnyData()
    {
        Write("a.md", Fanout.Replace("{{reading}}", "{{readign}}", StringComparison.Ordinal));

#pragma warning disable CA2007 // Lambda in test context; xUnit1030 forbids ConfigureAwait(false) in test methods
        var act = async () => await Renderer<ThrowingProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);
#pragma warning restore CA2007

        // ThrowingProvider would throw if it were read. It must not be.
        (await act.Should().ThrowAsync<RenderException>())
            .And.Message.Should().Contain("readign");
    }

    [Fact]
    public async Task RenderResultSummaryReportsDocumentsAndSkips()
    {
        StationProvider.Rows =
            [new() { Name = "alpha", Reading = 1 }, new() { Name = null!, Reading = 2 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.Summary.Should().Contain("1 document")
            .And.Contain("1 skipped")
            .And.Contain("stations")
            .And.Contain("required");
    }

    [Fact]
    public async Task RenderResultSummaryMentionsNoSkipsWhenThereAreNone()
    {
        StationProvider.Rows = [new() { Name = "alpha", Reading = 1 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        result.Summary.Should().Contain("1 document").And.NotContain("skipped");
    }

    [Fact]
    public async Task CheckResultSummaryReportsNoProblemsWhenClean()
    {
        StationProvider.Rows = [new() { Name = "alpha", Reading = 1 }];
        Write("a.md", Fanout);

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Summary.Should().Contain("1 template").And.Contain("no problems found");
    }

    [Fact]
    public async Task CheckResultSummaryListsEachProblem()
    {
        Write("a.md", Fanout.Replace("{{reading}}", "{{readign}}", StringComparison.Ordinal));

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Summary.Should().Contain("1 problem")
            .And.Contain("readign")
            .And.Contain("a.md");
    }

    [Fact]
    public async Task CheckReportsAnUnclosedSectionAgainstItsFile()
    {
        // The commonest authoring mistake there is, and --check is the
        // advertised way to catch it. Before, the body lex threw out of
        // CheckAsync carrying no path, so a directory of templates had to be
        // bisected by hand.
        Write("broken.md", """
            ---
            drmoustache:
              source: stations
              delivery: collate
              output: all.md
            ---
            {{#records}}- {{name}}
            """);

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Problems.Should().ContainSingle()
            .Which.Should().Contain("broken.md").And.Contain("not closed");
    }

    [Fact]
    public async Task CheckReportsAnUnregisteredSourceRatherThanThrowing()
    {
        // Reporting problems is CheckResult's entire contract, and a
        // misconfigured 'source:' is the definitive check-time problem.
        Write("a.md", Fanout.Replace("source: stations", "source: nowhere",
            StringComparison.Ordinal));

        var result = await Renderer<StationProvider>()
            .CheckAsync(TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Problems.Should().ContainSingle()
            .Which.Should().Contain("a.md")
            .And.Contain("nowhere")
            .And.Contain("stations");
    }

    [Fact]
    public async Task TwoTemplatesSharingASourceReadItOnce()
    {
        // Draining per template read a shared source twice: double the network
        // or database cost, skips counted twice, and — for a provider whose
        // sequence is not stable across calls — two templates rendered from
        // different data. The shipped sample has exactly this shape.
        Write("a.md", Fanout);
        Write("all.md", """
            ---
            drmoustache:
              source: stations
              delivery: collate
              output: all.md
            ---
            # {{count}} stations

            {{#records}}- {{name}}
            {{/records}}
            """);

        var services = new ServiceCollection();

        services.AddDrMoustache(o =>
        {
            o.TemplateDirectory = _templates;
            o.OutputDirectory = _output;
        })
        .AddSource<CountingProvider>("stations");

        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDocumentRenderer>()
            .RenderAsync(TestContext.Current.CancellationToken);

        var counting = (CountingProvider)provider
            .GetRequiredKeyedService<IDataProvider>("stations");

        counting.Reads.Should().Be(1, "a source two templates share is drained once");

        // Both templates still rendered from that one read.
        result.DocumentsWritten.Should().Be(2);

        // And the one unusable record is reported once, not once per template.
        result.Skips.Should().ContainSingle()
            .Which.Reason.Should().Contain("required");
    }
}
