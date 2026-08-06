# DrMoustache v1 Hosting Implementation Plan — Part 1: Contract and Library

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the contract changes v1 requires, then build the `DrMoustache` metapackage — DI registration, template discovery, record validation, atomic writing, and the `RenderAsync` / `CheckAsync` surface.

**Architecture:** A provider is a DI-registered service; `AddSource<TProvider>("name")` registers it as a keyed singleton that a template's `source:` key resolves against. `IDocumentRenderer` discovers templates on disk, validates them against each source's manifest, drains the provider, hands everything to `DeliveryPlanner`, and writes the resulting documents atomically. `DrMoustache.Core` stays I/O-free; all filesystem work lives in the new package.

**Tech Stack:** .NET 10, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, xunit.v3, FluentAssertions 6.12.2.

**Part 2** (`2026-08-05-drmoustache-hosting-part2.md`) covers the two `dotnet new` templates, their CI verification, README, package metadata, and release.

## Global Constraints

- `TargetFramework`: `net10.0`; `LangVersion`: `preview`; `Nullable`: `enable`; `ImplicitUsings`: `enable`
- `TreatWarningsAsErrors`: `true`, `EnforceCodeStyleInBuild`: `true`, `AnalysisLevel`: `latest-all`
- Central package management — every `PackageReference` takes its version from `Directory.Packages.props`, never inline
- FluentAssertions pinned at **6.12.2** (last MIT release). Do not bump.
- Namespace root `DrMoustache`. File-scoped namespaces, `using` directives inside the namespace.
- **Fix the cause of any warning rather than suppressing it.** If a suppression is genuinely unavoidable, scope it tightly (never file-wide) and justify it at the site.
- `DrMoustache.Core` performs no I/O and its architecture tests enforce that. **All filesystem access added by this plan lives in the `DrMoustache` package.**
- Core's declared type and member names must not contain `Message`, `Queue`, or `Endpoint`.
- All 431 existing Core tests must keep passing except where a task explicitly changes an expectation, in which case the change must be stated and justified.
- Governing principle: **"Markdown is the layout, Mustache is the data."**
- Product priority: **rapid answers to data questions, flexible enough for any data set.**

## `dotnet new` packaging — verified, not recalled

Part 2 depends on this and it was confirmed end to end before writing the plan: pack → install → instantiate → build → run, with two templates in one package.

- `<PackageType>Template</PackageType>`, `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<ContentTargetFolders>content</ContentTargetFolders>`
- `<EnableDefaultItems>false</EnableDefaultItems>` plus an explicit `<Content Include="content/**/*" Exclude="content/**/bin/**;content/**/obj/**" />`, otherwise the template's own `.cs` files get compiled into the template package
- `<NoWarn>$(NoWarn);NU5128</NoWarn>` — a template package has no lib assemblies
- Multiple templates are sibling directories under `content/`, each with its own `.template.config/template.json`
- `sourceName` renames **both** file names and file contents
- `dotnet new install <path-to-nupkg>` / `dotnet new uninstall <PackageId>`

## Core API this plan calls

Verified against the shipped assemblies at `HEAD`.

```csharp
// DrMoustache.Core.Frontmatter
ParsedTemplate FrontmatterParser.Parse(string content);
sealed record ParsedTemplate(RenderConfig Config, string PassthroughFrontmatter, string Body);
sealed record RenderConfig(string Source, DeliveryMode Delivery, string Output,
                           CollisionPolicy? OnCollision);

// DrMoustache.Core.Template
IReadOnlyList<TemplateNode> TemplateLexer.Lex(string body);
sealed class TemplateException : Exception { int Line { get; } }

// DrMoustache.Core.Manifest
RecordManifest ManifestBuilder.FromType(Type recordType);
sealed record ManifestField(string Path, FieldType Type, bool IsNullable,
                            string? Description, IReadOnlyList<ManifestField> Children);

// DrMoustache.Core.Binding
bool ValueResolver.TryResolve(object? root, string path, out object? value);

// DrMoustache.Core.Delivery  — Task 1 adds the culture parameter
DeliveryResult DeliveryPlanner.Plan(
    RenderConfig config, IReadOnlyList<TemplateNode> outputPattern,
    IReadOnlyList<TemplateNode> body, string passthrough,
    IReadOnlyList<object> records, CultureInfo? culture = null);
sealed record DeliveryResult(IReadOnlyList<RenderedDocument> Documents,
                             IReadOnlyList<string> Warnings);

// DrMoustache.Core.Validation  — Task 1 makes delivery required
IReadOnlyList<string> TemplateValidator.Validate(
    IReadOnlyList<TemplateNode> nodes, RecordManifest manifest, DeliveryMode delivery);

// DrMoustache.Core.Rendering
sealed record RenderedDocument(string Path, string Content);
```

Two things that will not compile if assumed otherwise: `Plan` does **not** lex `config.Output` for you — pass the nodes — and after Task 1, `Validate` has no default for `delivery`.

## File Structure

```
src/DrMoustache/
  DrMoustache.csproj
  DrMoustacheOptions.cs                 options bound from configuration
  ServiceCollectionExtensions.cs        AddDrMoustache
  IDrMoustacheBuilder.cs                builder + AddSource<T>
  SourceRegistration.cs                 what AddSource recorded
  Discovery/TemplateFile.cs             find and parse templates on disk
  Discovery/RenderException.cs          faults attributable to one template
  Validation/RecordValidator.cs         record vs its own manifest
  Output/DocumentWriter.cs              atomic writes
  Output/OutputException.cs
  Rendering/IDocumentRenderer.cs        the public surface
  Rendering/DocumentRenderer.cs         orchestration
  Rendering/RenderResult.cs             RenderResult, CheckResult, RecordSkip

tests/DrMoustache.Tests/
  DrMoustache.Tests.csproj
  RegistrationTests.cs
  Discovery/TemplateFileTests.cs
  Validation/RecordValidatorTests.cs
  Output/DocumentWriterTests.cs
  Rendering/DocumentRendererTests.cs
```

