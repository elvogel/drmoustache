# DrMoustache Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `DrMoustache.Abstractions` (the plugin contract) and `DrMoustache.Core` (a Markdown-aware Mustache rendering engine that performs no I/O).

**Architecture:** A provider declares an attributed POCO; reflection turns that into a `Manifest` of typed fields. Templates are lexed into a node tree, and a separate single-pass scanner labels every character position of the template with its Markdown context (fenced code, table row, line start, inline, …). At render time each interpolated value is escaped according to the context recorded at its tag's position. The engine never touches the filesystem — it returns `RenderedDocument` values in memory.

**Tech Stack:** .NET 10, C# `preview`, YamlDotNet (frontmatter), xunit.v3, FluentAssertions 6.12.2, Markdig (test-only, for the round-trip property test).

## Global Constraints

Copied verbatim from the spec — every task's requirements implicitly include these.

- `TargetFramework`: `net10.0`
- `LangVersion`: `preview`
- `Nullable`: `enable`, `ImplicitUsings`: `enable`
- `TreatWarningsAsErrors`: `true`, `EnforceCodeStyleInBuild`: `true`, `AnalysisLevel`: `latest-all`
- Central package management (`Directory.Packages.props`, `ManagePackageVersionsCentrally`)
- FluentAssertions pinned at **6.12.2** — the last MIT release. Do not bump.
- License: Apache-2.0. Authors/Company: Endpoint Systems.
- `DrMoustache.Core` must reference **no I/O or transport assembly**. No `System.Net.*`, no database or messaging clients, no `File`/`Directory` usage.
- `DrMoustache.Core` sources must contain none of the transport nouns `message`, `queue`, `row`, `endpoint` (enforced by an architecture test in Task 12).
- HTML entities (`&`, `<`, `>`) are **never** escaped. Output is Markdown, not HTML.
- Namespace root is `DrMoustache`. File-scoped namespaces, `using` directives inside the namespace (matching Crucible's style).

---

### Task 1: Solution scaffolding and build conventions

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `DrMoustache.slnx`
- Create: `LICENSE`
- Create: `src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj`
- Create: `src/DrMoustache.Core/DrMoustache.Core.csproj`
- Create: `tests/DrMoustache.Core.Tests/DrMoustache.Core.Tests.csproj`
- Test: `tests/DrMoustache.Core.Tests/ScaffoldingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable solution. Later tasks add files to `src/DrMoustache.Abstractions`, `src/DrMoustache.Core`, and `tests/DrMoustache.Core.Tests`.

- [ ] **Step 1: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-all</AnalysisLevel>
  </PropertyGroup>
  <PropertyGroup>
    <Authors>Endpoint Systems</Authors>
    <Company>Endpoint Systems</Company>
    <Product>DrMoustache</Product>
    <Copyright>Copyright © Endpoint Systems 2026. All rights reserved.</Copyright>
    <RepositoryType>git</RepositoryType>
    <RepositoryUrl>https://github.com/elvogel/drmoustache</RepositoryUrl>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <DebugType>embedded</DebugType>
    <DebugSymbols>true</DebugSymbols>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageVersion Include="YamlDotNet" Version="18.1.0" />
  </ItemGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="coverlet.collector" Version="10.0.1" />
    <!-- Intentionally pinned. 7.x/8.x moved to the Xceed license (paid for
         commercial use); 6.12.2 is the last MIT release. Do not bump. -->
    <PackageVersion Include="FluentAssertions" Version="6.12.2" />
    <!-- Test-only: used by the round-trip property test in Task 6. -->
    <PackageVersion Include="Markdig" Version="1.3.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the three project files**

`src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>Plugin contract for DrMoustache data providers.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

`src/DrMoustache.Core/DrMoustache.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>Markdown-aware Mustache rendering engine.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" />
    <ProjectReference Include="../DrMoustache.Abstractions/DrMoustache.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

`tests/DrMoustache.Core.Tests/DrMoustache.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Markdig" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/DrMoustache.Core/DrMoustache.Core.csproj" />
  </ItemGroup>
</Project>
```

Note: `xunit.v3` test projects are executables (`OutputType`/`Exe`), unlike xunit v2.

- [ ] **Step 4: Create `DrMoustache.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj" />
    <Project Path="src/DrMoustache.Core/DrMoustache.Core.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/DrMoustache.Core.Tests/DrMoustache.Core.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 5: Add the Apache-2.0 `LICENSE`**

Run: `curl -sL https://www.apache.org/licenses/LICENSE-2.0.txt -o LICENSE`

Then append the copyright line required by the Apache appendix — replace the bracketed placeholders in the boilerplate at the end of the file with `2026` and `Endpoint Systems`.

- [ ] **Step 6: Write a scaffolding test**

`tests/DrMoustache.Core.Tests/ScaffoldingTests.cs`:

```csharp
namespace DrMoustache.Core.Tests;

using FluentAssertions;
using Xunit;

public class ScaffoldingTests
{
    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        var asm = typeof(DrMoustache.Abstractions.IDataProvider).Assembly;
        asm.GetName().Name.Should().Be("DrMoustache.Abstractions");
    }
}
```

This will not compile until Task 2 defines `IDataProvider`. That is intentional — it is the failing test for Task 2. For this task, temporarily assert on a type that exists:

```csharp
    [Fact]
    public void SolutionBuilds() => true.Should().BeTrue();
```

Write `SolutionBuilds` now; Task 2 replaces it with `CoreAssemblyIsReferenceable`.

- [ ] **Step 7: Verify the build and test run**

Run: `dotnet build DrMoustache.slnx`
Expected: build succeeds, zero warnings (warnings are errors).

Run: `dotnet test DrMoustache.slnx`
Expected: 1 test passed.

- [ ] **Step 8: Commit**

```bash
git add Directory.Build.props Directory.Packages.props DrMoustache.slnx LICENSE src tests
git commit -m "$(cat <<'EOF'
Add solution scaffolding and build conventions

net10.0, nullable, warnings-as-errors, central package management.
FluentAssertions pinned at 6.12.2 (last MIT release).

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 2: The plugin contract

**Files:**
- Create: `src/DrMoustache.Abstractions/DrMoustacheRecordAttribute.cs`
- Create: `src/DrMoustache.Abstractions/FieldAttribute.cs`
- Create: `src/DrMoustache.Abstractions/ProviderContext.cs`
- Create: `src/DrMoustache.Abstractions/IDataProvider.cs`
- Modify: `tests/DrMoustache.Core.Tests/ScaffoldingTests.cs`
- Test: `tests/DrMoustache.Core.Tests/ContractTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `[DrMoustacheRecord(string name)]` — class/record attribute, `Name` property.
  - `[Field]` / `[Field(string description)]` — property attribute, `Description` property (nullable).
  - `ProviderContext` with `Configuration`, `Source`, `Logger` (all `required`).
  - `IDataProvider` with `string Name`, `Type RecordType`, `IAsyncEnumerable<object> ReadAsync(ProviderContext, CancellationToken)`.
  - `DataProvider<TRecord>` abstract base with `abstract string Name`, `abstract IAsyncEnumerable<TRecord> ReadAsync(ProviderContext, CancellationToken)`.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/ContractTests.cs`:

```csharp
namespace DrMoustache.Core.Tests;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using FluentAssertions;
using Xunit;

public class ContractTests
{
    [DrMoustacheRecord("sample")]
    private sealed record Sample
    {
        [Field] public required string Id { get; init; }
        [Field("a human note")] public string? Note { get; init; }
    }

    private sealed class SampleProvider : DataProvider<Sample>
    {
        public override string Name => "sample-provider";

        public override async IAsyncEnumerable<Sample> ReadAsync(
            ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Sample { Id = "a" };
        }
    }

    [Fact]
    public void RecordAttributeCarriesName()
    {
        var attr = typeof(Sample).GetCustomAttributes(typeof(DrMoustacheRecordAttribute), false)
            .Cast<DrMoustacheRecordAttribute>().Single();
        attr.Name.Should().Be("sample");
    }

    [Fact]
    public void FieldAttributeCarriesOptionalDescription()
    {
        var note = typeof(Sample).GetProperty(nameof(Sample.Note))!;
        var attr = note.GetCustomAttributes(typeof(FieldAttribute), false)
            .Cast<FieldAttribute>().Single();
        attr.Description.Should().Be("a human note");

        var id = typeof(Sample).GetProperty(nameof(Sample.Id))!;
        var idAttr = id.GetCustomAttributes(typeof(FieldAttribute), false)
            .Cast<FieldAttribute>().Single();
        idAttr.Description.Should().BeNull();
    }

    [Fact]
    public void TypedBaseExposesRecordTypeThroughInterface()
    {
        IDataProvider provider = new SampleProvider();
        provider.Name.Should().Be("sample-provider");
        provider.RecordType.Should().Be<Sample>();
    }

    [Fact]
    public async Task TypedBaseBridgesToObjectSequence()
    {
        IDataProvider provider = new SampleProvider();
        var ctx = new ProviderContext
        {
            Configuration = new Microsoft.Extensions.Configuration.ConfigurationManager(),
            Source = new Microsoft.Extensions.Configuration.ConfigurationManager()
                .GetSection("sample"),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
        };

        var items = new List<object>();
        await foreach (var item in provider.ReadAsync(ctx, TestContext.Current.CancellationToken))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().BeOfType<Sample>();
    }
}
```

The test project needs `Microsoft.Extensions.Configuration` (not just the abstractions) for `ConfigurationManager`. Add `<PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.0" />` to `Directory.Packages.props` and a matching `<PackageReference>` to the test project.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DrMoustache.slnx`
Expected: compilation failure — `DrMoustacheRecordAttribute`, `FieldAttribute`, `ProviderContext`, `IDataProvider`, `DataProvider<>` do not exist.

- [ ] **Step 3: Write the attributes**

`src/DrMoustache.Abstractions/DrMoustacheRecordAttribute.cs`:

```csharp
namespace DrMoustache.Abstractions;

/// <summary>Marks a type as a record shape supplied to the rendering engine.</summary>
/// <param name="name">Business name for the shape, e.g. <c>failed-order</c>.
/// Reported in validation errors.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DrMoustacheRecordAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
```

`src/DrMoustache.Abstractions/FieldAttribute.cs`:

```csharp
namespace DrMoustache.Abstractions;

/// <summary>Exposes a property to templates.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    public FieldAttribute() { }

    /// <param name="description">Shown in validation errors when a template
    /// references a near-miss field name.</param>
    public FieldAttribute(string description) => Description = description;

    public string? Description { get; }
}
```

- [ ] **Step 4: Write `ProviderContext`**

`src/DrMoustache.Abstractions/ProviderContext.cs`:

```csharp
namespace DrMoustache.Abstractions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>Everything a provider is given when asked to read.</summary>
public sealed class ProviderContext
{
    /// <summary>The full configuration chain.</summary>
    /// <remarks>A provider can read every setting the host can, including
    /// secrets belonging to unrelated sources. Narrow it per source with
    /// <c>scopeConfig: true</c>.</remarks>
    public required IConfiguration Configuration { get; init; }

    /// <summary>This source's own configuration section, always scoped
    /// regardless of the <c>scopeConfig</c> setting.</summary>
    public required IConfigurationSection Source { get; init; }

    public required ILogger Logger { get; init; }
}
```

- [ ] **Step 5: Write the provider contract**

`src/DrMoustache.Abstractions/IDataProvider.cs`:

```csharp
namespace DrMoustache.Abstractions;

using System.Runtime.CompilerServices;

/// <summary>Supplies records to the rendering engine.</summary>
/// <remarks>Read-only: implementations must not mutate their underlying
/// source. There is deliberately no write, delete, or acknowledge surface.</remarks>
public interface IDataProvider
{
    /// <summary>Matched against a template's <c>source:</c> setting.</summary>
    string Name { get; }

    /// <summary>The <see cref="DrMoustacheRecordAttribute"/>-annotated type
    /// this provider yields.</summary>
    Type RecordType { get; }

    IAsyncEnumerable<object> ReadAsync(ProviderContext ctx, CancellationToken ct);
}

/// <summary>Base class for providers. Derive from this, not from
/// <see cref="IDataProvider"/> directly.</summary>
public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    public abstract string Name { get; }

    public Type RecordType => typeof(TRecord);

    public abstract IAsyncEnumerable<TRecord> ReadAsync(
        ProviderContext ctx, CancellationToken ct);

    async IAsyncEnumerable<object> IDataProvider.ReadAsync(
        ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in ReadAsync(ctx, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return record;
    }
}
```

- [ ] **Step 6: Replace the placeholder scaffolding test**

In `tests/DrMoustache.Core.Tests/ScaffoldingTests.cs`, replace `SolutionBuilds` with:

```csharp
    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        var asm = typeof(DrMoustache.Abstractions.IDataProvider).Assembly;
        asm.GetName().Name.Should().Be("DrMoustache.Abstractions");
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test DrMoustache.slnx`
Expected: 5 tests passed.

- [ ] **Step 8: Commit**

```bash
git add src/DrMoustache.Abstractions tests Directory.Packages.props
git commit -m "$(cat <<'EOF'
Add plugin contract

Read-only by construction: IDataProvider exposes no write, delete, or
acknowledge surface. Authors derive from DataProvider<TRecord>; the
non-generic interface exists so the loader has something to activate.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 3: Manifest model

**Files:**
- Create: `src/DrMoustache.Core/Manifest/FieldType.cs`
- Create: `src/DrMoustache.Core/Manifest/ManifestField.cs`
- Create: `src/DrMoustache.Core/Manifest/RecordManifest.cs`
- Test: `tests/DrMoustache.Core.Tests/Manifest/RecordManifestTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `enum FieldType { String, Integer, Decimal, Boolean, Temporal, Guid, Object, Collection }`
  - `sealed record ManifestField(string Path, FieldType Type, bool IsNullable, string? Description, IReadOnlyList<ManifestField> Children)`
  - `sealed class RecordManifest` with `string Name`, `IReadOnlyList<ManifestField> Fields`, `bool TryResolve(string path, out ManifestField field)`, `IEnumerable<string> AllPaths()`.

`RecordManifest` holds no `PropertyInfo` — it is a plain data structure so a source generator can produce it at compile time later (spec: *Deferred*).

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Manifest/RecordManifestTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Manifest;

using DrMoustache.Core.Manifest;
using FluentAssertions;
using Xunit;

public class RecordManifestTests
{
    private static RecordManifest Build() => new("failed-order",
    [
        new ManifestField("orderId", FieldType.String, false, "the order number", []),
        new ManifestField("total", FieldType.Decimal, false, null, []),
        new ManifestField("promoCode", FieldType.String, true, null, []),
        new ManifestField("order", FieldType.Object, false, null,
        [
            new ManifestField("id", FieldType.String, false, null, []),
            new ManifestField("total", FieldType.Decimal, false, null, []),
        ]),
        new ManifestField("items", FieldType.Collection, false, null,
        [
            new ManifestField("name", FieldType.String, false, null, []),
        ]),
    ]);

    [Fact]
    public void ResolvesTopLevelField()
    {
        Build().TryResolve("orderId", out var f).Should().BeTrue();
        f.Type.Should().Be(FieldType.String);
        f.Description.Should().Be("the order number");
    }

    [Fact]
    public void ResolvesNestedFieldByDottedPath()
    {
        Build().TryResolve("order.total", out var f).Should().BeTrue();
        f.Type.Should().Be(FieldType.Decimal);
    }

    [Fact]
    public void FailsOnUnknownPath()
    {
        Build().TryResolve("nope", out _).Should().BeFalse();
        Build().TryResolve("order.nope", out _).Should().BeFalse();
    }

    [Fact]
    public void AllPathsIncludesNestedAndCollectionMembers()
    {
        Build().AllPaths().Should().BeEquivalentTo(
        [
            "orderId", "total", "promoCode",
            "order", "order.id", "order.total",
            "items", "items.name",
        ]);
    }

    [Fact]
    public void NullabilityIsRecorded()
    {
        Build().TryResolve("promoCode", out var f).Should().BeTrue();
        f.IsNullable.Should().BeTrue();
        Build().TryResolve("orderId", out var g).Should().BeTrue();
        g.IsNullable.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RecordManifestTests`
Expected: compilation failure — `FieldType`, `ManifestField`, `RecordManifest` do not exist.

- [ ] **Step 3: Write `FieldType`**

`src/DrMoustache.Core/Manifest/FieldType.cs`:

```csharp
namespace DrMoustache.Core.Manifest;

/// <summary>Escaping, validation, and helper arity all key off this.</summary>
public enum FieldType
{
    /// <summary>The only type that can produce Markdown-significant output.</summary>
    String,
    Integer,
    Decimal,
    Boolean,
    Temporal,
    Guid,
    /// <summary>A nested shape. Cannot be interpolated directly.</summary>
    Object,
    /// <summary>A sequence. Usable only as a section.</summary>
    Collection,
}
```

- [ ] **Step 4: Write `ManifestField`**

`src/DrMoustache.Core/Manifest/ManifestField.cs`:

```csharp
namespace DrMoustache.Core.Manifest;

/// <param name="Path">The camelCase segment name, not the full dotted path.</param>
/// <param name="Children">Members of an <see cref="FieldType.Object"/> or
/// <see cref="FieldType.Collection"/>; empty otherwise.</param>
public sealed record ManifestField(
    string Path,
    FieldType Type,
    bool IsNullable,
    string? Description,
    IReadOnlyList<ManifestField> Children)
{
    /// <summary>True when a value of this type can never contain
    /// Markdown-significant characters, so escaping can be skipped.</summary>
    public bool IsEscapeExempt => Type is not (FieldType.String or FieldType.Object);
}
```

- [ ] **Step 5: Write `RecordManifest`**

`src/DrMoustache.Core/Manifest/RecordManifest.cs`:

```csharp
namespace DrMoustache.Core.Manifest;

using System.Diagnostics.CodeAnalysis;

/// <summary>The declared shape of a provider's records. Plain data — holds no
/// reflection handles, so a source generator can emit it later.</summary>
public sealed class RecordManifest(string name, IReadOnlyList<ManifestField> fields)
{
    public string Name { get; } = name;

    public IReadOnlyList<ManifestField> Fields { get; } = fields;

    public bool TryResolve(string path, [NotNullWhen(true)] out ManifestField? field)
    {
        ArgumentNullException.ThrowIfNull(path);

        var current = Fields;
        field = null;

        foreach (var segment in path.Split('.'))
        {
            var match = current.FirstOrDefault(f =>
                string.Equals(f.Path, segment, StringComparison.Ordinal));

            if (match is null)
            {
                field = null;
                return false;
            }

            field = match;
            current = match.Children;
        }

        return field is not null;
    }

    public IEnumerable<string> AllPaths() => Walk(Fields, prefix: null);

    private static IEnumerable<string> Walk(
        IReadOnlyList<ManifestField> fields, string? prefix)
    {
        foreach (var f in fields)
        {
            var path = prefix is null ? f.Path : $"{prefix}.{f.Path}";
            yield return path;

            foreach (var child in Walk(f.Children, path))
                yield return child;
        }
    }
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RecordManifestTests`
Expected: 5 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache.Core/Manifest tests/DrMoustache.Core.Tests/Manifest
git commit -m "$(cat <<'EOF'
Add manifest model

Plain data structure holding no reflection handles, so a source generator
can emit it at compile time later for Native AOT.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 4: Manifest builder (reflection over the attributed POCO)

**Files:**
- Create: `src/DrMoustache.Core/Manifest/ManifestBuilder.cs`
- Test: `tests/DrMoustache.Core.Tests/Manifest/ManifestBuilderTests.cs`

**Interfaces:**
- Consumes: `RecordManifest`, `ManifestField`, `FieldType` (Task 3); `DrMoustacheRecordAttribute`, `FieldAttribute` (Task 2).
- Produces: `static RecordManifest ManifestBuilder.FromType(Type recordType)` and `static RecordManifest ManifestBuilder.FromType<T>()`. Throws `ManifestException` (also created here) when the type lacks `[DrMoustacheRecord]`.

Mapping rules: property names become camelCase paths; `[Field]` is required for a property to be exposed; nullability comes from `NullabilityInfoContext`; `IEnumerable<T>` (excluding `string`) becomes `Collection` with `T`'s members as children; any other non-scalar class or struct becomes `Object` with its `[Field]` members as children.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Manifest/ManifestBuilderTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Manifest;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using FluentAssertions;
using Xunit;

public class ManifestBuilderTests
{
    private sealed record LineItem
    {
        [Field] public required string Name { get; init; }
        [Field] public int Qty { get; init; }
    }

    private sealed record OrderInfo
    {
        [Field] public required string Id { get; init; }
        [Field] public decimal Total { get; init; }
    }

    [DrMoustacheRecord("failed-order")]
    private sealed record FailedOrder
    {
        [Field] public required long SequenceNumber { get; init; }
        [Field("Why it dead-lettered")] public required string Reason { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public DateTimeOffset PlacedAt { get; init; }
        [Field] public bool IsRetryable { get; init; }
        [Field] public Guid TraceId { get; init; }
        [Field] public string? PromoCode { get; init; }
        [Field] public required OrderInfo Order { get; init; }
        [Field] public IReadOnlyList<LineItem> Items { get; init; } = [];
        public string NotExposed { get; init; } = "";
    }

    private sealed record Unannotated;

    [Fact]
    public void UsesTheDeclaredRecordName()
        => ManifestBuilder.FromType<FailedOrder>().Name.Should().Be("failed-order");

    [Theory]
    [InlineData("sequenceNumber", FieldType.Integer)]
    [InlineData("reason", FieldType.String)]
    [InlineData("total", FieldType.Decimal)]
    [InlineData("placedAt", FieldType.Temporal)]
    [InlineData("isRetryable", FieldType.Boolean)]
    [InlineData("traceId", FieldType.Guid)]
    [InlineData("order", FieldType.Object)]
    [InlineData("items", FieldType.Collection)]
    public void MapsClrTypesToFieldTypes(string path, FieldType expected)
    {
        ManifestBuilder.FromType<FailedOrder>().TryResolve(path, out var f).Should().BeTrue();
        f!.Type.Should().Be(expected);
    }

    [Fact]
    public void CamelCasesPropertyNames()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().Contain("sequenceNumber").And.NotContain("SequenceNumber");

    [Fact]
    public void ExposesOnlyAnnotatedProperties()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().NotContain("notExposed");

    [Fact]
    public void RecordsNullability()
    {
        var m = ManifestBuilder.FromType<FailedOrder>();
        m.TryResolve("promoCode", out var nullable).Should().BeTrue();
        nullable!.IsNullable.Should().BeTrue();
        m.TryResolve("reason", out var required).Should().BeTrue();
        required!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void CarriesDescriptions()
    {
        ManifestBuilder.FromType<FailedOrder>().TryResolve("reason", out var f).Should().BeTrue();
        f!.Description.Should().Be("Why it dead-lettered");
    }

    [Fact]
    public void DescendsIntoNestedObjectsAndCollections()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().Contain(["order.id", "order.total", "items.name", "items.qty"]);

    [Fact]
    public void RejectsTypesWithoutTheRecordAttribute()
    {
        var act = () => ManifestBuilder.FromType<Unannotated>();
        act.Should().Throw<ManifestException>()
            .WithMessage("*[DrMoustacheRecord]*");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ManifestBuilderTests`
Expected: compilation failure — `ManifestBuilder` and `ManifestException` do not exist.

- [ ] **Step 3: Write `ManifestException`**

`src/DrMoustache.Core/Manifest/ManifestException.cs`:

```csharp
namespace DrMoustache.Core.Manifest;

/// <summary>Raised when a record type cannot be turned into a manifest.</summary>
public sealed class ManifestException : Exception
{
    public ManifestException() { }

    public ManifestException(string message) : base(message) { }

    public ManifestException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 4: Write `ManifestBuilder`**

`src/DrMoustache.Core/Manifest/ManifestBuilder.cs`:

```csharp
namespace DrMoustache.Core.Manifest;

using System.Collections;
using System.Reflection;
using DrMoustache.Abstractions;

/// <summary>Reflects an attributed record type into a <see cref="RecordManifest"/>.
/// Runs once per provider load, not per record.</summary>
public static class ManifestBuilder
{
    private const int MaxDepth = 16;

    public static RecordManifest FromType<T>() => FromType(typeof(T));

    public static RecordManifest FromType(Type recordType)
    {
        ArgumentNullException.ThrowIfNull(recordType);

        var attr = recordType.GetCustomAttribute<DrMoustacheRecordAttribute>()
            ?? throw new ManifestException(
                $"'{recordType.Name}' is not annotated with [DrMoustacheRecord]. "
                + "Add [DrMoustacheRecord(\"some-name\")] to the type.");

        return new RecordManifest(attr.Name, BuildFields(recordType, depth: 0));
    }

    private static IReadOnlyList<ManifestField> BuildFields(Type type, int depth)
    {
        if (depth >= MaxDepth)
            throw new ManifestException(
                $"Record shape nests deeper than {MaxDepth} levels at '{type.Name}'. "
                + "This usually means a cycle.");

        var nullability = new NullabilityInfoContext();
        var fields = new List<ManifestField>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<FieldAttribute>() is not { } fieldAttr)
                continue;

            var (fieldType, elementType) = Classify(prop.PropertyType);

            var isNullable = nullability.Create(prop).ReadState == NullabilityState.Nullable;

            var children = fieldType switch
            {
                FieldType.Collection => BuildFields(elementType!, depth + 1),
                FieldType.Object => BuildFields(prop.PropertyType, depth + 1),
                _ => [],
            };

            fields.Add(new ManifestField(
                ToCamelCase(prop.Name), fieldType, isNullable, fieldAttr.Description, children));
        }

        return fields;
    }

    private static (FieldType Type, Type? ElementType) Classify(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return (FieldType.String, null);
        if (underlying == typeof(bool)) return (FieldType.Boolean, null);
        if (underlying == typeof(Guid)) return (FieldType.Guid, null);

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly) || underlying == typeof(TimeOnly))
            return (FieldType.Temporal, null);

        if (underlying == typeof(decimal) || underlying == typeof(double)
            || underlying == typeof(float))
            return (FieldType.Decimal, null);

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong))
            return (FieldType.Integer, null);

        if (GetElementType(underlying) is { } element)
            return (FieldType.Collection, element);

        return (FieldType.Object, null);
    }

    private static Type? GetElementType(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return null;

        if (type.IsArray) return type.GetElementType();

        var enumerable = type.GetInterfaces()
            .Concat(type.IsInterface ? [type] : [])
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable?.GetGenericArguments()[0];
    }

    private static string ToCamelCase(string name) =>
        name.Length > 0 && char.IsUpper(name[0])
            ? string.Create(name.Length, name, static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                span[0] = char.ToLowerInvariant(span[0]);
            })
            : name;
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ManifestBuilderTests`
Expected: 15 tests passed (the `[Theory]` contributes 8).

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Core/Manifest tests/DrMoustache.Core.Tests/Manifest
git commit -m "$(cat <<'EOF'
Add manifest builder

Reflects an attributed record type once per provider load. Nullability
comes from NullabilityInfoContext so `required` and `?` both inform
validation.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 5: Markdown context scanner

This is the heart of the engine. It answers: *at character index `i` of the template, what Markdown construct are we inside?*

**Files:**
- Create: `src/DrMoustache.Core/Escaping/MarkdownContext.cs`
- Create: `src/DrMoustache.Core/Escaping/ContextScanner.cs`
- Test: `tests/DrMoustache.Core.Tests/Escaping/ContextScannerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `enum MarkdownContext { Inline, LineStart, TableRow, CodeSpan, FencedCode, LinkTarget }`
  - `static MarkdownContext[] ContextScanner.Scan(string template)` — one entry per character of `template`.

