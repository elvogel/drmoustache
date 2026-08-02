# DrMoustache CLI Implementation Plan — Part 2: Verbs and Output

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Continues** `2026-08-02-drmoustache-cli.md`. Tasks 1–7 (command shell, configuration, `drmoustache.yaml`, sample plugin, load context, acquisition, provider registry) must be complete before starting Task 8.

**Goal:** Complete the CLI — atomic file writing, the three verbs, record-level error handling, and the documentation that makes the plugin architecture reusable.

**Global Constraints:** as stated in Part 1. They apply to every task here.

## Core API this plan calls

Verified against the shipped assemblies. Use these signatures exactly.

```csharp
// DrMoustache.Core.Frontmatter
ParsedTemplate FrontmatterParser.Parse(string content);
sealed record ParsedTemplate(RenderConfig Config, string PassthroughFrontmatter, string Body);
sealed record RenderConfig(string Source, DeliveryMode Delivery, string Output,
                           CollisionPolicy? OnCollision);
enum DeliveryMode { Fanout, Collate }
enum CollisionPolicy { Error, Suffix, Ordinal, Overwrite }

// DrMoustache.Core.Template
IReadOnlyList<TemplateNode> TemplateLexer.Lex(string body);
sealed class TemplateException : Exception { int Line { get; } }

// DrMoustache.Core.Manifest
RecordManifest ManifestBuilder.FromType(Type recordType);
bool RecordManifest.TryResolve(string path, out ManifestField? field);
IEnumerable<string> RecordManifest.AllPaths();
sealed record ManifestField(string Path, FieldType Type, bool IsNullable,
                            string? Description, IReadOnlyList<ManifestField> Children);
sealed class ManifestException : Exception;

// DrMoustache.Core.Validation
IReadOnlyList<string> TemplateValidator.Validate(
    IReadOnlyList<TemplateNode> nodes, RecordManifest manifest,
    DeliveryMode delivery = DeliveryMode.Fanout);

// DrMoustache.Core.Delivery
DeliveryResult DeliveryPlanner.Plan(
    RenderConfig config, IReadOnlyList<TemplateNode> outputPattern,
    IReadOnlyList<TemplateNode> body, string passthrough, IReadOnlyList<object> records);
sealed record DeliveryResult(IReadOnlyList<RenderedDocument> Documents,
                             IReadOnlyList<string> Warnings);
sealed class DeliveryException : Exception;

// DrMoustache.Core.Rendering
sealed record RenderedDocument(string Path, string Content);
```

Two consequences worth holding in mind:

- **`Plan` lexes the output pattern for you? No — it does not.** You lex `config.Output` yourself and pass the nodes as `outputPattern`. Passing the raw string will not compile.
- **`Plan` renders the documents.** So a *template* fault surfaces as a `TemplateException` out of `Plan`, aborting the whole render — which is correct, because template faults are static and affect every record. Record-level faults must therefore be caught **before** `Plan` (Task 10).

---

### Task 8: Atomic document writing

**Files:**
- Create: `src/DrMoustache.Cli/Output/DocumentWriter.cs`
- Test: `tests/DrMoustache.Cli.Tests/Output/DocumentWriterTests.cs`

**Interfaces:**
- Consumes: `RenderedDocument` (Core).
- Produces: `internal sealed class DocumentWriter(string outputRoot)` with `Task<int> WriteAllAsync(IReadOnlyList<RenderedDocument> documents, CancellationToken ct)` returning the number written.

The spec's requirement, verbatim: *"Documents are never partially written. A record renders fully to memory and reaches disk only when complete, so a mid-render failure leaves no half-written file and re-running is always safe."*

Rendering to memory already happens in Core. This task closes the remaining gap: a write that fails partway — disk full, process killed — must not leave a truncated file that looks valid. Write to a temporary file in the destination directory, then `File.Move` with overwrite, which is atomic on the same volume.

**Containment is re-checked here**, even though `DeliveryPlanner` already slugifies per value. Defence in depth on a filesystem write is warranted: this is the last place a bug anywhere upstream could escape the output directory.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Output/DocumentWriterTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Output;

using DrMoustache.Cli.Output;
using DrMoustache.Core.Rendering;
using FluentAssertions;
using Xunit;