---

### Task 1: Contract changes to Abstractions and Core

Three breaking changes, together in one task because they are all "adjust what exists" and a reviewer would accept or reject them as a set.

**Files:**
- Delete: `src/DrMoustache.Abstractions/ProviderContext.cs`
- Modify: `src/DrMoustache.Abstractions/IDataProvider.cs`
- Modify: `src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj`
- Modify: `src/DrMoustache.Core/Validation/TemplateValidator.cs`
- Modify: `src/DrMoustache.Core/Delivery/DeliveryPlanner.cs`
- Modify: `tests/DrMoustache.Core.Tests/ContractTests.cs`
- Modify: `tests/DrMoustache.Core.Tests/Validation/TemplateValidatorTests.cs`
- Modify: `tests/DrMoustache.Core.Tests/Delivery/DeliveryPlannerTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `IDataProvider.ReadAsync(CancellationToken ct)` — no `ProviderContext`
  - `DataProvider<TRecord>.ReadAsync(CancellationToken ct)`
  - `TemplateValidator.Validate(nodes, manifest, DeliveryMode delivery)` — required parameter
  - `DeliveryPlanner.Plan(config, outputPattern, body, passthrough, records, CultureInfo? culture = null)`

**Why each change:**

1. **`ProviderContext` is deleted.** It supplied an `IConfiguration`, that source's section, and an `ILogger`. Under DI all three arrive by constructor injection, so it now hands a provider three things it already has.
2. **`Validate`'s `DeliveryMode` default is removed.** A `collate` template validated as `Fanout` reports `count` and `records` as unknown fields and then never descends into the loop body — failing valid templates while passing broken ones (`docs/known-issues.md` R5). There is now a real caller; the compiler should ask.
3. **`Plan` gains an optional culture.** `DrMoustacheOptions.Culture` has nowhere to go otherwise: `Plan` is what calls `TemplateRenderer.Render` internally, and `Render`'s culture parameter is already optional. Without this the option would silently do nothing.

A pleasing consequence of (1): **`DrMoustache.Abstractions` ends up with zero package dependencies.** `ProviderContext` was the only consumer of `Microsoft.Extensions.Configuration.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`. A contract package that depends on nothing is exactly what you want plugin authors referencing.

- [ ] **Step 1: Update the contract tests first**

In `tests/DrMoustache.Core.Tests/ContractTests.cs`, delete the `ProviderContext` construction and the `TypedBaseBridgesToObjectSequence` test's context argument. The nested `SampleProvider` becomes:

```csharp
    private sealed class SampleProvider : DataProvider<Sample>
    {
        public override string Name => "sample-provider";

        public override async IAsyncEnumerable<Sample> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Sample { Id = "a" };
        }
    }
```

and the bridging test:

```csharp
    [Fact]
    public async Task TypedBaseBridgesToObjectSequence()
    {
        IDataProvider provider = new SampleProvider();

        var items = new List<object>();
        await foreach (var item in provider.ReadAsync(TestContext.Current.CancellationToken))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().BeOfType<Sample>();
    }
```

Add a test asserting the dependency claim, since it is a deliberate property rather than an accident:

```csharp
    [Fact]
    public void AbstractionsHasNoPackageDependencies()
    {
        var referenced = typeof(IDataProvider).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null
                && !n.StartsWith("System.", StringComparison.Ordinal)
                && !string.Equals(n, "netstandard", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEmpty(
            "the contract package must be referenceable without dragging anything in");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ContractTests`
Expected: compilation failure — `ReadAsync` still requires a `ProviderContext`.

- [ ] **Step 3: Change the contract**

Delete `src/DrMoustache.Abstractions/ProviderContext.cs`.

Rewrite `src/DrMoustache.Abstractions/IDataProvider.cs`:

```csharp
namespace DrMoustache.Abstractions;

using System.Runtime.CompilerServices;

/// <summary>Supplies records to the rendering engine.</summary>
/// <remarks>
/// <para>Read-only: implementations must not mutate their underlying source.
/// There is deliberately no write, delete, or acknowledge surface. A plausible
/// early use is browsing a dead-letter queue, where a destructive read is
/// unrecoverable.</para>
/// <para>Providers are dependency-injected services. Declare what you need —
/// configuration, options, logging, a client, a database context — in your
/// constructor.</para>
/// </remarks>
public interface IDataProvider
{
    /// <summary>Matched against a template's <c>source:</c> setting, and the
    /// name this provider was registered under.</summary>
    string Name { get; }

    /// <summary>The <see cref="DrMoustacheRecordAttribute"/>-annotated type
    /// this provider yields.</summary>
    Type RecordType { get; }

    /// <summary>Reads every record. May stream; the host materializes.</summary>
    /// <param name="ct">Cancels the read.</param>
    IAsyncEnumerable<object> ReadAsync(CancellationToken ct);
}

/// <summary>Base class for providers. Derive from this, not from
/// <see cref="IDataProvider"/> directly.</summary>
/// <typeparam name="TRecord">The annotated record type this provider yields.</typeparam>
public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public Type RecordType => typeof(TRecord);

    /// <summary>Reads every record. May stream; the host materializes.</summary>
    /// <param name="ct">Cancels the read.</param>
    public abstract IAsyncEnumerable<TRecord> ReadAsync(CancellationToken ct);

    async IAsyncEnumerable<object> IDataProvider.ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in ReadAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            yield return record;
    }
}
```

In `src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj`, delete both `PackageReference` elements, leaving an empty `ItemGroup` to remove or the element removed entirely.

- [ ] **Step 4: Make `Validate`'s delivery mode required**

In `src/DrMoustache.Core/Validation/TemplateValidator.cs`, change the signature from
`DeliveryMode delivery = DeliveryMode.Fanout` to `DeliveryMode delivery`, and add to the
XML docs:

```csharp
    /// <param name="delivery">The template's own delivery mode. Required rather
    /// than defaulted: a collate template validated as fanout reports 'count'
    /// and 'records' as unknown fields and then never descends into the loop
    /// body, so it fails valid templates while passing broken ones.</param>