**Critical detail — section tags are transparent at line start.** A fan-out table row looks like:

```
{{#items}}| {{name}} | {{qty}} |
```

The line's first non-whitespace character is `{`, not `|`. The scanner must skip over any leading run of section-open/close tags (`{{#…}}`, `{{^…}}`, `{{/…}}`) before deciding whether the line is a table row. Without this, table fan-out — the primary use case — gets `Inline` escaping and pipes are not escaped.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Core.Tests/Escaping/ContextScannerTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Xunit;

public class ContextScannerTests
{
    /// <summary>Context at the position of the marker character '@',
    /// which is stripped before scanning.</summary>
    private static MarkdownContext At(string templateWithMarker)
    {
        var index = templateWithMarker.IndexOf('@', StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, "the test must place a '@' marker");
        var template = templateWithMarker.Remove(index, 1);
        return ContextScanner.Scan(template)[index];
    }

    [Fact]
    public void PlainProseIsInline()
        => At("some text @here").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void StartOfLineIsLineStart()
        => At("first line\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void FirstCharacterOfDocumentIsLineStart()
        => At("@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void LineStartExtendsToColumnThree()
        => At("line\n   @value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void ColumnFourIsNoLongerLineStart()
        => At("line\n    @value").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void InsideFencedCodeIsFencedCode()
        => At("```\n@value\n```").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void AfterAClosedFenceIsNotFencedCode()
        => At("```\ncode\n```\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void TildeFencesAreRecognised()
        => At("~~~\n@value\n~~~").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void ShorterFenceDoesNotCloseALongerOne()
        => At("````\n```\n@value\n````").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void InsideCodeSpanIsCodeSpan()
        => At("text `@value` more").Should().Be(MarkdownContext.CodeSpan);

    [Fact]
    public void AfterCodeSpanIsInline()
        => At("text `code` @value").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void TableRowIsDetected()
        => At("| a | @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void SectionTagAtLineStartIsTransparentForTableDetection()
        => At("{{#items}}| a | @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void ClosingSectionTagIsAlsoTransparent()
        => At("{{/prev}}{{#items}}| @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void InvertedSectionTagIsAlsoTransparent()
        => At("{{^empty}}| @value |").Should().Be(MarkdownContext.TableRow);

    [Fact]
    public void PipeInsideFencedCodeIsNotATableRow()
        => At("```\n| a | @value |\n```").Should().Be(MarkdownContext.FencedCode);

    [Fact]
    public void LinkTargetIsDetected()
        => At("see [the docs](@value) here").Should().Be(MarkdownContext.LinkTarget);

    [Fact]
    public void LinkTextIsNotLinkTarget()
        => At("see [@value](http://x) here").Should().Be(MarkdownContext.Inline);

    [Fact]
    public void CrlfLineEndingsAreHandled()
        => At("first\r\n@value").Should().Be(MarkdownContext.LineStart);

    [Fact]
    public void ScanReturnsOneEntryPerCharacter()
        => ContextScanner.Scan("abc").Should().HaveCount(3);

    [Fact]
    public void EmptyTemplateReturnsEmptyArray()
        => ContextScanner.Scan("").Should().BeEmpty();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ContextScannerTests`
Expected: compilation failure — `MarkdownContext` and `ContextScanner` do not exist.

- [ ] **Step 3: Write `MarkdownContext`**

`src/DrMoustache.Core/Escaping/MarkdownContext.cs`:

```csharp
namespace DrMoustache.Core.Escaping;

/// <summary>Where in a Markdown document a template tag sits. Determines
/// which characters an interpolated value must escape.</summary>
public enum MarkdownContext
{
    /// <summary>Mid-line prose. Emphasis and link syntax are live.</summary>
    Inline,

    /// <summary>Columns 0-3 of a line. Block constructs are live here, so a
    /// value beginning '#' would become a heading.</summary>
    LineStart,

    /// <summary>Inside a table row. Pipes and newlines break the row.</summary>
    TableRow,

    /// <summary>Inside a backtick span. Content is literal.</summary>
    CodeSpan,

    /// <summary>Inside a fenced block. Content is literal.</summary>
    FencedCode,

    /// <summary>Inside the target portion of a link. URL rules apply.</summary>
    LinkTarget,
}
```

- [ ] **Step 4: Write `ContextScanner`**

`src/DrMoustache.Core/Escaping/ContextScanner.cs`:

```csharp
namespace DrMoustache.Core.Escaping;

/// <summary>Labels every character position of a template with the Markdown
/// construct containing it.</summary>
/// <remarks>A template is not valid Markdown until expanded — <c>{{#items}}|
/// {{name}} |</c> is a paragraph to any parser and only becomes a table row
/// after the section expands. So this is a lexical state machine, not a
/// Markdown parse.</remarks>
public static class ContextScanner
{
    public static MarkdownContext[] Scan(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var result = new MarkdownContext[template.Length];
        if (template.Length == 0) return result;

        var i = 0;
        var fenceChar = '\0';
        var fenceLength = 0;

        while (i < template.Length)
        {
            var lineEnd = FindLineEnd(template, i);
            var line = template.AsSpan(i, lineEnd - i);

            if (fenceLength > 0)
            {
                if (IsClosingFence(line, fenceChar, fenceLength))
                {
                    Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                    fenceChar = '\0';
                    fenceLength = 0;
                }
                else
                {
                    Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                }
            }
            else if (TryReadFenceOpen(line, out var openChar, out var openLength))
            {
                Fill(result, i, lineEnd, MarkdownContext.FencedCode);
                fenceChar = openChar;
                fenceLength = openLength;
            }
            else
            {
                ScanTextLine(template, i, lineEnd, result);
            }

            i = SkipLineBreak(template, lineEnd);
        }

        return result;
    }

    private static void ScanTextLine(
        string template, int start, int end, MarkdownContext[] result)
    {
        var isTableRow = LooksLikeTableRow(template, start, end);
        var baseContext = isTableRow ? MarkdownContext.TableRow : MarkdownContext.Inline;

        var i = start;
        var codeSpanTicks = 0;
        var inLinkTarget = false;

        while (i < end)
        {
            var c = template[i];

            if (codeSpanTicks > 0)
            {
                if (c == '`')
                {
                    var run = CountRun(template, i, '`', end);
                    if (run == codeSpanTicks)
                    {
                        Fill(result, i, i + run, MarkdownContext.CodeSpan);
                        codeSpanTicks = 0;
                        i += run;
                        continue;
                    }
                }

                result[i] = MarkdownContext.CodeSpan;
                i++;
                continue;
            }

            if (c == '`')
            {
                var run = CountRun(template, i, '`', end);
                Fill(result, i, i + run, MarkdownContext.CodeSpan);
                codeSpanTicks = run;
                i += run;
                continue;
            }

            if (inLinkTarget)
            {
                result[i] = MarkdownContext.LinkTarget;
                if (c == ')') inLinkTarget = false;
                i++;
                continue;
            }

            if (c == '(' && i > start && template[i - 1] == ']')
            {
                inLinkTarget = true;
                result[i] = MarkdownContext.LinkTarget;
                i++;
                continue;
            }

            result[i] = !isTableRow && i - start < 4
                ? MarkdownContext.LineStart
                : baseContext;

            i++;
        }
    }

    /// <summary>A line is a table row when its first meaningful character is a
    /// pipe. Leading section tags are transparent, so
    /// <c>{{#items}}| a | b |</c> counts.</summary>
    private static bool LooksLikeTableRow(string template, int start, int end)
    {
        var i = start;

        while (i < end)
        {
            if (char.IsWhiteSpace(template[i])) { i++; continue; }

            if (TryMeasureSectionTag(template, i, end, out var length))
            {
                i += length;
                continue;
            }

            return template[i] == '|';
        }

        return false;
    }

    /// <summary>Measures a section-open, inverted-section, or section-close tag
    /// at <paramref name="index"/>. Interpolation tags do not count — only
    /// block tags are transparent for table detection.</summary>
    private static bool TryMeasureSectionTag(
        string template, int index, int end, out int length)
    {
        length = 0;

        if (index + 3 >= end) return false;
        if (template[index] != '{' || template[index + 1] != '{') return false;
        if (template[index + 2] is not ('#' or '^' or '/')) return false;

        var close = template.IndexOf("}}", index + 3, StringComparison.Ordinal);
        if (close < 0 || close + 2 > end) return false;

        length = close + 2 - index;
        return true;
    }

    private static bool TryReadFenceOpen(
        ReadOnlySpan<char> line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;

        var i = 0;
        while (i < line.Length && line[i] == ' ' && i < 4) i++;
        if (i >= line.Length) return false;

        var c = line[i];
        if (c is not ('`' or '~')) return false;

        var run = 0;
        while (i + run < line.Length && line[i + run] == c) run++;
        if (run < 3) return false;

        fenceChar = c;
        fenceLength = run;
        return true;
    }

    private static bool IsClosingFence(
        ReadOnlySpan<char> line, char fenceChar, int openLength)
    {
        var i = 0;
        while (i < line.Length && line[i] == ' ' && i < 4) i++;

        var run = 0;
        while (i + run < line.Length && line[i + run] == fenceChar) run++;
        if (run < openLength) return false;

        for (var j = i + run; j < line.Length; j++)
            if (!char.IsWhiteSpace(line[j])) return false;

        return true;
    }

    private static int CountRun(string s, int index, char c, int end)
    {
        var run = 0;
        while (index + run < end && s[index + run] == c) run++;
        return run;
    }

    private static int FindLineEnd(string s, int from)
    {
        var i = s.IndexOf('\n', from);
        if (i < 0) return s.Length;
        return i > from && s[i - 1] == '\r' ? i - 1 : i;
    }

    private static int SkipLineBreak(string s, int lineEnd)
    {
        var i = lineEnd;
        if (i < s.Length && s[i] == '\r') i++;
        if (i < s.Length && s[i] == '\n') i++;
        return i == lineEnd ? s.Length : i;
    }

    private static void Fill(
        MarkdownContext[] result, int start, int end, MarkdownContext context)
    {
        for (var i = start; i < end && i < result.Length; i++)
            result[i] = context;
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ContextScannerTests`
Expected: 21 tests passed.

If `SectionTagAtLineStartIsTransparentForTableDetection` fails, the bug is in `LooksLikeTableRow` / `TryMeasureSectionTag` — that pair is the whole reason table fan-out works.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Core/Escaping tests/DrMoustache.Core.Tests/Escaping
git commit -m "$(cat <<'EOF'
Add Markdown context scanner

Single-pass lexical state machine labelling every character position with
its Markdown construct. Not a Markdown parse: a template is not valid
Markdown until expanded.

Leading section tags are transparent for table detection, so
`{{#items}}| {{name}} |` is recognised as a table row. Without that,
table fan-out silently loses pipe escaping.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 6: The escaper and its round-trip property test

**Files:**
- Create: `src/DrMoustache.Core/Escaping/MarkdownEscaper.cs`
- Test: `tests/DrMoustache.Core.Tests/Escaping/MarkdownEscaperTests.cs`
- Test: `tests/DrMoustache.Core.Tests/Escaping/RoundTripTests.cs`

**Interfaces:**
- Consumes: `MarkdownContext` (Task 5).
- Produces: `static string MarkdownEscaper.Escape(string value, MarkdownContext context)` and `static string MarkdownEscaper.FenceFor(string value)` (returns the backtick run needed to wrap a value containing backticks).

The rules table from the spec, verbatim:

| Context | Escaped | Left alone |
|---|---|---|
| `FencedCode` | nothing | everything |
| `CodeSpan` | nothing; widen the fence | everything |
| `TableRow` | pipe → backslash-escaped pipe; newline → `<br>` | `#`, `*`, `_` |
| `LineStart` | leading `#` `>` `-` `*` `+`, `1.`, four-space indent | after column 0 |
| `LinkTarget` | percent-encode space, `(`, `)` | — |
| `Inline` | `*` `_` `[` `]` `` ` `` `\|` | `#` `-` `>` |

- [ ] **Step 1: Write the failing rules test**

`tests/DrMoustache.Core.Tests/Escaping/MarkdownEscaperTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Xunit;

public class MarkdownEscaperTests
{
    [Theory]
    [InlineData("anything | at * all #", MarkdownContext.FencedCode)]
    [InlineData("anything | at * all #", MarkdownContext.CodeSpan)]
    public void CodeContextsEscapeNothing(string value, MarkdownContext context)
        => MarkdownEscaper.Escape(value, context).Should().Be(value);

    [Fact]
    public void TableRowEscapesPipes()
        => MarkdownEscaper.Escape("fast | cheap", MarkdownContext.TableRow)
            .Should().Be(@"fast \| cheap");

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    public void TableRowConvertsNewlinesToBreaks(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.TableRow)
            .Should().Be("a<br>b");

    [Fact]
    public void TableRowLeavesHashesAndEmphasisAlone()
        => MarkdownEscaper.Escape("#1 *top*", MarkdownContext.TableRow)
            .Should().Be("#1 *top*");

    [Theory]
    [InlineData("#1 pick", @"\#1 pick")]
    [InlineData("> quoted", @"\> quoted")]
    [InlineData("- item", @"\- item")]
    [InlineData("* item", @"\* item")]
    [InlineData("+ item", @"\+ item")]
    [InlineData("1. first", @"1\. first")]
    public void LineStartEscapesLeadingBlockMarkers(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LineStart).Should().Be(expected);

    [Fact]
    public void LineStartLeavesLaterCharactersAlone()
        => MarkdownEscaper.Escape("ok # later", MarkdownContext.LineStart)
            .Should().Be("ok # later");

    [Fact]
    public void LineStartEscapesLeadingFourSpaceIndent()
        => MarkdownEscaper.Escape("    indented", MarkdownContext.LineStart)
            .Should().Be("&#32;   indented");

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a(b", "a%28b")]
    [InlineData("a)b", "a%29b")]
    public void LinkTargetPercentEncodes(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.LinkTarget).Should().Be(expected);

    [Theory]
    [InlineData("a*b", @"a\*b")]
    [InlineData("a_b", @"a\_b")]
    [InlineData("a[b", @"a\[b")]
    [InlineData("a]b", @"a\]b")]
    [InlineData("a`b", @"a\`b")]
    [InlineData("a|b", @"a\|b")]
    public void InlineEscapesEmphasisLinkAndPipe(string value, string expected)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(expected);

    [Theory]
    [InlineData("a#b")]
    [InlineData("a-b")]
    [InlineData("a>b")]
    public void InlineLeavesBlockMarkersAlone(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(value);

    [Theory]
    [InlineData(MarkdownContext.Inline)]
    [InlineData(MarkdownContext.LineStart)]
    [InlineData(MarkdownContext.TableRow)]
    public void HtmlEntitiesAreNeverEscaped(MarkdownContext context)
        => MarkdownEscaper.Escape("O'Brien & Sons <Ltd>", context)
            .Should().Contain("&").And.Contain("<Ltd>").And.Contain("'");

    [Theory]
    [InlineData("plain", "`")]
    [InlineData("has ` one", "``")]
    [InlineData("has `` two", "```")]
    public void FenceForWidensPastTheLongestRun(string value, string expected)
        => MarkdownEscaper.FenceFor(value).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyAndWhitespaceValuesSurvive(string value)
        => MarkdownEscaper.Escape(value, MarkdownContext.Inline).Should().Be(value);

    [Fact]
    public void UnicodeIsUntouched()
        => MarkdownEscaper.Escape("café — naïve 日本語", MarkdownContext.Inline)
            .Should().Be("café — naïve 日本語");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~MarkdownEscaperTests`
Expected: compilation failure — `MarkdownEscaper` does not exist.

- [ ] **Step 3: Write `MarkdownEscaper`**

`src/DrMoustache.Core/Escaping/MarkdownEscaper.cs`:

```csharp
namespace DrMoustache.Core.Escaping;

using System.Text;

/// <summary>Escapes an interpolated value for the Markdown construct
/// containing it.</summary>
/// <remarks>HTML entities are never escaped. Output is Markdown; the
/// downstream renderer handles HTML.</remarks>
public static class MarkdownEscaper
{
    private static readonly SearchValues<char> InlineSpecials =
        SearchValues.Create("*_[]`|");

    public static string Escape(string value, MarkdownContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return value;

        return context switch
        {
            MarkdownContext.FencedCode or MarkdownContext.CodeSpan => value,
            MarkdownContext.TableRow => EscapeTableCell(value),
            MarkdownContext.LineStart => EscapeLineStart(value),
            MarkdownContext.LinkTarget => EscapeLinkTarget(value),
            _ => EscapeInline(value),
        };
    }

    /// <summary>The backtick run needed to wrap <paramref name="value"/> as a
    /// code span without the value's own backticks closing it early.</summary>
    public static string FenceFor(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var longest = 0;
        var run = 0;

        foreach (var c in value)
        {
            if (c == '`') { run++; longest = Math.Max(longest, run); }
            else run = 0;
        }

        return new string('`', longest + 1);
    }

    private static string EscapeTableCell(string value)
    {
        var sb = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            switch (c)
            {
                case '|':
                    sb.Append(@"\|");
                    break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append("<br>");
                    break;
                case '\n':
                    sb.Append("<br>");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeLineStart(string value)
    {
        // A leading four-space indent would become a code block. Replace the
        // first space with its numeric entity, which renders as a space but
        // breaks the indent run.
        if (value.StartsWith("    ", StringComparison.Ordinal))
            return "&#32;" + value[1..];

        var first = value[0];

        if (first is '#' or '>' or '-' or '*' or '+')
            return @"\" + value;

        // An ordered-list marker: digits followed by '.' or ')'.
        var digits = 0;
        while (digits < value.Length && char.IsAsciiDigit(value[digits])) digits++;

        if (digits > 0 && digits < value.Length && value[digits] is '.' or ')')
            return string.Concat(value.AsSpan(0, digits), @"\", value.AsSpan(digits));

        return value;
    }

    private static string EscapeLinkTarget(string value)
    {
        var sb = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            switch (c)
            {
                case ' ': sb.Append("%20"); break;
                case '(': sb.Append("%28"); break;
                case ')': sb.Append("%29"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeInline(string value)
    {
        if (value.AsSpan().IndexOfAny(InlineSpecials) < 0) return value;

        var sb = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            if (InlineSpecials.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }
}
```

`SearchValues<char>` needs `using System.Buffers;` — add it to the file's using block.

- [ ] **Step 4: Run to verify the rules tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~MarkdownEscaperTests`
Expected: 34 tests passed.

- [ ] **Step 5: Write the round-trip property test**

This is the highest-value test in the project: it catches escaping defects the rules table misses, by asserting the *observable* property rather than the implementation.

`tests/DrMoustache.Core.Tests/Escaping/RoundTripTests.cs`:

```csharp
namespace DrMoustache.Core.Tests.Escaping;

using DrMoustache.Core.Escaping;
using FluentAssertions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Xunit;

public class RoundTripTests
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static TheoryData<string> HostileValues() =>
    [
        "plain",
        "fast | cheap",
        "|leading pipe",
        "trailing pipe|",
        "||double||",
        "#1 pick",
        "*emphasis*",
        "_underscore_",
        "[link](http://x)",
        "back`tick",
        "back``ticks",
        @"back\slash",
        "a\nnewline",
        "a\r\nwindows newline",
        "   leading spaces",
        "trailing spaces   ",
        "O'Brien & Sons <Ltd>",
        "café — naïve 日本語",
        "{{not a tag}}",
        "- dash start",
        "> quote start",
        "1. ordered start",
        "",
    ];

    [Theory]
    [MemberData(nameof(HostileValues))]
    public void TableCellSurvivesRoundTrip(string value)
    {
        var escaped = MarkdownEscaper.Escape(value, MarkdownContext.TableRow);
        var markdown = $"| Header |\n|--------|\n| {escaped} |\n";

        var document = Markdown.Parse(markdown, Pipeline);
        var table = document.Descendants<Table>().Should().ContainSingle().Subject;

        // Row 0 is the header; row 1 holds the value.
        table.Should().HaveCount(2, "the table must have a header and exactly one body row");

        var cellText = ExtractText(markdown, (TableRow)table[1]);
        Normalise(cellText).Should().Be(Normalise(value));
    }

    private static string ExtractText(string source, TableRow row)
    {
        var cell = (TableCell)row[0];
        var block = cell.Descendants<LeafBlock>().First();
        return block.Lines.ToString();
    }

    /// <summary>Newlines legitimately become &lt;br&gt; in a cell, and
    /// surrounding whitespace is not significant. Everything else must survive
    /// byte for byte.</summary>
    private static string Normalise(string s) => s
        .Replace("<br>", "\n", StringComparison.Ordinal)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace(@"\|", "|", StringComparison.Ordinal)
        .Trim();
}
```

- [ ] **Step 6: Run the round-trip test**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~RoundTripTests`
Expected: 23 tests passed.

If a case fails, fix `MarkdownEscaper` — not the test. The property is the requirement; the rules table is only the current implementation of it. A genuinely unrepresentable value (rather than an escaping bug) should be removed from `HostileValues` with a comment explaining why.

- [ ] **Step 7: Commit**

```bash
git add src/DrMoustache.Core/Escaping tests/DrMoustache.Core.Tests/Escaping
git commit -m "$(cat <<'EOF'
Add Markdown escaper with round-trip property test

Escapes per context from the spec's rules table. HTML entities are never
escaped -- output is Markdown, not HTML.

The round-trip test asserts the observable property rather than the
implementation: any value rendered into a table cell and re-parsed with
Markdig must come back unchanged.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

**Tasks 7–12 follow in a second file** to keep each document reviewable:

- **Task 7:** Template lexer — nodes (`TextNode`, `InterpolationNode`, `SectionNode`, `RawNode`, `HelperNode`), tag parsing, and the Markdown-aware standalone-tag rule.
- **Task 8:** Frontmatter parsing — the reserved `drmoustache` block, `RenderConfig`, and the leading-`{{` YAML error rewrite.
- **Task 9:** Value resolution — dotted paths over records, nullable handling, collection sections.
- **Task 10:** Helpers — the closed set (`date`, `format`, `upper`, `lower`, `join`, `default`).
- **Task 11:** Renderer — binding, escaping, sections, and `RenderedDocument`.
- **Task 12:** Delivery planner and architecture test — `fanout`/`collate`, slugification, the four collision policies, and the no-transport-nouns guard.