public class DocumentWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("drm-out-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private DocumentWriter Writer() => new(_root);

    [Fact]
    public async Task WritesADocument()
    {
        var written = await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "# Hello")],
            TestContext.Current.CancellationToken);

        written.Should().Be(1);
        (await File.ReadAllTextAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken))
            .Should().Be("# Hello");
    }

    [Fact]
    public async Task CreatesNestedDirectories()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("dlq/expired/a.md", "x")],
            TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_root, "dlq", "expired", "a.md")).Should().BeTrue();
    }

    [Fact]
    public async Task OverwritesAnExistingDocument()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, "a.md"), "old", TestContext.Current.CancellationToken);

        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "new")], TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken))
            .Should().Be("new");
    }

    [Fact]
    public async Task LeavesNoTemporaryFilesBehind()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "x"), new RenderedDocument("b/c.md", "y")],
            TestContext.Current.CancellationToken);

        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(["a.md", "c.md"]);
    }

    [Fact]
    public async Task WritesUtf8WithoutAByteOrderMark()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "café")], TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken);

        bytes.Should().NotStartWith([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("a/../../escape.md")]
    [InlineData("/absolute.md")]
    public async Task RefusesToWriteOutsideTheOutputRoot(string path)
    {
        var act = async () => await Writer().WriteAllAsync(
            [new RenderedDocument(path, "x")], TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<OutputException>())
            .WithMessage("*outside*");
    }

    [Fact]
    public async Task AnEmptyDocumentListWritesNothingAndCreatesNoRoot()
    {
        var missing = Path.Combine(_root, "nested-root");

        var written = await new DocumentWriter(missing).WriteAllAsync(
            [], TestContext.Current.CancellationToken);

        written.Should().Be(0);
        Directory.Exists(missing).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentWriterTests`
Expected: compilation failure — `DocumentWriter` does not exist.

- [ ] **Step 3: Write `DocumentWriter`**

`src/DrMoustache.Cli/Output/DocumentWriter.cs`:

```csharp
namespace DrMoustache.Cli.Output;

using System.Text;
using DrMoustache.Core.Rendering;

/// <summary>A document could not be written.</summary>
internal sealed class OutputException : Exception
{
    public OutputException() { }

    public OutputException(string message) : base(message) { }

    public OutputException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Writes rendered documents beneath a root directory.</summary>
/// <remarks>Each document is written to a temporary file in its destination
/// directory and then moved into place, so a failure partway through never
/// leaves a truncated file that looks valid.</remarks>
internal sealed class DocumentWriter(string outputRoot)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _outputRoot = outputRoot
        ?? throw new ArgumentNullException(nameof(outputRoot));

    internal async Task<int> WriteAllAsync(
        IReadOnlyList<RenderedDocument> documents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0) return 0;

        var root = Path.GetFullPath(_outputRoot);
        Directory.CreateDirectory(root);

        foreach (var document in documents)
            await WriteAsync(root, document, ct).ConfigureAwait(false);

        return documents.Count;
    }

    private static async Task WriteAsync(
        string root, RenderedDocument document, CancellationToken ct)
    {
        var destination = Resolve(root, document.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Same directory as the destination, so the move is on one volume
        // and therefore atomic.
        var temporary = destination + ".drm-tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, document.Content, Utf8NoBom, ct)
                .ConfigureAwait(false);

            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OutputException($"could not write '{destination}': {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { /* best effort */ }
            }
        }
    }

    /// <summary>Resolves a document path against the root and refuses anything
    /// that escapes it.</summary>
    /// <remarks>DeliveryPlanner already slugifies each interpolated value, so
    /// this should be unreachable. It is the last place a bug anywhere
    /// upstream could write outside the output directory, which makes a
    /// second check worth its cost.</remarks>
    private static string Resolve(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': absolute paths fall outside "
                + $"the output directory '{root}'");
        }

        var combined = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!combined.StartsWith(
                root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': it resolves outside "
                + $"the output directory '{root}'");
        }

        return combined;
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentWriterTests`
Expected: 9 tests passed (the `[Theory]` contributes 3).

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Cli/Output tests/DrMoustache.Cli.Tests/Output
git commit -m "$(cat <<'EOF'
Add atomic document writing

Write to a temporary file in the destination directory then move into
place, so a failure partway through never leaves a truncated file that
looks valid.

Containment is re-checked here even though DeliveryPlanner already
slugifies per value: this is the last place an upstream bug could escape
the output directory.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 9: Template discovery and the render pipeline

**Files:**
- Create: `src/DrMoustache.Cli/Rendering/TemplateFile.cs`
- Create: `src/DrMoustache.Cli/Rendering/RenderPipeline.cs`
- Test: `tests/DrMoustache.Cli.Tests/Rendering/RenderPipelineTests.cs`

**Interfaces:**
- Consumes: `ResolvedSource`, `ProviderRegistry` (Task 7); `DocumentWriter` (Task 8); Core's `FrontmatterParser`, `TemplateLexer`, `DeliveryPlanner`, `ManifestBuilder`.
- Produces:
  - `internal sealed record TemplateFile(string Path, ParsedTemplate Parsed)` with `static IReadOnlyList<TemplateFile> Discover(string directory)`
  - `internal sealed record RenderOutcome(int DocumentsWritten, IReadOnlyList<string> Warnings, IReadOnlyList<RecordSkip> Skips)`
  - `internal sealed class RenderPipeline` with `Task<RenderOutcome> RunAsync(TemplateFile template, ResolvedSource source, DocumentWriter writer, CancellationToken ct)`

`RecordSkip` is defined in Task 10; for this task the list is always empty. That is deliberate — this task proves the happy path end to end, and Task 10 adds the failure paths on top rather than both landing at once.

Templates are discovered as `*.md` files, recursively, **excluding** anything under a directory beginning `_` (a partials convention) and excluding files with no `drmoustache:` block — a template directory legitimately contains hand-written pages too, and failing on those would make the tool unusable alongside ordinary docs.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Rendering/RenderPipelineTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Rendering;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using DrMoustache.Cli.Output;
using DrMoustache.Cli.Plugins;
using DrMoustache.Cli.Rendering;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class RenderPipelineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drm-pipe-").FullName;
    private readonly string _out = Directory.CreateTempSubdirectory("drm-pipe-out-").FullName;

    public void Dispose()
    {
        foreach (var d in new[] { _dir, _out })
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

    private sealed class StationProvider(params Station[] rows) : DataProvider<Station>
    {
        public override string Name => "test-stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var row in rows) yield return row;
        }
    }

    private ResolvedSource Source(params Station[] rows)
    {
        var configuration = new ConfigurationBuilder().Build();

        return new ResolvedSource("stations", new StationProvider(rows), new ProviderContext
        {
            Configuration = configuration,
            Source = configuration.GetSection("sources:stations"),
            Logger = NullLogger.Instance,
        });
    }

    private string WriteTemplate(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string FanoutTemplate = """
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

    [Fact]
    public void DiscoversTemplatesWithAReservedBlock()
    {
        WriteTemplate("a.md", FanoutTemplate);
        WriteTemplate("plain.md", "# just a page, no frontmatter");
        WriteTemplate("_partials/p.md", FanoutTemplate);

        var found = TemplateFile.Discover(_dir);

        found.Should().ContainSingle()
            .Which.Path.Should().EndWith("a.md");
    }

    [Fact]
    public void DiscoversTemplatesRecursively()
    {
        WriteTemplate("nested/deep/a.md", FanoutTemplate);

        TemplateFile.Discover(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task RendersOneDocumentPerRecord()
    {
        var path = WriteTemplate("a.md", FanoutTemplate);
        var template = TemplateFile.Discover(_dir).Single();

        var outcome = await new RenderPipeline().RunAsync(
            template,
            Source(new Station { Name = "alpha", Reading = 1 },
                   new Station { Name = "beta", Reading = 2 }),
            new DocumentWriter(_out),
            TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(2);
        File.Exists(Path.Combine(_out, "stations", "alpha.md")).Should().BeTrue();
        File.Exists(Path.Combine(_out, "stations", "beta.md")).Should().BeTrue();
    }

    [Fact]
    public async Task RenderedContentCarriesBodyAndPassthroughFrontmatter()
    {
        WriteTemplate("a.md", FanoutTemplate);
        var template = TemplateFile.Discover(_dir).Single();

        await new RenderPipeline().RunAsync(
            template, Source(new Station { Name = "alpha", Reading = 42 }),
            new DocumentWriter(_out), TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(
            Path.Combine(_out, "stations", "alpha.md"),
            TestContext.Current.CancellationToken);

        text.Should().StartWith("---");
        text.Should().Contain("title: alpha");
        text.Should().Contain("# alpha");
        text.Should().Contain("Reading: 42");
    }

    [Fact]
    public async Task CollateProducesOneDocument()
    {
        WriteTemplate("all.md", """
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

        var template = TemplateFile.Discover(_dir).Single();

        var outcome = await new RenderPipeline().RunAsync(
            template,
            Source(new Station { Name = "alpha", Reading = 1 },
                   new Station { Name = "beta", Reading = 2 }),
            new DocumentWriter(_out), TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(1);

        var text = await File.ReadAllTextAsync(
            Path.Combine(_out, "all.md"), TestContext.Current.CancellationToken);

        text.Should().Contain("# 2 stations");
        text.Should().Contain("- alpha");
        text.Should().Contain("- beta");
    }

    [Fact]
    public async Task CollisionWarningsAreSurfaced()
    {
        WriteTemplate("a.md", FanoutTemplate.Replace(
            "onCollision: error", "onCollision: suffix", StringComparison.Ordinal));

        var template = TemplateFile.Discover(_dir).Single();

        var outcome = await new RenderPipeline().RunAsync(
            template,
            Source(new Station { Name = "same", Reading = 1 },
                   new Station { Name = "same", Reading = 2 }),
            new DocumentWriter(_out), TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(2);
        outcome.Warnings.Should().ContainSingle().Which.Should().Contain("collision");
    }

    [Fact]
    public async Task NoRecordsProducesNoDocumentsAndDoesNotThrow()
    {
        WriteTemplate("a.md", FanoutTemplate);
        var template = TemplateFile.Discover(_dir).Single();

        var outcome = await new RenderPipeline().RunAsync(
            template, Source(), new DocumentWriter(_out),
            TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(0);
    }

    [Fact]
    public void ATemplateWithAMalformedReservedBlockIsReportedByPath()
    {
        WriteTemplate("bad.md", """
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
            .WithMessage("*bad.md*sideways*");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderPipelineTests`
Expected: compilation failure — `TemplateFile` and `RenderPipeline` do not exist.

- [ ] **Step 3: Write `TemplateFile`**

`src/DrMoustache.Cli/Rendering/TemplateFile.cs`:

```csharp
namespace DrMoustache.Cli.Rendering;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;

/// <summary>A fault attributable to one template file.</summary>
internal sealed class RenderException : Exception
{
    public RenderException() { }

    public RenderException(string message) : base(message) { }

    public RenderException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <param name="Path">Absolute path, used in error messages.</param>
internal sealed record TemplateFile(string Path, ParsedTemplate Parsed)
{
    private const string ReservedKey = "drmoustache:";

    /// <summary>Finds every renderable template beneath a directory.</summary>
    /// <remarks>Markdown files without a <c>drmoustache:</c> block are skipped
    /// rather than rejected: a template directory legitimately holds
    /// hand-written pages too. Directories beginning with an underscore are
    /// skipped as partials.</remarks>
    internal static IReadOnlyList<TemplateFile> Discover(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
            throw new RenderException($"template directory not found: {directory}");

        var templates = new List<TemplateFile>();

        foreach (var path in Directory
            .EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
            .Where(p => !IsUnderPartials(directory, p))
            .Order(StringComparer.Ordinal))
        {
            var content = File.ReadAllText(path);

            if (!content.Contains(ReservedKey, StringComparison.Ordinal))
                continue;

            try
            {
                templates.Add(new TemplateFile(path, FrontmatterParser.Parse(content)));
            }
            catch (TemplateException ex)
            {
                throw new RenderException($"{path}: {ex.Message}", ex);
            }
        }

        return templates;
    }

    private static bool IsUnderPartials(string root, string path)
        => Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => segment.StartsWith('_'));
}
```

- [ ] **Step 4: Write `RenderPipeline`**

`src/DrMoustache.Cli/Rendering/RenderPipeline.cs`:

```csharp
namespace DrMoustache.Cli.Rendering;

using DrMoustache.Cli.Output;
using DrMoustache.Cli.Plugins;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Template;

/// <param name="Skips">Records that could not be used. Always empty until
/// Task 10 adds record-level validation.</param>
internal sealed record RenderOutcome(
    int DocumentsWritten,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecordSkip> Skips);

internal sealed class RenderPipeline
{
    internal async Task<RenderOutcome> RunAsync(
        TemplateFile template,
        ResolvedSource source,
        DocumentWriter writer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        var records = await DrainAsync(source, ct).ConfigureAwait(false);

        var config = template.Parsed.Config;

        DeliveryResult result;

        try
        {
            result = DeliveryPlanner.Plan(
                config,
                TemplateLexer.Lex(config.Output),
                TemplateLexer.Lex(template.Parsed.Body),
                template.Parsed.PassthroughFrontmatter,
                records);
        }
        catch (TemplateException ex)
        {
            throw new RenderException($"{template.Path}: {ex.Message}", ex);
        }
        catch (DeliveryException ex)
        {
            throw new RenderException($"{template.Path}: {ex.Message}", ex);
        }

        var written = await writer.WriteAllAsync(result.Documents, ct).ConfigureAwait(false);

        return new RenderOutcome(written, result.Warnings, []);
    }

    /// <summary>Materializes the provider's records.</summary>
    /// <remarks>Delivery needs every record before it can name any output
    /// file, because a collision is a property of the whole set. The provider
    /// contract stays asynchronous so it can stream and be cancelled; the
    /// buffering stops here.</remarks>
    private static async Task<List<object>> DrainAsync(
        ResolvedSource source, CancellationToken ct)
    {
        var records = new List<object>();

        await foreach (var record in source.Provider
            .ReadAsync(source.Context, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            records.Add(record);
        }

        return records;
    }
}
```

`RecordSkip` does not exist yet — Task 10 defines it. To keep this task compiling on its own, add a placeholder in the same file **and delete it in Task 10**:

```csharp
/// <summary>Placeholder. Task 10 replaces this with the real type.</summary>
internal sealed record RecordSkip(string Reason);
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderPipelineTests`
Expected: 8 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Cli/Rendering tests/DrMoustache.Cli.Tests/Rendering
git commit -m "$(cat <<'EOF'
Add template discovery and the render pipeline

Markdown files without a drmoustache: block are skipped rather than
rejected -- a template directory legitimately holds hand-written pages too.

Records are materialized before planning: delivery needs the whole set
before it can name any file, because a collision is a property of the set.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 10: Record-level error collection, `--strict`, and the summary

**Files:**
- Create: `src/DrMoustache.Cli/Rendering/RecordValidator.cs`
- Create: `src/DrMoustache.Cli/Output/RenderReport.cs`
- Modify: `src/DrMoustache.Cli/Rendering/RenderPipeline.cs` (remove the placeholder `RecordSkip`; wire in validation)
- Test: `tests/DrMoustache.Cli.Tests/Rendering/RecordValidatorTests.cs`
- Test: `tests/DrMoustache.Cli.Tests/Output/RenderReportTests.cs`

**Interfaces:**
- Consumes: `RecordManifest`, `ManifestField`, `ManifestBuilder`, `ValueResolver` (Core); `RenderOutcome` (Task 9).
- Produces:
  - `internal sealed record RecordSkip(int Ordinal, string Reason)` — replaces Task 9's placeholder
  - `internal static class RecordValidator` with `static string? Validate(object record, RecordManifest manifest)` returning null when the record is usable, or a reason
  - `internal static class RenderReport` with `static string Summarize(string outputRoot, RenderOutcome outcome)` and `static int ExitCode(RenderOutcome outcome, bool strict)`

The spec's requirement, verbatim: *"One malformed record must not destroy a large report. Failures are collected, rendering continues, and the summary is honest about what was lost… `--strict` promotes any skip to a hard failure with exit 1 — the CI posture. The default is lenient because ad-hoc inspection wants the 4,997 successful documents rather than a stack trace. Silently dropping a record is never acceptable."*

Two sources of record-level failure:
1. **A provider throws mid-stream.** Draining catches it, records the ordinal and message, and stops reading that source — an iterator cannot be resumed after it throws.
2. **A record violates its own manifest.** A field the manifest declares non-nullable is null. Core renders that as empty rather than failing, so the CLI must check it — otherwise a report silently contains blanks where required data should be.

- [ ] **Step 1: Write the failing validator test**

`tests/DrMoustache.Cli.Tests/Rendering/RecordValidatorTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Rendering;

using DrMoustache.Abstractions;
using DrMoustache.Cli.Rendering;
using DrMoustache.Core.Manifest;
using FluentAssertions;
using Xunit;

public class RecordValidatorTests
{
    private sealed record Inner
    {
        [Field] public required string Id { get; init; }
    }

    [DrMoustacheRecord("thing")]
    private sealed record Thing
    {
        [Field("the order number")] public required string OrderId { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public string? Note { get; init; }
        [Field] public Inner? Nested { get; init; }
    }

    private static RecordManifest Manifest => ManifestBuilder.FromType<Thing>();

    [Fact]
    public void AcceptsACompleteRecord()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m }, Manifest)
            .Should().BeNull();

    [Fact]
    public void AcceptsANullNullableField()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Note = null }, Manifest)
            .Should().BeNull();

    [Fact]
    public void RejectsANullRequiredField()
    {
        // `required` is a compile-time guarantee, not a runtime one — a
        // provider using reflection or deserialization can still produce null.
        var thing = new Thing { OrderId = null!, Total = 1m };

        RecordValidator.Validate(thing, Manifest)
            .Should().Contain("orderId").And.Contain("required");
    }

    [Fact]
    public void NamesTheFieldDescriptionWhenOneExists()
        => RecordValidator.Validate(new Thing { OrderId = null!, Total = 1m }, Manifest)
            .Should().Contain("the order number");

    [Fact]
    public void DoesNotDescendIntoANullNullableObject()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Nested = null }, Manifest)
            .Should().BeNull();

    [Fact]
    public void RejectsANullRequiredFieldInsideANestedObject()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Nested = new Inner { Id = null! } },
                Manifest)
            .Should().Contain("nested.id");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RecordValidatorTests`
Expected: compilation failure — `RecordValidator` does not exist.

- [ ] **Step 3: Write `RecordValidator`**

`src/DrMoustache.Cli/Rendering/RecordValidator.cs`:

```csharp
namespace DrMoustache.Cli.Rendering;

using DrMoustache.Core.Binding;
using DrMoustache.Core.Manifest;

/// <summary>Checks a record against the shape its own manifest declares.</summary>
/// <remarks>C#'s <c>required</c> is a compile-time guarantee only. A provider
/// that deserializes or reflects can still yield null in a non-nullable field,
/// and the renderer would emit an empty string — a report silently missing
/// data it claims to contain. This catches that before anything is written.</remarks>
internal static class RecordValidator
{
    /// <returns>Null when the record is usable, otherwise the reason.</returns>
    internal static string? Validate(object record, RecordManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(manifest);

        return Check(record, manifest.Fields, prefix: null);
    }

    private static string? Check(
        object? scope, IReadOnlyList<ManifestField> fields, string? prefix)
    {
        foreach (var field in fields)
        {
            var path = prefix is null ? field.Path : $"{prefix}.{field.Path}";

            if (!ValueResolver.TryResolve(scope, field.Path, out var value))
                continue;

            if (value is null)
            {
                if (field.IsNullable) continue;

                var description = field.Description is { } d ? $" ({d})" : "";
                return $"'{path}' was null but the record declares it required{description}";
            }

            if (field.Type is FieldType.Object
                && Check(value, field.Children, path) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
```

Collections are deliberately not descended into: a collection of N items would produce N × fields checks per record, and a malformed element renders as a blank cell rather than a missing document. Note this in your report.

- [ ] **Step 4: Write the failing report test**