```

Update every call site in `tests/DrMoustache.Core.Tests/Validation/TemplateValidatorTests.cs` to pass a mode explicitly. The existing helper becomes:

```csharp
    private static IReadOnlyList<string> Validate(
        string template, DeliveryMode delivery = DeliveryMode.Fanout)
        => TemplateValidator.Validate(
            TemplateLexer.Lex(template), ManifestBuilder.FromType<Thing>(), delivery);
```

The default moves to the **test helper**, where it is a convenience, rather than living on the production API where it is a trap.

- [ ] **Step 5: Add the culture parameter to `Plan`**

In `src/DrMoustache.Core/Delivery/DeliveryPlanner.cs`, add `CultureInfo? culture = null` as the final parameter of `Plan`, thread it to every internal `TemplateRenderer.Render` call, and document it:

```csharp
    /// <param name="culture">Formatting culture. Null means invariant, so
    /// output is byte-identical across machines.</param>
```

Add a test to `tests/DrMoustache.Core.Tests/Delivery/DeliveryPlannerTests.cs`:

```csharp
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
```

with the fixture record alongside the file's existing ones:

```csharp
    [DrMoustacheRecord("money")]
    private sealed record Money
    {
        [Field] public required string Id { get; init; }
        [Field] public required decimal Amount { get; init; }

        public Money(string id, decimal amount) { Id = id; Amount = amount; }
    }
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet build DrMoustache.slnx`
Expected: 0 warnings.

Run: `dotnet test DrMoustache.slnx`
Expected: all tests pass. Test count rises by 2 (the dependency assertion and the culture test).

If a test outside the three files listed above fails, **stop and report** — that would mean the contract change reaches further than expected and I need to know.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache.Abstractions src/DrMoustache.Core tests/DrMoustache.Core.Tests
git commit -m "$(cat <<'EOF'
Drop ProviderContext, require DeliveryMode, thread culture through Plan

Providers are DI services now, so ProviderContext handed them three things
they already have. Deleting it leaves Abstractions with zero package
dependencies -- exactly what a contract package should be, and now asserted
by a test.

TemplateValidator's DeliveryMode default is removed: a collate template
validated as fanout fails valid templates and passes broken ones
(known-issues R5). There is a real caller now, so the compiler should ask.

DeliveryPlanner.Plan gains an optional culture. Without it the hosting
package's Culture option would silently do nothing, since Plan is what
calls TemplateRenderer.Render.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 2: The `DrMoustache` package and its options

**Files:**
- Create: `src/DrMoustache/DrMoustache.csproj`
- Create: `src/DrMoustache/DrMoustacheOptions.cs`
- Create: `tests/DrMoustache.Tests/DrMoustache.Tests.csproj`
- Create: `tests/DrMoustache.Tests/OptionsTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `DrMoustache.slnx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public sealed class DrMoustacheOptions` with `TemplateDirectory`, `OutputDirectory`, `Strict`, `Culture`.

- [ ] **Step 1: Add package versions**

In `Directory.Packages.props`, add to the first `<ItemGroup>`:

```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
```

`Microsoft.Extensions.Logging.Abstractions` is already present.

- [ ] **Step 2: Create the project**

`src/DrMoustache/DrMoustache.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <RootNamespace>DrMoustache</RootNamespace>
    <Description>Renders Markdown templates against data from your own providers. Values are escaped by their position in the document, so data cannot reshape the page.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <ProjectReference Include="../DrMoustache.Abstractions/DrMoustache.Abstractions.csproj" />
    <ProjectReference Include="../DrMoustache.Core/DrMoustache.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DrMoustache.Tests" />
  </ItemGroup>
</Project>
```

The two `ProjectReference`s are what make this a metapackage: `dotnet pack` turns them into package dependencies, so referencing `DrMoustache` brings Abstractions and Core with it.

- [ ] **Step 3: Create the test project**

`tests/DrMoustache.Tests/DrMoustache.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <RootNamespace>DrMoustache.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/DrMoustache/DrMoustache.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add both to `DrMoustache.slnx`**

Add to the `/src/` folder:
```xml
    <Project Path="src/DrMoustache/DrMoustache.csproj" />
```
and to the `/tests/` folder:
```xml
    <Project Path="tests/DrMoustache.Tests/DrMoustache.Tests.csproj" />
```

- [ ] **Step 5: Write the failing test**

`tests/DrMoustache.Tests/OptionsTests.cs`:

```csharp
namespace DrMoustache.Tests;

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

public class OptionsTests
{
    [Fact]
    public void DefaultsAreUsableWithNoConfiguration()
    {
        var options = new DrMoustacheOptions();

        options.TemplateDirectory.Should().Be("templates");
        options.OutputDirectory.Should().Be("dist");
        options.Strict.Should().BeFalse();
        options.Culture.Should().BeNull("null means invariant");
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DrMoustache:TemplateDirectory"] = "pages",
                ["DrMoustache:OutputDirectory"] = "out",
                ["DrMoustache:Strict"] = "true",
            })
            .Build();

        var options = new DrMoustacheOptions();
        configuration.GetSection("DrMoustache").Bind(options);

        options.TemplateDirectory.Should().Be("pages");
        options.OutputDirectory.Should().Be("out");
        options.Strict.Should().BeTrue();
    }

    [Fact]
    public void CultureIsSettableForDeliberateLocalisation()
    {
        var options = new DrMoustacheOptions
        {
            Culture = CultureInfo.GetCultureInfo("de-DE"),
        };

        options.Culture!.Name.Should().Be("de-DE");
    }
}
```

`Bind` needs `Microsoft.Extensions.Configuration.Binder`, which
`Microsoft.Extensions.Options.ConfigurationExtensions` brings transitively.

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~OptionsTests`
Expected: compilation failure — `DrMoustacheOptions` does not exist.

- [ ] **Step 7: Write `DrMoustacheOptions`**

`src/DrMoustache/DrMoustacheOptions.cs`:

```csharp
namespace DrMoustache;

using System.Globalization;

/// <summary>Configures a render. Binds from a <c>DrMoustache</c>
/// configuration section with no extra work.</summary>
public sealed class DrMoustacheOptions
{
    /// <summary>Directory searched recursively for templates.</summary>
    public string TemplateDirectory { get; set; } = "templates";

    /// <summary>Directory rendered documents are written beneath.</summary>
    public string OutputDirectory { get; set; } = "dist";

    /// <summary>Treat any skipped record as a failure. The CI posture.</summary>
    /// <remarks>A skip means a record never reached the output. A warning means
    /// something was resolved without losing data, so warnings never fail.</remarks>
    public bool Strict { get; set; }

    /// <summary>Formatting culture for numbers and dates.</summary>
    /// <remarks>Null means <see cref="CultureInfo.InvariantCulture"/>, so the same
    /// templates and data produce byte-identical output on every machine. Set this
    /// only to localise deliberately.</remarks>
    public CultureInfo? Culture { get; set; }
}
```

- [ ] **Step 8: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~OptionsTests`
Expected: 3 tests passed.

Run: `dotnet build DrMoustache.slnx`
Expected: 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props DrMoustache.slnx src/DrMoustache tests/DrMoustache.Tests
git commit -m "$(cat <<'EOF'
Add the DrMoustache package and its options

Two ProjectReferences make it a metapackage: pack turns them into package
dependencies, so referencing DrMoustache brings Abstractions and Core.

Options are a plain class so they bind from a DrMoustache configuration
section with no extra work. Null culture means invariant.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 3: DI registration

**Files:**
- Create: `src/DrMoustache/SourceRegistration.cs`
- Create: `src/DrMoustache/IDrMoustacheBuilder.cs`
- Create: `src/DrMoustache/ServiceCollectionExtensions.cs`
- Test: `tests/DrMoustache.Tests/RegistrationTests.cs`

**Interfaces:**
- Consumes: `DrMoustacheOptions` (Task 2); `IDataProvider` (Abstractions).
- Produces:
  - `public sealed record SourceRegistration(string Name, Type ProviderType)`
  - `public interface IDrMoustacheBuilder` — `IServiceCollection Services { get; }`, `IDrMoustacheBuilder AddSource<TProvider>(string name) where TProvider : class, IDataProvider`
  - `public static IDrMoustacheBuilder ServiceCollectionExtensions.AddDrMoustache(this IServiceCollection services, Action<DrMoustacheOptions>? configure = null)`

`AddSource` registers the provider as a **keyed** singleton under `name`, and registers a `SourceRegistration` singleton alongside. The second exists because keyed DI cannot enumerate its own keys — without it, a template naming an unregistered source gets a bare lookup failure instead of a message listing what *is* registered. This is deliberate, not redundant.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Tests/RegistrationTests.cs`:

```csharp
namespace DrMoustache.Tests;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class RegistrationTests
{
    [DrMoustacheRecord("station")]
    private sealed record Station
    {
        [Field] public required string Name { get; init; }
    }

    private sealed class StationProvider : DataProvider<Station>
    {
        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Station { Name = "alpha" };
        }
    }

    private sealed class OtherProvider : DataProvider<Station>
    {
        public override string Name => "other";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    /// <summary>Takes a dependency, to prove providers are constructed by DI.</summary>
    private sealed class InjectedProvider(IOptions<DrMoustacheOptions> options)
        : DataProvider<Station>
    {
        public string SeenOutputDirectory => options.Value.OutputDirectory;

        public override string Name => "injected";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    private static ServiceProvider Build(Action<IDrMoustacheBuilder> configure,
        Action<DrMoustacheOptions>? options = null)
    {
        var services = new ServiceCollection();
        configure(services.AddDrMoustache(options));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvesAProviderByItsRegisteredName()
    {
        using var sp = Build(b => b.AddSource<StationProvider>("stations"));

        sp.GetRequiredKeyedService<IDataProvider>("stations")
            .Should().BeOfType<StationProvider>();
    }

    [Fact]
    public void KeepsSeveralSourcesApart()
    {
        using var sp = Build(b => b
            .AddSource<StationProvider>("stations")
            .AddSource<OtherProvider>("other"));

        sp.GetRequiredKeyedService<IDataProvider>("stations").Should().BeOfType<StationProvider>();
        sp.GetRequiredKeyedService<IDataProvider>("other").Should().BeOfType<OtherProvider>();
    }

    [Fact]
    public void RecordsWhatWasRegisteredSoErrorsCanListIt()
    {
        using var sp = Build(b => b
            .AddSource<StationProvider>("stations")
            .AddSource<OtherProvider>("other"));

        sp.GetRequiredService<IEnumerable<SourceRegistration>>()
            .Select(r => r.Name)
            .Should().BeEquivalentTo(["stations", "other"]);
    }

    [Fact]
    public void ProvidersAreConstructedByDependencyInjection()
    {
        using var sp = Build(
            b => b.AddSource<InjectedProvider>("injected"),
            o => o.OutputDirectory = "somewhere");

        sp.GetRequiredKeyedService<IDataProvider>("injected")
            .Should().BeOfType<InjectedProvider>()
            .Which.SeenOutputDirectory.Should().Be("somewhere");
    }

    [Fact]
    public void ProvidersAreSingletons()
    {
        using var sp = Build(b => b.AddSource<StationProvider>("stations"));

        sp.GetRequiredKeyedService<IDataProvider>("stations")
            .Should().BeSameAs(sp.GetRequiredKeyedService<IDataProvider>("stations"));
    }

    [Fact]
    public void OptionsAreResolvableAndCarryTheConfiguredValues()
    {
        using var sp = Build(_ => { }, o => o.Strict = true);

        sp.GetRequiredService<IOptions<DrMoustacheOptions>>()
            .Value.Strict.Should().BeTrue();
    }

    [Fact]
    public void OptionsAreResolvableWithNoConfigureCallback()
    {
        using var sp = Build(_ => { });

        sp.GetRequiredService<IOptions<DrMoustacheOptions>>()
            .Value.OutputDirectory.Should().Be("dist");
    }

    [Fact]
    public void RegisteringTheSameNameTwiceIsRejected()
    {
        var services = new ServiceCollection();
        var builder = services.AddDrMoustache().AddSource<StationProvider>("stations");

        var act = () => builder.AddSource<OtherProvider>("stations");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*stations*already*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSourceNameIsRejected(string name)
    {
        var services = new ServiceCollection();
        var builder = services.AddDrMoustache();

        var act = () => builder.AddSource<StationProvider>(name);

        act.Should().Throw<ArgumentException>();
    }
}
```