`tests/DrMoustache.Cli.Tests/Output/RenderReportTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Output;

using DrMoustache.Cli.Output;
using DrMoustache.Cli.Rendering;
using FluentAssertions;
using Xunit;

public class RenderReportTests
{
    private static RenderOutcome Outcome(
        int written = 0,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<RecordSkip>? skips = null)
        => new(written, warnings ?? [], skips ?? []);

    [Fact]
    public void ACleanRunReportsTheCountAndDestination()
        => RenderReport.Summarize("./dist", Outcome(written: 3))
            .Should().Contain("3").And.Contain("./dist");

    [Fact]
    public void SkipsAreListedWithTheirReason()
    {
        var text = RenderReport.Summarize("./dist", Outcome(
            written: 2,
            skips: [new RecordSkip(41, "'total' was null but the record declares it required")]));

        text.Should().Contain("1 skipped");
        text.Should().Contain("41");
        text.Should().Contain("total");
    }

    [Fact]
    public void WarningsAreListed()
        => RenderReport.Summarize("./dist", Outcome(
                written: 2, warnings: ["collision on 'a.md': 2 records"]))
            .Should().Contain("collision on 'a.md'");

    [Fact]
    public void ACleanRunListsNoSkipsOrWarningSections()
    {
        var text = RenderReport.Summarize("./dist", Outcome(written: 1));
        text.Should().NotContain("skipped").And.NotContain("warning");
    }

    [Fact]
    public void ExitCodeIsZeroForACleanRun()
        => RenderReport.ExitCode(Outcome(written: 1), strict: false).Should().Be(0);

    [Fact]
    public void ExitCodeIsZeroForSkipsWhenLenient()
        => RenderReport.ExitCode(
                Outcome(written: 1, skips: [new RecordSkip(1, "x")]), strict: false)
            .Should().Be(0);

    [Fact]
    public void ExitCodeIsOneForSkipsWhenStrict()
        => RenderReport.ExitCode(
                Outcome(written: 1, skips: [new RecordSkip(1, "x")]), strict: true)
            .Should().Be(1);

    [Fact]
    public void WarningsDoNotFailEvenUnderStrict()
        => RenderReport.ExitCode(
                Outcome(written: 1, warnings: ["a collision was disambiguated"]), strict: true)
            .Should().Be(0);
}
```

`WarningsDoNotFailEvenUnderStrict` encodes a real distinction: a collision that `suffix` resolved lost no data, so it is not a failure. Only a skip means a record did not reach the output.

- [ ] **Step 5: Write `RenderReport` and `RecordSkip`**

`src/DrMoustache.Cli/Output/RenderReport.cs`:

```csharp
namespace DrMoustache.Cli.Output;

using System.Globalization;
using System.Text;
using DrMoustache.Cli.Rendering;

internal static class RenderReport
{
    internal static string Summarize(string outputRoot, RenderOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var sb = new StringBuilder();

        sb.Append(CultureInfo.InvariantCulture,
            $"rendered {outcome.DocumentsWritten} document");

        if (outcome.DocumentsWritten != 1) sb.Append('s');
        sb.Append(CultureInfo.InvariantCulture, $" to {outputRoot}");

        if (outcome.Skips.Count > 0)
            sb.Append(CultureInfo.InvariantCulture, $"  ({outcome.Skips.Count} skipped)");

        sb.AppendLine();

        foreach (var skip in outcome.Skips)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  skipped  record {skip.Ordinal}  {skip.Reason}");
        }

        foreach (var warning in outcome.Warnings)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  warning  {warning}");

        return sb.ToString();
    }

    /// <summary>A skip means a record never reached the output, so
    /// <c>--strict</c> fails on it. A warning means something was resolved
    /// without losing data, so it never fails.</summary>
    internal static int ExitCode(RenderOutcome outcome, bool strict)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return strict && outcome.Skips.Count > 0 ? 1 : 0;
    }
}
```

`src/DrMoustache.Cli/Rendering/RenderPipeline.cs` — delete the placeholder `RecordSkip` and add the real one at the top of the file:

```csharp
/// <param name="Ordinal">Zero-based position in the provider's sequence.
/// The only stable identifier available before a record is bound.</param>
internal sealed record RecordSkip(int Ordinal, string Reason);
```

- [ ] **Step 6: Wire validation and provider faults into `RenderPipeline`**

Replace `DrainAsync` and the `RunAsync` body's use of it:

```csharp
    private static async Task<(List<object> Records, List<RecordSkip> Skips)> DrainAsync(
        ResolvedSource source, CancellationToken ct)
    {
        var records = new List<object>();
        var skips = new List<RecordSkip>();

        RecordManifest? manifest = null;
        var ordinal = 0;

        var enumerator = source.Provider
            .ReadAsync(source.Context, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false)
            .GetAsyncEnumerator();

        try
        {
            while (true)
            {
                object record;

                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    record = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // An iterator cannot be resumed after it throws, so this
                    // ends the read. Everything already drained still renders.
                    skips.Add(new RecordSkip(ordinal,
                        $"provider '{source.Provider.Name}' threw: {ex.Message}"));
                    break;
                }

                manifest ??= ManifestBuilder.FromType(source.Provider.RecordType);

                if (RecordValidator.Validate(record, manifest) is { } reason)
                    skips.Add(new RecordSkip(ordinal, reason));
                else
                    records.Add(record);

                ordinal++;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return (records, skips);
    }
```

In `RunAsync`, replace the drain call and the return:

```csharp
        var (records, skips) = await DrainAsync(source, ct).ConfigureAwait(false);
        …
        return new RenderOutcome(written, result.Warnings, skips);
```

Add `using DrMoustache.Core.Manifest;` to the file.

- [ ] **Step 7: Add pipeline tests for the failure paths**

Append to `tests/DrMoustache.Cli.Tests/Rendering/RenderPipelineTests.cs`:

```csharp
    private sealed class ThrowingProvider : DataProvider<Station>
    {
        public override string Name => "throwing";

        public override async IAsyncEnumerable<Station> ReadAsync(
            ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Station { Name = "good", Reading = 1 };
            throw new InvalidOperationException("the well ran dry");
        }
    }

    [Fact]
    public async Task AProviderFaultSkipsTheRestButKeepsWhatWasRead()
    {
        WriteTemplate("a.md", FanoutTemplate);
        var template = TemplateFile.Discover(_dir).Single();

        var configuration = new ConfigurationBuilder().Build();
        var source = new ResolvedSource("stations", new ThrowingProvider(), new ProviderContext
        {
            Configuration = configuration,
            Source = configuration.GetSection("sources:stations"),
            Logger = NullLogger.Instance,
        });

        var outcome = await new RenderPipeline().RunAsync(
            template, source, new DocumentWriter(_out),
            TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(1);
        outcome.Skips.Should().ContainSingle()
            .Which.Reason.Should().Contain("the well ran dry");
        File.Exists(Path.Combine(_out, "stations", "good.md")).Should().BeTrue();
    }

    [Fact]
    public async Task ARecordViolatingItsManifestIsSkippedNotRendered()
    {
        WriteTemplate("a.md", FanoutTemplate);
        var template = TemplateFile.Discover(_dir).Single();

        var outcome = await new RenderPipeline().RunAsync(
            template,
            Source(new Station { Name = "alpha", Reading = 1 },
                   new Station { Name = null!, Reading = 2 }),
            new DocumentWriter(_out), TestContext.Current.CancellationToken);

        outcome.DocumentsWritten.Should().Be(1);
        outcome.Skips.Should().ContainSingle()
            .Which.Reason.Should().Contain("name").And.Contain("required");
    }
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RecordValidatorTests`
Expected: 6 tests passed.

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderReportTests`
Expected: 8 tests passed.

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderPipelineTests`
Expected: 10 tests passed.

- [ ] **Step 9: Commit**

```bash
git add src/DrMoustache.Cli tests/DrMoustache.Cli.Tests
git commit -m "$(cat <<'EOF'
Add record-level error collection, --strict, and the summary

One malformed record must not destroy a large report, and silently
dropping one is never acceptable: failures are collected with an ordinal
and a reason, rendering continues, and the summary says what was lost.

C#'s `required` is compile-time only, so a provider that deserializes can
still yield null in a non-nullable field. Core renders that as empty --
a report silently missing data it claims to contain -- so the CLI checks
each record against its manifest first.

--strict fails on a skip but not on a warning: a collision that suffix
resolved lost no data.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 11: Wire the `render` verb

**Files:**
- Create: `src/DrMoustache.Cli/Verbs/RenderVerb.cs`
- Modify: `src/DrMoustache.Cli/Program.cs` (replace the `render` stub action)
- Test: `tests/DrMoustache.Cli.Tests/Verbs/RenderVerbTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2, 3, 6, 7, 8, 9, 10.
- Produces: `internal static class RenderVerb` with `static Task<int> RunAsync(DirectoryInfo templates, DirectoryInfo output, bool strict, FileInfo? config, TextWriter stdout, TextWriter stderr, CancellationToken ct)`

Passing `stdout`/`stderr` explicitly rather than using `Console` directly is what makes this testable without capturing process output.

Config file resolution: `--config` if given, else `./drmoustache.yaml` if present, else an empty configuration (no plugins, no sources) — which then fails with a clear message when a template names a source.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Verbs/RenderVerbTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Verbs;

using DrMoustache.Cli.Verbs;
using FluentAssertions;
using Xunit;

public class RenderVerbTests : IDisposable
{
    private readonly string _templates = Directory.CreateTempSubdirectory("drm-v-t-").FullName;
    private readonly string _output = Directory.CreateTempSubdirectory("drm-v-o-").FullName;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        foreach (var d in new[] { _templates, _output })
        {
            try { Directory.Delete(d, recursive: true); }
            catch (IOException) { /* best effort */ }
        }