`RegisteringTheSameNameTwiceIsRejected` matters: keyed DI would silently let the second registration win, and a template would then bind to a provider the author did not intend.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RegistrationTests`
Expected: compilation failure — `AddDrMoustache` does not exist.

- [ ] **Step 3: Write `SourceRegistration`**

`src/DrMoustache/SourceRegistration.cs`:

```csharp
namespace DrMoustache;

/// <summary>Records that a source name was registered.</summary>
/// <remarks>Keyed dependency injection cannot enumerate its own keys, so this
/// exists to let a failure name the sources that <em>are</em> registered rather
/// than only the one that is not. Deliberate, not redundant.</remarks>
/// <param name="Name">The name a template's <c>source:</c> resolves against.</param>
/// <param name="ProviderType">The provider registered under that name.</param>
public sealed record SourceRegistration(string Name, Type ProviderType);
```

- [ ] **Step 4: Write `IDrMoustacheBuilder`**

`src/DrMoustache/IDrMoustacheBuilder.cs`:

```csharp
namespace DrMoustache;

using DrMoustache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the data sources templates bind to.</summary>
public interface IDrMoustacheBuilder
{
    /// <summary>The underlying collection, for further registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>Registers a provider under the name templates reference
    /// with their <c>source:</c> key.</summary>
    /// <typeparam name="TProvider">Constructed by dependency injection, so it
    /// may take whatever it needs in its constructor.</typeparam>
    /// <param name="name">Must be unique within this builder.</param>
    IDrMoustacheBuilder AddSource<TProvider>(string name)
        where TProvider : class, IDataProvider;
}
```

- [ ] **Step 5: Write `ServiceCollectionExtensions`**

`src/DrMoustache/ServiceCollectionExtensions.cs`:

```csharp
namespace DrMoustache;

using DrMoustache.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Adds DrMoustache to a service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the renderer and returns a builder for adding sources.</summary>
    public static IDrMoustacheBuilder AddDrMoustache(
        this IServiceCollection services,
        Action<DrMoustacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null) services.AddOptions<DrMoustacheOptions>();
        else services.AddOptions<DrMoustacheOptions>().Configure(configure);

        return new DrMoustacheBuilder(services);
    }

    private sealed class DrMoustacheBuilder(IServiceCollection services) : IDrMoustacheBuilder
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public IServiceCollection Services { get; } = services;

        public IDrMoustacheBuilder AddSource<TProvider>(string name)
            where TProvider : class, IDataProvider
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!_names.Add(name))
            {
                throw new InvalidOperationException(
                    $"a source named '{name}' is already registered. "
                    + "Source names must be unique, because a template's "
                    + "'source:' key resolves against exactly one provider.");
            }

            Services.AddKeyedSingleton<IDataProvider, TProvider>(name);
            Services.AddSingleton(new SourceRegistration(name, typeof(TProvider)));

            return this;
        }
    }
}
```

The duplicate check lives on the builder instance rather than the service collection, so it catches the realistic mistake — two `AddSource` calls in one chain — without falsely rejecting a second `AddDrMoustache` in a test.

- [ ] **Step 6: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RegistrationTests`
Expected: 10 tests passed (the `[Theory]` contributes 2).

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache tests/DrMoustache.Tests
git commit -m "$(cat <<'EOF'
Add DI registration

AddSource registers a keyed singleton plus a SourceRegistration. The second
exists because keyed DI cannot enumerate its own keys, so without it a
template naming an unregistered source gets a bare lookup failure instead
of a message listing what is registered.

Duplicate names are rejected: keyed DI would silently let the second
registration win and a template would bind to a provider the author did
not intend.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 4: Atomic document writing

**Files:**
- Create: `src/DrMoustache/Output/OutputException.cs`
- Create: `src/DrMoustache/Output/DocumentWriter.cs`
- Test: `tests/DrMoustache.Tests/Output/DocumentWriterTests.cs`

**Interfaces:**
- Consumes: `RenderedDocument` (Core).
- Produces: `internal sealed class DocumentWriter(string outputRoot)` with `Task<int> WriteAllAsync(IReadOnlyList<RenderedDocument> documents, CancellationToken ct)`; `public sealed class OutputException : Exception`.

The spec, verbatim: *"Documents are never partially written. A record renders fully to memory and reaches disk only when complete, so a mid-render failure leaves no half-written file and re-running is always safe."*

Rendering to memory already happens in Core. This closes the remaining gap: a write that fails partway — disk full, process killed — must not leave a truncated file that looks valid. Write to a temporary file **in the destination directory**, then `File.Move` with overwrite, which is atomic on one volume.

Containment is re-checked here even though `DeliveryPlanner` already slugifies per value, because this is the last place a bug anywhere upstream could escape the output directory.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Tests/Output/DocumentWriterTests.cs`:

```csharp
namespace DrMoustache.Tests.Output;

using DrMoustache.Core.Rendering;
using DrMoustache.Output;
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

        (await act.Should().ThrowAsync<OutputException>()).WithMessage("*outside*");
    }

    [Fact]
    public async Task AnEmptyListWritesNothingAndCreatesNoRoot()
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

- [ ] **Step 3: Write `OutputException`**

`src/DrMoustache/Output/OutputException.cs`:

```csharp
namespace DrMoustache.Output;

/// <summary>A document could not be written.</summary>
public sealed class OutputException : Exception
{
    public OutputException() { }

    public OutputException(string message) : base(message) { }

    public OutputException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 4: Write `DocumentWriter`**

`src/DrMoustache/Output/DocumentWriter.cs`:

```csharp
namespace DrMoustache.Output;

using System.Text;
using DrMoustache.Core.Rendering;

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
    /// this should be unreachable. It is the last place a bug anywhere upstream
    /// could write outside the output directory, which makes a second check
    /// worth its cost.</remarks>
    private static string Resolve(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': absolute paths fall outside "
                + $"the output directory '{root}'");
        }

        var combined = Path.GetFullPath(Path.Combine(root, relativePath));

        var rooted = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rooted, StringComparison.Ordinal))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': it resolves outside "
                + $"the output directory '{root}'");
        }

        return combined;
    }
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentWriterTests`
Expected: 9 tests passed (the `[Theory]` contributes 3).

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache/Output tests/DrMoustache.Tests/Output
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

### Task 5: Template discovery

**Files:**
- Create: `src/DrMoustache/Discovery/RenderException.cs`
- Create: `src/DrMoustache/Discovery/TemplateFile.cs`
- Test: `tests/DrMoustache.Tests/Discovery/TemplateFileTests.cs`

**Interfaces:**
- Consumes: `FrontmatterParser`, `ParsedTemplate`, `TemplateException` (Core).
- Produces: `internal sealed record TemplateFile(string Path, ParsedTemplate Parsed)` with `static IReadOnlyList<TemplateFile> Discover(string directory)`; `public sealed class RenderException : Exception`.

Templates are `*.md` found recursively, **excluding** anything under a directory beginning `_` (a partials convention), and **excluding files with no `drmoustache:` block** — a template directory legitimately contains hand-written pages too, and failing on those would make the tool unusable alongside ordinary docs.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Tests/Discovery/TemplateFileTests.cs`:

```csharp
namespace DrMoustache.Tests.Discovery;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Discovery;
using FluentAssertions;
using Xunit;

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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateFileTests`
Expected: compilation failure — `TemplateFile` does not exist.

- [ ] **Step 3: Write `RenderException`**

`src/DrMoustache/Discovery/RenderException.cs`:

```csharp
namespace DrMoustache.Discovery;

/// <summary>A fault attributable to one template file.</summary>
public sealed class RenderException : Exception
{
    public RenderException() { }

    public RenderException(string message) : base(message) { }

    public RenderException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 4: Write `TemplateFile`**

`src/DrMoustache/Discovery/TemplateFile.cs`:

```csharp
namespace DrMoustache.Discovery;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;

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
        => System.IO.Path.GetRelativePath(root, path)
            .Split(System.IO.Path.DirectorySeparatorChar,
                   System.IO.Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => segment.StartsWith('_'));
}
```

The `System.IO.Path` qualification is needed because the record's own `Path` property shadows the type name inside the record body.

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateFileTests`
Expected: 10 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache/Discovery tests/DrMoustache.Tests/Discovery
git commit -m "$(cat <<'EOF'
Add template discovery

Markdown without a drmoustache: block is skipped rather than rejected --
a template directory legitimately holds hand-written pages too, and
failing on those would make the tool unusable alongside ordinary docs.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 6: Record validation

**Files:**
- Create: `src/DrMoustache/Rendering/RenderResult.cs`
- Create: `src/DrMoustache/Validation/RecordValidator.cs`
- Test: `tests/DrMoustache.Tests/Validation/RecordValidatorTests.cs`

**Interfaces:**
- Consumes: `RecordManifest`, `ManifestField`, `FieldType`, `ManifestBuilder`, `ValueResolver` (Core).
- Produces:
  - `public sealed record RecordSkip(string Source, int Ordinal, string Reason)`
  - `public sealed record RenderResult(int DocumentsWritten, IReadOnlyList<string> Warnings, IReadOnlyList<RecordSkip> Skips, bool Success)` with a computed `string Summary`
  - `public sealed record CheckResult(int TemplatesChecked, IReadOnlyList<string> Problems)` with computed `bool Success` and `string Summary`
  - `internal static class RecordValidator` with `static string? Validate(object record, RecordManifest manifest)`

C#'s `required` is a compile-time guarantee only. A provider that deserializes or reflects can still yield null in a non-nullable field, and Core renders that as an empty string — a report silently missing data it claims to contain. This catches it before anything is written.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Tests/Validation/RecordValidatorTests.cs`:

```csharp
namespace DrMoustache.Tests.Validation;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using DrMoustache.Validation;
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
        => RecordValidator.Validate(new Thing { OrderId = "a", Total = 1m }, Manifest)
            .Should().BeNull();

    [Fact]
    public void AcceptsANullNullableField()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Note = null }, Manifest)
            .Should().BeNull();

    [Fact]
    public void RejectsANullRequiredField()
        => RecordValidator.Validate(new Thing { OrderId = null!, Total = 1m }, Manifest)
            .Should().Contain("orderId").And.Contain("required");

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

- [ ] **Step 3: Write the result types**

`src/DrMoustache/Rendering/RenderResult.cs`:

```csharp
namespace DrMoustache.Rendering;

using System.Globalization;
using System.Text;