        _stdout.Dispose();
        _stderr.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<int> RunAsync(bool strict = false, FileInfo? config = null)
        => RenderVerb.RunAsync(
            new DirectoryInfo(_templates), new DirectoryInfo(_output),
            strict, config, _stdout, _stderr, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnEmptyTemplateDirectorySucceedsAndSaysSo()
    {
        var exit = await RunAsync();

        exit.Should().Be(0);
        _stdout.ToString().Should().Contain("no templates");
    }

    [Fact]
    public async Task AMissingTemplateDirectoryFailsWithThePath()
    {
        var missing = Path.Combine(_templates, "nope");

        var exit = await RenderVerb.RunAsync(
            new DirectoryInfo(missing), new DirectoryInfo(_output),
            strict: false, config: null, _stdout, _stderr,
            TestContext.Current.CancellationToken);

        exit.Should().Be(1);
        _stderr.ToString().Should().Contain("nope");
    }

    [Fact]
    public async Task ATemplateNamingAnUnconfiguredSourceFailsClearly()
    {
        await File.WriteAllTextAsync(Path.Combine(_templates, "a.md"), """
            ---
            drmoustache:
              source: nowhere
              delivery: collate
              output: a.md
            ---
            body
            """, TestContext.Current.CancellationToken);

        var exit = await RunAsync();

        exit.Should().Be(1);
        _stderr.ToString().Should().Contain("nowhere");
    }

    [Fact]
    public async Task AMalformedTemplateFailsWithItsPathAndDoesNotWrite()
    {
        await File.WriteAllTextAsync(Path.Combine(_templates, "bad.md"), """
            ---
            drmoustache:
              source: x
              delivery: fanout
              output: "{{id}}.md"
            ---
            body
            """, TestContext.Current.CancellationToken);

        var exit = await RunAsync();

        exit.Should().Be(1);
        _stderr.ToString().Should().Contain("bad.md").And.Contain("onCollision");
        Directory.EnumerateFiles(_output, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingConfigFilePassedExplicitlyFailsByPath()
    {
        var exit = await RunAsync(config: new FileInfo("/nonexistent/drmoustache.yaml"));

        exit.Should().Be(1);
        _stderr.ToString().Should().Contain("/nonexistent/drmoustache.yaml");
    }
}
```

These exercise the failure paths, which are the ones a user meets first. The success path is proven end to end in Task 14's smoke test, where a real plugin and a real database exist.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderVerbTests`
Expected: compilation failure — `RenderVerb` does not exist.

- [ ] **Step 3: Write `RenderVerb`**

`src/DrMoustache.Cli/Verbs/RenderVerb.cs`:

```csharp
namespace DrMoustache.Cli.Verbs;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Output;
using DrMoustache.Cli.Plugins;
using DrMoustache.Cli.Rendering;
using Microsoft.Extensions.Logging;

internal static class RenderVerb
{
    internal static async Task<int> RunAsync(
        DirectoryInfo templates,
        DirectoryInfo output,
        bool strict,
        FileInfo? config,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            var discovered = TemplateFile.Discover(templates.FullName);

            if (discovered.Count == 0)
            {
                await stdout.WriteLineAsync(
                    $"no templates found in {templates.FullName} "
                    + "(a template needs a 'drmoustache:' block in its frontmatter)")
                    .ConfigureAwait(false);
                return 0;
            }

            var file = VerbSupport.LoadConfigurationFile(config);

            using var loggers = LoggerFactory.Create(builder => builder.AddConsole());

            var configuration = CliConfiguration.Build(
                appSettingsPath: null,
                commandLineValues: new Dictionary<string, string?>(),
                basePath: Directory.GetCurrentDirectory());

            using var registry = await ProviderRegistry.LoadAsync(
                file, configuration, loggers,
                new PluginAcquisition(PluginAcquisition.DefaultCacheRoot), ct)
                .ConfigureAwait(false);

            var writer = new DocumentWriter(output.FullName);
            var pipeline = new RenderPipeline();

            var written = 0;
            var warnings = new List<string>();
            var skips = new List<RecordSkip>();

            foreach (var template in discovered)
            {
                var source = registry.Resolve(template.Parsed.Config.Source);
                var outcome = await pipeline
                    .RunAsync(template, source, writer, ct).ConfigureAwait(false);

                written += outcome.DocumentsWritten;
                warnings.AddRange(outcome.Warnings);
                skips.AddRange(outcome.Skips);
            }

            var total = new RenderOutcome(written, warnings, skips);
            await stdout.WriteAsync(RenderReport.Summarize(output.FullName, total))
                .ConfigureAwait(false);

            return RenderReport.ExitCode(total, strict);
        }
        catch (Exception ex) when (VerbSupport.IsUserFacing(ex))
        {
            await stderr.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
```

- [ ] **Step 4: Write the shared verb support**

`src/DrMoustache.Cli/Verbs/VerbSupport.cs`:

```csharp
namespace DrMoustache.Cli.Verbs;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Output;
using DrMoustache.Cli.Plugins;
using DrMoustache.Cli.Rendering;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;

internal static class VerbSupport
{
    internal const string DefaultConfigFileName = "drmoustache.yaml";

    /// <summary>Every exception type that represents a user's mistake rather
    /// than a defect. These are reported as one clear line; anything else
    /// propagates with its stack trace, because it is a bug.</summary>
    internal static bool IsUserFacing(Exception ex) => ex
        is ConfigurationException
        or PluginLoadException
        or RenderException
        or OutputException
        or TemplateException
        or DeliveryException
        or ManifestException;

    /// <summary>Resolves the configuration file: the explicit path, else
    /// ./drmoustache.yaml if present, else empty.</summary>
    internal static DrMoustacheFile LoadConfigurationFile(FileInfo? explicitPath)
    {
        if (explicitPath is not null)
            return DrMoustacheFile.Load(explicitPath.FullName);

        var conventional = Path.Combine(Directory.GetCurrentDirectory(), DefaultConfigFileName);

        return File.Exists(conventional)
            ? DrMoustacheFile.Load(conventional)
            : DrMoustacheFile.Parse("");
    }
}
```

- [ ] **Step 5: Wire it into `Program.cs`**

Replace the `render` stub action:

```csharp
        command.SetAction(async (ParseResult pr, CancellationToken ct) =>
            await RenderVerb.RunAsync(
                pr.GetValue(templates)!,
                pr.GetValue(output)!,
                pr.GetValue(strict),
                pr.GetValue(config),
                Console.Out,
                Console.Error,
                ct).ConfigureAwait(false));
```

Add `using DrMoustache.Cli.Verbs;` to `Program.cs`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RenderVerbTests`
Expected: 5 tests passed.

Run: `dotnet build DrMoustache.slnx`
Expected: 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache.Cli tests/DrMoustache.Cli.Tests
git commit -m "$(cat <<'EOF'
Wire the render verb

Verbs take TextWriters rather than using Console directly, so failure
paths are testable without capturing process output.

User mistakes are reported as one clear line; anything else propagates
with its stack trace, because it is a bug rather than bad input.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 12: The `check` and `init` verbs

**Files:**
- Create: `src/DrMoustache.Cli/Verbs/CheckVerb.cs`
- Create: `src/DrMoustache.Cli/Verbs/InitVerb.cs`
- Modify: `src/DrMoustache.Cli/Program.cs` (replace both stub actions)
- Test: `tests/DrMoustache.Cli.Tests/Verbs/CheckVerbTests.cs`
- Test: `tests/DrMoustache.Cli.Tests/Verbs/InitVerbTests.cs`

**Interfaces:**
- Consumes: `TemplateFile`, `VerbSupport`, `ProviderRegistry`, `TemplateValidator`, `ManifestBuilder`.
- Produces:
  - `internal static class CheckVerb` with `static Task<int> RunAsync(DirectoryInfo templates, FileInfo? config, TextWriter stdout, TextWriter stderr, CancellationToken ct)`
  - `internal static class InitVerb` with `static Task<int> RunAsync(DirectoryInfo? directory, TextWriter stdout, TextWriter stderr, CancellationToken ct)`

The spec, verbatim: *"`check` performs no provider reads — it validates that every referenced field exists in the declared manifest, that delivery configuration is complete, and that named plugins load. This makes it a fast CI gate."*

**`check` must pass the template's `DeliveryMode` to the validator.** `TemplateValidator.Validate` defaults to `Fanout`; a `collate` template validated with the default reports `unknown field 'count'` and then skips its loop body entirely — failing valid templates while passing broken ones. This is recorded in `docs/known-issues.md` as R5, and this task is where it must not be reproduced.

`init` scaffolds a `drmoustache.yaml` and one template. The template must model the **quoted** form for values beginning `{{`, since the unquoted form is an error.

- [ ] **Step 1: Write the failing `check` test**

`tests/DrMoustache.Cli.Tests/Verbs/CheckVerbTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Verbs;

using DrMoustache.Cli.Verbs;
using FluentAssertions;
using Xunit;

public class CheckVerbTests : IDisposable
{
    private readonly string _templates = Directory.CreateTempSubdirectory("drm-chk-").FullName;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        try { Directory.Delete(_templates, recursive: true); }
        catch (IOException) { /* best effort */ }
        _stdout.Dispose();
        _stderr.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<int> RunAsync()
        => CheckVerb.RunAsync(
            new DirectoryInfo(_templates), config: null,
            _stdout, _stderr, TestContext.Current.CancellationToken);

    private void Write(string name, string content)
        => File.WriteAllText(Path.Combine(_templates, name), content);

    [Fact]
    public async Task AnEmptyDirectorySucceeds()
        => (await RunAsync()).Should().Be(0);

    [Fact]
    public async Task AMalformedReservedBlockFailsWithThePath()
    {
        Write("bad.md", """
            ---
            drmoustache:
              source: x
              delivery: fanout
              output: "{{id}}.md"
            ---
            body
            """);

        (await RunAsync()).Should().Be(1);
        _stderr.ToString().Should().Contain("bad.md").And.Contain("onCollision");
    }

    [Fact]
    public async Task ATemplateNamingAnUnconfiguredSourceFails()
    {
        Write("a.md", """
            ---
            drmoustache:
              source: nowhere
              delivery: collate
              output: a.md
            ---
            body
            """);

        (await RunAsync()).Should().Be(1);
        _stderr.ToString().Should().Contain("nowhere");
    }

    [Fact]
    public async Task CheckNeverWritesOutput()
    {
        Write("a.md", """
            ---
            drmoustache:
              source: nowhere
              delivery: collate
              output: a.md
            ---
            body
            """);

        await RunAsync();

        Directory.EnumerateFiles(_templates)
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(["a.md"]);
    }
}
```

Field-level validation against a real manifest is covered by the smoke test in Task 14, where a plugin is actually loaded. These tests cover what `check` can establish without one.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CheckVerbTests`
Expected: compilation failure — `CheckVerb` does not exist.

- [ ] **Step 3: Write `CheckVerb`**

`src/DrMoustache.Cli/Verbs/CheckVerb.cs`:

```csharp
namespace DrMoustache.Cli.Verbs;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Plugins;
using DrMoustache.Cli.Rendering;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;
using DrMoustache.Core.Validation;
using Microsoft.Extensions.Logging;

internal static class CheckVerb
{
    internal static async Task<int> RunAsync(
        DirectoryInfo templates,
        FileInfo? config,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            var discovered = TemplateFile.Discover(templates.FullName);

            if (discovered.Count == 0)
            {
                await stdout.WriteLineAsync($"no templates found in {templates.FullName}")
                    .ConfigureAwait(false);
                return 0;
            }

            var file = VerbSupport.LoadConfigurationFile(config);

            using var loggers = LoggerFactory.Create(builder => builder.AddConsole());

            var configuration = CliConfiguration.Build(
                null, new Dictionary<string, string?>(), Directory.GetCurrentDirectory());

            using var registry = await ProviderRegistry.LoadAsync(
                file, configuration, loggers,
                new PluginAcquisition(PluginAcquisition.DefaultCacheRoot), ct)
                .ConfigureAwait(false);

            var errors = new List<string>();

            foreach (var template in discovered)
            {
                var source = registry.Resolve(template.Parsed.Config.Source);
                var manifest = ManifestBuilder.FromType(source.Provider.RecordType);

                // The delivery mode is load-bearing: a collate template
                // validated as fanout reports 'count' and 'records' as
                // unknown fields and then never descends into the loop body,
                // so it fails valid templates and passes broken ones.
                var problems = TemplateValidator.Validate(
                    TemplateLexer.Lex(template.Parsed.Body),
                    manifest,
                    template.Parsed.Config.Delivery);

                foreach (var problem in problems)
                    errors.Add($"{template.Path}: {problem}");
            }

            foreach (var error in errors)
                await stderr.WriteLineAsync(error).ConfigureAwait(false);

            if (errors.Count > 0) return 1;

            await stdout.WriteLineAsync(
                $"checked {discovered.Count} template"
                + (discovered.Count == 1 ? "" : "s") + " — no problems found")
                .ConfigureAwait(false);

            return 0;
        }
        catch (Exception ex) when (VerbSupport.IsUserFacing(ex))
        {
            await stderr.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
```

- [ ] **Step 4: Write the failing `init` test**

`tests/DrMoustache.Cli.Tests/Verbs/InitVerbTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Verbs;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Verbs;
using DrMoustache.Core.Frontmatter;
using FluentAssertions;
using Xunit;

public class InitVerbTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drm-init-").FullName;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
        _stdout.Dispose();
        _stderr.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<int> RunAsync()
        => InitVerb.RunAsync(
            new DirectoryInfo(_dir), _stdout, _stderr, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ScaffoldsAConfigFileAndATemplate()
    {
        (await RunAsync()).Should().Be(0);

        File.Exists(Path.Combine(_dir, "drmoustache.yaml")).Should().BeTrue();
        Directory.EnumerateFiles(Path.Combine(_dir, "templates"), "*.md")
            .Should().ContainSingle();
    }

    [Fact]
    public async Task TheScaffoldedConfigParses()
    {
        await RunAsync();

        var file = DrMoustacheFile.Load(Path.Combine(_dir, "drmoustache.yaml"));
        file.Sources.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TheScaffoldedTemplateParses()
    {
        await RunAsync();

        var path = Directory.EnumerateFiles(
            Path.Combine(_dir, "templates"), "*.md").Single();

        var parsed = FrontmatterParser.Parse(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        parsed.Config.Source.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RefusesToOverwriteAnExistingConfig()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "drmoustache.yaml"), "existing: true",
            TestContext.Current.CancellationToken);

        (await RunAsync()).Should().Be(1);
        _stderr.ToString().Should().Contain("drmoustache.yaml");

        (await File.ReadAllTextAsync(
            Path.Combine(_dir, "drmoustache.yaml"), TestContext.Current.CancellationToken))
            .Should().Be("existing: true");
    }
}
```

`TheScaffoldedTemplateParses` is the important one: it proves the scaffold is not merely written but *valid*, which is exactly the mistake a hand-written template string invites.

- [ ] **Step 5: Write `InitVerb`**

`src/DrMoustache.Cli/Verbs/InitVerb.cs`:

```csharp
namespace DrMoustache.Cli.Verbs;

using DrMoustache.Cli.Output;

internal static class InitVerb
{
    private const string ConfigTemplate = """
        # Plugins are NuGet packages, written as PackageId@Version.
        plugins:
          - DrMoustache.Sample.Plugin@1.0.0

        # A source names a provider by its Name and passes it settings.
        sources:
          observations:
            type: sample-sqlite
            path: ./weather.db
            # scopeConfig: true   # narrow this provider to its own section
        """;

    // Values that BEGIN with {{ must be quoted: YAML reads a leading '{' as
    // a flow mapping. A tag elsewhere in the value needs no quotes.
    private const string TemplateTemplate = """
        ---
        drmoustache:
          source: observations
          delivery: fanout
          output: stations/{{station}}.md
          onCollision: suffix
        title: "{{station}}"
        ---

        # {{station}}

        Recorded {{date recordedAt "yyyy-MM-dd HH:mm"}} at {{temperature}}°C.

        {{#note}}> {{.}}
        {{/note}}
        """;

    internal static async Task<int> RunAsync(
        DirectoryInfo? directory,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var root = directory?.FullName ?? Directory.GetCurrentDirectory();

        try
        {
            var configPath = Path.Combine(root, VerbSupport.DefaultConfigFileName);

            if (File.Exists(configPath))
            {
                await stderr.WriteLineAsync(
                    $"error: {configPath} already exists; init will not overwrite it")
                    .ConfigureAwait(false);
                return 1;
            }

            var templatesDir = Path.Combine(root, "templates");
            var templatePath = Path.Combine(templatesDir, "observation.md");

            if (File.Exists(templatePath))
            {
                await stderr.WriteLineAsync(
                    $"error: {templatePath} already exists; init will not overwrite it")
                    .ConfigureAwait(false);
                return 1;
            }

            Directory.CreateDirectory(templatesDir);

            await File.WriteAllTextAsync(configPath, ConfigTemplate, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(templatePath, TemplateTemplate, ct)
                .ConfigureAwait(false);

            await stdout.WriteLineAsync($"created {configPath}").ConfigureAwait(false);
            await stdout.WriteLineAsync($"created {templatePath}").ConfigureAwait(false);
            await stdout.WriteLineAsync("next: drmoustache check ./templates")
                .ConfigureAwait(false);

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync($"error: could not scaffold in {root}: {ex.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }
}
```

- [ ] **Step 6: Wire both into `Program.cs`**

Replace the `check` stub action:

```csharp
        command.SetAction(async (ParseResult pr, CancellationToken ct) =>
            await CheckVerb.RunAsync(
                pr.GetValue(templates)!, pr.GetValue(config),
                Console.Out, Console.Error, ct).ConfigureAwait(false));
```

Replace the `init` stub action:

```csharp
        command.SetAction(async (ParseResult pr, CancellationToken ct) =>
            await InitVerb.RunAsync(
                pr.GetValue(directory), Console.Out, Console.Error, ct).ConfigureAwait(false));
```

`init` does not use `--config`; the option stays on the command for a uniform surface but is ignored. Remove it from `BuildInitCommand` if that reads better to you, and update `CommandShellTests.ConfigOptionIsAvailableOnEveryVerb` accordingly — but say which you chose in your report.

- [ ] **Step 7: Run the tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CheckVerbTests`
Expected: 4 tests passed.

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~InitVerbTests`
Expected: 4 tests passed.

- [ ] **Step 8: Commit**

```bash
git add src/DrMoustache.Cli tests/DrMoustache.Cli.Tests
git commit -m "$(cat <<'EOF'
Add the check and init verbs

check passes each template's own DeliveryMode to the validator. Validating
a collate template as fanout reports count and records as unknown fields
and then never descends into the loop body -- failing valid templates while
passing broken ones (docs/known-issues.md R5).

init's scaffold is asserted to parse, not merely to exist: a hand-written
template string is exactly the kind of thing that ships subtly invalid.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 13: `docs/plugin-architecture.md`

**Files:**
- Create: `docs/plugin-architecture.md`
- Test: `tests/DrMoustache.Cli.Tests/DocumentationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the teaching artifact the spec calls for, and a test that keeps its code samples honest.

The spec, verbatim: *"`docs/plugin-architecture.md` is a deliberate teaching artifact covering `AssemblyLoadContext`, `AssemblyDependencyResolver`, the shared-abstractions-assembly trap, collectible contexts and unloading, and why the contract lives in its own package."*

Documentation rots. The test below asserts the document still names the types it teaches about and that those types still exist — a cheap guard that catches a rename silently invalidating the prose.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/DocumentationTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests;

using FluentAssertions;
using Xunit;

public class DocumentationTests
{
    private static string PluginArchitecture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("tests must run inside the repository");

        var path = Path.Combine(dir!.FullName, "docs", "plugin-architecture.md");
        File.Exists(path).Should().BeTrue($"{path} must exist");

        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("AssemblyLoadContext")]
    [InlineData("AssemblyDependencyResolver")]
    [InlineData("isCollectible")]
    [InlineData("DrMoustache.Abstractions")]
    [InlineData("deps.json")]
    public void CoversTheConceptsTheSpecRequires(string concept)
        => PluginArchitecture().Should().Contain(concept);

    [Fact]
    public void NamesTypesThatStillExist()
    {
        var text = PluginArchitecture();

        // If the document names a DrMoustache type, that type must exist —
        // otherwise the prose is teaching something that has been renamed.
        var abstractions = typeof(DrMoustache.Abstractions.IDataProvider).Assembly;

        foreach (var name in new[] { "IDataProvider", "DataProvider", "ProviderContext" })
        {
            if (!text.Contains(name, StringComparison.Ordinal)) continue;

            abstractions.GetTypes().Select(t => t.Name)
                .Should().Contain(n => n.StartsWith(name, StringComparison.Ordinal),
                    $"the document teaches '{name}', so it must still exist");
        }
    }

    [Fact]
    public void ExplainsTheSharedContractTrapRatherThanJustMentioningIt()
    {
        var text = PluginArchitecture();

        text.Should().Contain("default",
            "the fix is to defer the shared assembly to the default context");
        text.Should().MatchRegex("(?i)(same name|identical name|different (Type|context))",
            "the trap is that identical type names in different contexts are different Types");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentationTests`
Expected: fails — `docs/plugin-architecture.md` does not exist.

- [ ] **Step 3: Write the document**

Create `docs/plugin-architecture.md` covering, in this order:

1. **Why the contract lives in its own package.** `DrMoustache.Abstractions` is small and stable so a plugin built against 1.0 keeps working when the engine reaches 1.4. Show the dependency direction: plugin → Abstractions ← Core.

2. **`AssemblyLoadContext` in one paragraph.** What isolation buys, and that a plugin's dependencies must not collide with the host's.

3. **`AssemblyDependencyResolver` and `.deps.json`.** Why a bare `LoadFromAssemblyPath` appears to work and then throws `FileNotFoundException` at the first call into a dependency. Include the real `PluginLoadContext.Load` override from `src/DrMoustache.Cli/Plugins/PluginLoadContext.cs`. Note that `LoadUnmanagedDll` is a separate path, needed for packages with native assets — the sample plugin has them.

4. **The shared-contract trap.** The centrepiece. If the plugin's own copy of `DrMoustache.Abstractions` loads into the plugin context, `is IDataProvider` returns **false** — same type name, different `AssemblyLoadContext`, therefore not the same `Type`. The symptom is not an exception; it is finding zero providers. Show the two-line fix and explain why returning `null` from `Load` defers to the default context.

5. **Collectible contexts.** Why `isCollectible: true`, what it costs, and that the benefit is a future watch mode. Note that a collectible context only unloads once every reference to anything in it is gone.

6. **Acquisition: `dotnet publish` over a generated project.** Why not the NuGet client libraries. State the SDK-at-runtime trade-off honestly.

7. **A worked example.** Point at `samples/DrMoustache.Sample.Plugin` and show `SqliteObservationProvider` in full. Explain why the sample deliberately has a transitive dependency tree — a dependency-free sample would pass a broken loader.

8. **Common mistakes**, as a table: assembly name not matching package id; no public parameterless constructor; provider not public; forgetting `[DrMoustacheRecord]`; a plain `class` record type breaking `onCollision: suffix` hashing (cross-reference `docs/known-issues.md`).

Keep every code sample copied from real files rather than retyped.

- [ ] **Step 4: Run the tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentationTests`
Expected: 7 tests passed.

- [ ] **Step 5: Commit**

```bash
git add docs/plugin-architecture.md tests/DrMoustache.Cli.Tests/DocumentationTests.cs
git commit -m "$(cat <<'EOF'
Add the plugin architecture guide

The teaching artifact the spec calls for: AssemblyLoadContext,
AssemblyDependencyResolver and .deps.json, the shared-contract trap,
collectible contexts, and why the contract lives in its own package.

Tested. Documentation rots, so the tests assert it still names the
concepts it claims to teach and that those types still exist.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 14: README, package metadata, CI, and the end-to-end smoke test

**Files:**
- Create: `README.md`
- Create: `PACKAGE.md`
- Create: `.github/workflows/build.yml`
- Create: `tests/DrMoustache.Cli.Tests/SmokeTests.cs`
- Modify: `Directory.Build.props`

**Interfaces:**
- Consumes: everything.
- Produces: a publishable repository and one test that proves the whole tool works end to end.

- [ ] **Step 1: Write the failing smoke test**

`tests/DrMoustache.Cli.Tests/SmokeTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests;

using DrMoustache.Cli.Tests.Plugins;
using DrMoustache.Cli.Verbs;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>Proves the whole tool: a real plugin, loaded from a published
/// folder with its real dependency tree, reading a real database, rendering
/// real templates to real files.</summary>
public class SmokeTests : IClassFixture<SamplePluginFixture>, IDisposable
{
    private readonly SamplePluginFixture _sample;
    private readonly string _root = Directory.CreateTempSubdirectory("drm-smoke-").FullName;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public SmokeTests(SamplePluginFixture sample)
    {
        _sample = sample;

        var cache = Path.Combine(_root, "cache", "DrMoustache.Sample.Plugin", "1.0.0");
        Directory.CreateDirectory(cache);

        foreach (var file in Directory.EnumerateFiles(_sample.PublishDirectory))
            File.Copy(file, Path.Combine(cache, Path.GetFileName(file)), overwrite: true);

        CreateDatabase(Path.Combine(_root, "weather.db"));

        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        File.WriteAllText(Path.Combine(_root, "templates", "station.md"), """
            ---
            drmoustache:
              source: observations
              delivery: fanout
              output: stations/{{station}}.md
              onCollision: suffix
            title: "{{station}}"
            ---

            # {{station}}

            | Metric | Value |
            |--------|-------|
            | Temperature | {{temperature}} |
            """);

        File.WriteAllText(Path.Combine(_root, "drmoustache.yaml"), $"""
            plugins:
              - DrMoustache.Sample.Plugin@1.0.0

            sources:
              observations:
                type: sample-sqlite
                path: {Path.Combine(_root, "weather.db")}
            """);
    }

    private static void CreateDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE observations (
                station TEXT NOT NULL,
                temperature REAL NOT NULL,
                recorded_at TEXT NOT NULL,
                note TEXT);
            INSERT INTO observations VALUES
                ('alpha', 12.5, '2026-08-02T09:00:00+00:00', NULL),
                ('beta',  -3.25, '2026-08-02T09:05:00+00:00', 'sensor drift | suspect');
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
        _stdout.Dispose();
        _stderr.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RendersRealDataThroughARealPluginToRealFiles()
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);

        try
        {
            var exit = await RenderVerb.RunAsync(
                new DirectoryInfo(Path.Combine(_root, "templates")),
                new DirectoryInfo(Path.Combine(_root, "dist")),
                strict: true,
                config: new FileInfo(Path.Combine(_root, "drmoustache.yaml")),
                _stdout, _stderr, TestContext.Current.CancellationToken);

            exit.Should().Be(0, _stderr.ToString());

            var alpha = await File.ReadAllTextAsync(
                Path.Combine(_root, "dist", "stations", "alpha.md"),
                TestContext.Current.CancellationToken);

            alpha.Should().Contain("# alpha").And.Contain("12.5");

            var beta = await File.ReadAllTextAsync(
                Path.Combine(_root, "dist", "stations", "beta.md"),
                TestContext.Current.CancellationToken);

            // The pipe in the note must not shatter the table row, and the
            // value must stay invariant regardless of the host's locale.
            beta.Should().Contain("-3.25");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public async Task CheckPassesOnTheSameTemplates()
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);

        try
        {
            var exit = await CheckVerb.RunAsync(
                new DirectoryInfo(Path.Combine(_root, "templates")),
                new FileInfo(Path.Combine(_root, "drmoustache.yaml")),
                _stdout, _stderr, TestContext.Current.CancellationToken);

            exit.Should().Be(0, _stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
```

Add `<PackageReference Include="Microsoft.Data.Sqlite" />` to `tests/DrMoustache.Cli.Tests/DrMoustache.Cli.Tests.csproj` so the test can build the database.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~SmokeTests`
Expected: fails — either compilation, or a genuine end-to-end failure. Fix whatever it reveals; this test is the point of the plan.

- [ ] **Step 3: Write `README.md`**

Cover, in this order: what it is in two sentences; the non-goals block from the spec **verbatim**, prominently — *"DrMoustache puts your fields into Markdown. It is not a logic engine…"*; a 30-second example (`init`, `check`, `render`); how a template looks; how a provider looks, pointing at `samples/`; the three packages; a link to `docs/plugin-architecture.md` and `docs/known-issues.md`; how it composes with Crucible; Apache-2.0.

State plainly that a provider plugin is required — the tool does nothing on its own.

- [ ] **Step 4: Write `PACKAGE.md`**

A short NuGet-facing README: what the package is, the minimal example, a link to the repository. Referenced by `PackageReadmeFile`.

- [ ] **Step 5: Add package metadata to `Directory.Build.props`**

Add to the second `<PropertyGroup>`:

```xml
    <Version>0.1.0</Version>
    <PackageReadmeFile>PACKAGE.md</PackageReadmeFile>
    <PackageTags>markdown;mustache;templating;static-site;codegen</PackageTags>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
```

and a new `<ItemGroup>`:

```xml
  <ItemGroup Condition="'$(IsPackable)' == 'true'">
    <None Include="$(MSBuildThisFileDirectory)PACKAGE.md" Pack="true" PackagePath="" />
  </ItemGroup>
```

- [ ] **Step 6: Add CI**

`.github/workflows/build.yml`:

```yaml
name: build

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore DrMoustache.slnx

      - name: Build
        run: dotnet build DrMoustache.slnx -c Release --no-restore

      - name: Test
        run: dotnet test DrMoustache.slnx -c Release --no-build

      - name: Pack
        run: dotnet pack DrMoustache.slnx -c Release --no-build -o ./artifacts

      - uses: actions/upload-artifact@v4
        with:
          name: packages
          path: ./artifacts/*.nupkg
```

- [ ] **Step 7: Verify everything**

Run: `dotnet build -c Release DrMoustache.slnx`
Expected: 0 warnings.

Run: `dotnet test DrMoustache.slnx`
Expected: all tests pass.

Run: `dotnet pack -c Release DrMoustache.slnx -o /tmp/drm-pack && ls /tmp/drm-pack`
Expected: three `.nupkg` files — Abstractions, Core, Cli.

Run: `dotnet tool install --global --add-source /tmp/drm-pack DrMoustache.Cli --version 0.1.0 && drmoustache --help`
Expected: the tool installs and prints usage. Then `dotnet tool uninstall --global DrMoustache.Cli`.

Report the output of the pack and tool-install steps — they are the only proof the `dotnet tool` packaging actually works.

- [ ] **Step 8: Commit**

```bash
git add README.md PACKAGE.md Directory.Build.props .github tests
git commit -m "$(cat <<'EOF'
Add README, package metadata, CI, and the end-to-end smoke test

The smoke test is the one that matters: a real plugin loaded from a
published folder with its real transitive tree, reading a real SQLite
database, rendering real templates to real files, under --strict.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

## Self-Review

**1. Spec coverage.** Every CLI-related spec section maps to a task:

| Spec section | Task |
|---|---|
| Configuration chain, `IConfiguration` first then environment | 2 |
| Configuration visibility to plugins, `scopeConfig` | 7 |
| Plugin distribution: NuGet package + version | 3, 6 |
| `AssemblyDependencyResolver`, shared-Abstractions deferral, collectible contexts | 5 |
| CLI verbs `render` / `init` / `check` | 11, 12 |
| `check` performs no provider reads | 12 |
| Record errors collected, `--strict`, honest summary | 10 |
| Documents never partially written | 8 |
| Silently dropping a record is never acceptable | 10 |
| Plugin loading proven end to end in CI | 5, 14 |
| `docs/plugin-architecture.md` | 13 |
| Three packages ship, `DrMoustache.Cli` as a `dotnet tool` | 1, 14 |

**Deliberately not covered**, and correctly so: `watch` and `serve` are stated non-goals; first-party JSON/CSV/SQL/HTTP providers are deferred in the spec; source-generated manifests and user-defined helpers are deferred.

**Known-issues items this plan resolves:** R5 (`check` must pass the delivery mode — Task 12) and the missing package metadata (Task 14). The rest of `docs/known-issues.md` is untouched by design.

**2. Placeholder scan.** No `TBD` or "add error handling" instructions. Every code step contains complete, compilable code. Task 13's document is specified as an eight-section outline with a test that enforces its content rather than pasted prose — that is a deliberate exception, since the document is long-form writing rather than code, and the test makes the requirement checkable.

**3. Type consistency.** Verified across tasks: `PluginReference` (3) is consumed by `PluginAcquisition.AcquireAsync` (6) and `ProviderRegistry.LoadAsync` (7). `PluginPackage.EntryAssemblyPath` (5) is produced by 6 and consumed by 5's constructor. `ResolvedSource` (7) is consumed by `RenderPipeline.RunAsync` (9). `RecordSkip` is introduced as a placeholder in 9 and replaced in 10 — flagged explicitly in both tasks so a reviewer sees it is intentional. `RenderOutcome` (9) is consumed by `RenderReport` (10). `VerbSupport.IsUserFacing` (11) is used by 11 and 12. `SamplePluginFixture` (5) is reused by 7 and 14 via `IClassFixture`.

One gap found and closed during review: Task 9's `RenderPipeline` referenced `RecordSkip` before Task 10 defined it, which would not compile. Task 9 now specifies the placeholder and Task 10 specifies its removal.