/// <param name="Source">Which source the record came from. One project can
/// render several sources in one pass.</param>
/// <param name="Ordinal">Zero-based position in the provider's sequence — the
/// only stable identifier available before a record is bound.</param>
public sealed record RecordSkip(string Source, int Ordinal, string Reason);

/// <param name="Success">Accounts for <see cref="DrMoustacheOptions.Strict"/>,
/// so a caller does not have to remember to compare skips against an option
/// set somewhere else.</param>
public sealed record RenderResult(
    int DocumentsWritten,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecordSkip> Skips,
    bool Success)
{
    /// <summary>A human-readable report of what happened.</summary>
    public string Summary
    {
        get
        {
            var sb = new StringBuilder();

            sb.Append(CultureInfo.InvariantCulture,
                $"rendered {DocumentsWritten} document{(DocumentsWritten == 1 ? "" : "s")}");

            if (Skips.Count > 0)
                sb.Append(CultureInfo.InvariantCulture, $"  ({Skips.Count} skipped)");

            sb.AppendLine();

            foreach (var skip in Skips)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  skipped  {skip.Source} record {skip.Ordinal}  {skip.Reason}");
            }

            foreach (var warning in Warnings)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  warning  {warning}");

            return sb.ToString();
        }
    }
}

public sealed record CheckResult(int TemplatesChecked, IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;

    public string Summary
    {
        get
        {
            if (Success)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"checked {TemplatesChecked} template"
                    + $"{(TemplatesChecked == 1 ? "" : "s")} — no problems found")
                    + Environment.NewLine;
            }

            var sb = new StringBuilder();

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"checked {TemplatesChecked} template"
                + $"{(TemplatesChecked == 1 ? "" : "s")} — {Problems.Count} problem"
                + $"{(Problems.Count == 1 ? "" : "s")}");

            foreach (var problem in Problems)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {problem}");

            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Write `RecordValidator`**

`src/DrMoustache/Validation/RecordValidator.cs`:

```csharp
namespace DrMoustache.Validation;

using DrMoustache.Core.Binding;
using DrMoustache.Core.Manifest;

/// <summary>Checks a record against the shape its own manifest declares.</summary>
/// <remarks>C#'s <c>required</c> is a compile-time guarantee only. A provider
/// that deserializes or reflects can still yield null in a non-nullable field,
/// and the renderer would emit an empty string — a report silently missing data
/// it claims to contain. This catches that before anything is written.</remarks>
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

Collections are deliberately not descended into: a collection of N items would cost N × fields checks per record, and a malformed element renders as a blank cell rather than a missing document. Note this in your report.

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RecordValidatorTests`
Expected: 6 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache tests/DrMoustache.Tests/Validation
git commit -m "$(cat <<'EOF'
Add record validation and the result types

C#'s required is compile-time only, so a provider that deserializes can
still yield null in a non-nullable field. Core renders that as empty --
a report silently missing data it claims to contain -- so records are
checked against their own manifest before anything is written.

Success lives on RenderResult rather than being re-derived, because it
depends on Strict and a caller who must remember that will forget.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 7: The renderer

**Files:**
- Create: `src/DrMoustache/Rendering/IDocumentRenderer.cs`
- Create: `src/DrMoustache/Rendering/DocumentRenderer.cs`
- Modify: `src/DrMoustache/ServiceCollectionExtensions.cs` (register the renderer)
- Test: `tests/DrMoustache.Tests/Rendering/DocumentRendererTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–6, plus Core's `TemplateLexer`, `DeliveryPlanner`, `TemplateValidator`, `ManifestBuilder`.
- Produces: `public interface IDocumentRenderer` with `Task<RenderResult> RenderAsync(CancellationToken ct = default)` and `Task<CheckResult> CheckAsync(CancellationToken ct = default)`.

**Three behaviours that must hold:**

1. **`RenderAsync` validates before reading.** Template faults are static and affect every record, so they fail before a single provider is touched.
2. **Each template's own `DeliveryMode` reaches the validator.** A `collate` template validated as `Fanout` fails valid templates and passes broken ones. Task 1 removed the default so the compiler enforces passing it; this is where the correct value comes from.
3. **A provider fault ends that source's read but keeps what was already drained.** An iterator cannot be resumed after it throws.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Tests/Rendering/DocumentRendererTests.cs`:

```csharp
namespace DrMoustache.Tests.Rendering;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using DrMoustache.Discovery;
using DrMoustache.Rendering;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class DocumentRendererTests : IDisposable
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
        result.Skips.Should().ContainSingle()
            .Which.Reason.Should().Contain("the well ran dry");
        result.Skips[0].Source.Should().Be("stations");
        File.Exists(Path.Combine(_output, "stations", "good.md")).Should().BeTrue();
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

        var act = async () => await Renderer<StationProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

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

        var act = async () => await Renderer<ThrowingProvider>()
            .RenderAsync(TestContext.Current.CancellationToken);

        // ThrowingProvider would throw if it were read. It must not be.
        (await act.Should().ThrowAsync<RenderException>())
            .And.Message.Should().Contain("readign");
    }
}
```

`RenderFailsOnAnInvalidTemplateBeforeReadingAnyData` proves ordering by using a provider that throws — if the message mentions the well running dry rather than the typo, validation ran too late.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentRendererTests`
Expected: compilation failure — `IDocumentRenderer` does not exist.

- [ ] **Step 3: Write `IDocumentRenderer`**

`src/DrMoustache/Rendering/IDocumentRenderer.cs`:

```csharp
namespace DrMoustache.Rendering;

/// <summary>Renders every discovered template against its registered source.</summary>
public interface IDocumentRenderer
{
    /// <summary>Validates, reads, renders, and writes.</summary>
    /// <remarks>Validation runs first: template faults are static and affect
    /// every record, so they fail before any provider is touched.</remarks>
    Task<RenderResult> RenderAsync(CancellationToken ct = default);

    /// <summary>Validates every template against its source's manifest without
    /// reading any data or writing any output.</summary>
    Task<CheckResult> CheckAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Write `DocumentRenderer`**

`src/DrMoustache/Rendering/DocumentRenderer.cs`:

```csharp
namespace DrMoustache.Rendering;

using DrMoustache.Abstractions;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;
using DrMoustache.Core.Validation;
using DrMoustache.Discovery;
using DrMoustache.Output;
using DrMoustache.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal sealed class DocumentRenderer(
    IServiceProvider services,
    IEnumerable<SourceRegistration> registrations,
    IOptions<DrMoustacheOptions> options) : IDocumentRenderer
{
    private readonly List<SourceRegistration> _registrations = [.. registrations];
    private readonly DrMoustacheOptions _options = options.Value;

    public async Task<RenderResult> RenderAsync(CancellationToken ct = default)
    {
        var templates = TemplateFile.Discover(_options.TemplateDirectory);

        var problems = Validate(templates);

        if (problems.Count > 0)
            throw new RenderException(string.Join(Environment.NewLine, problems));

        var writer = new DocumentWriter(_options.OutputDirectory);

        var written = 0;
        var warnings = new List<string>();
        var skips = new List<RecordSkip>();

        foreach (var template in templates)
        {
            var config = template.Parsed.Config;
            var provider = Resolve(config.Source);
            var manifest = ManifestBuilder.FromType(provider.RecordType);

            var (records, templateSkips) = await DrainAsync(provider, manifest, ct)
                .ConfigureAwait(false);

            skips.AddRange(templateSkips);

            DeliveryResult result;

            try
            {
                result = DeliveryPlanner.Plan(
                    config,
                    TemplateLexer.Lex(config.Output),
                    TemplateLexer.Lex(template.Parsed.Body),
                    template.Parsed.PassthroughFrontmatter,
                    records,
                    _options.Culture);
            }
            catch (Exception ex) when (ex is TemplateException or DeliveryException)
            {
                throw new RenderException($"{template.Path}: {ex.Message}", ex);
            }

            warnings.AddRange(result.Warnings);
            written += await writer.WriteAllAsync(result.Documents, ct).ConfigureAwait(false);
        }

        var success = !(_options.Strict && skips.Count > 0);
        return new RenderResult(written, warnings, skips, success);
    }

    public Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var templates = TemplateFile.Discover(_options.TemplateDirectory);

        return Task.FromResult(new CheckResult(templates.Count, Validate(templates)));
    }

    private List<string> Validate(IReadOnlyList<TemplateFile> templates)
    {
        var problems = new List<string>();

        foreach (var template in templates)
        {
            var config = template.Parsed.Config;
            var provider = Resolve(config.Source);
            var manifest = ManifestBuilder.FromType(provider.RecordType);

            // The delivery mode is load-bearing: a collate template validated
            // as fanout reports 'count' and 'records' as unknown fields and
            // then never descends into the loop body, so it fails valid
            // templates and passes broken ones.
            var found = TemplateValidator.Validate(
                TemplateLexer.Lex(template.Parsed.Body), manifest, config.Delivery);

            foreach (var problem in found)
                problems.Add($"{template.Path}: {problem}");
        }

        return problems;
    }

    private IDataProvider Resolve(string source)
    {
        var provider = services.GetKeyedService<IDataProvider>(source);

        if (provider is not null) return provider;

        var available = _registrations.Count == 0
            ? "none are registered"
            : string.Join(", ", _registrations.Select(r => r.Name).Order(StringComparer.Ordinal));

        throw new RenderException(
            $"no source named '{source}' is registered. Available: {available}. "
            + "Register it with .AddSource<YourProvider>(\"" + source + "\").");
    }

    /// <summary>Materializes a provider's records, validating each.</summary>
    /// <remarks>Delivery needs every record before it can name any output file,
    /// because a collision is a property of the whole set. The provider contract
    /// stays asynchronous so it can stream and be cancelled; buffering stops here.</remarks>
    private static async Task<(List<object> Records, List<RecordSkip> Skips)> DrainAsync(
        IDataProvider provider, RecordManifest manifest, CancellationToken ct)
    {
        var records = new List<object>();
        var skips = new List<RecordSkip>();
        var ordinal = 0;

        var enumerator = provider.ReadAsync(ct)
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
                    skips.Add(new RecordSkip(provider.Name, ordinal,
                        $"provider threw: {ex.Message}"));
                    break;
                }

                if (RecordValidator.Validate(record, manifest) is { } reason)
                    skips.Add(new RecordSkip(provider.Name, ordinal, reason));
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
}
```

- [ ] **Step 5: Register the renderer**

In `src/DrMoustache/ServiceCollectionExtensions.cs`, add to `AddDrMoustache` before the `return`:

```csharp
        services.AddSingleton<Rendering.IDocumentRenderer, Rendering.DocumentRenderer>();
```

- [ ] **Step 6: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DocumentRendererTests`
Expected: 11 tests passed.

Run: `dotnet test DrMoustache.slnx`
Expected: all Core tests plus all new tests pass, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache tests/DrMoustache.Tests
git commit -m "$(cat <<'EOF'
Add the document renderer

Validation runs before any provider is read: template faults are static
and affect every record. Each template's own DeliveryMode reaches the
validator, so a collate template is not validated as fanout.

A provider fault ends that source's read but keeps what was already
drained -- an iterator cannot be resumed after it throws.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

**Part 2** — [`2026-08-05-drmoustache-hosting-part2.md`](2026-08-05-drmoustache-hosting-part2.md) — covers:

- **Task 8:** `DrMoustache.Templates` package plus the minimal template
- **Task 9:** The worked-example template
- **Task 10:** Template CI verification — instantiate, build, run, assert output
- **Task 11:** README, `PACKAGE.md`, and package metadata
- **Task 12:** CI workflow, pack verification, and the release checklist
