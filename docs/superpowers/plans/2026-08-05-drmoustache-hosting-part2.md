# DrMoustache v1 Hosting Implementation Plan — Part 2: Templates and Release

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Continues** `2026-08-05-drmoustache-hosting.md`. Tasks 1–7 (contract changes, the `DrMoustache` package, DI registration, atomic writing, discovery, record validation, the renderer) must be complete before starting Task 8.

**Goal:** Ship the two `dotnet new` templates, prove they work in CI, and make the repository publishable.

**Global Constraints:** as stated in Part 1. They apply to every task here.

## The local-feed problem, and how these tasks solve it

A scaffolded project references `DrMoustache` **by package version**. During development that package does not exist on nuget.org, so a freshly instantiated template cannot restore.

Every task below that builds a scaffolded project therefore does this:

1. `dotnet pack` the solution into a local directory
2. Write a `NuGet.config` into the instantiated project's directory that adds that directory as a package source
3. Then restore and build

This is not a workaround for testing — it is how anyone will develop against an unreleased version, and getting it wrong is why template packages so often ship broken.

## `dotnet new` packaging — verified, not recalled

Confirmed end to end before this plan was written: pack → install → instantiate → build → run, with two templates in one package.

- `<PackageType>Template</PackageType>`, `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<ContentTargetFolders>content</ContentTargetFolders>`
- `<EnableDefaultItems>false</EnableDefaultItems>` plus an explicit `<Content Include="content/**/*" Exclude="content/**/bin/**;content/**/obj/**" />`, otherwise the template's own `.cs` files are compiled into the template package
- `<NoWarn>$(NoWarn);NU5128</NoWarn>` — a template package has no lib assemblies
- Multiple templates are sibling directories under `content/`, each with `.template.config/template.json`
- `sourceName` renames **both** file names and file contents
- `dotnet new install <path-to-nupkg>` / `dotnet new uninstall <PackageId>`

---

### Task 8: The templates package and the minimal template

**Files:**
- Create: `templates/DrMoustache.Templates.csproj`
- Create: `templates/content/DrMoustache.Minimal/.template.config/template.json`
- Create: `templates/content/DrMoustache.Minimal/DrMoustache.Minimal.csproj`
- Create: `templates/content/DrMoustache.Minimal/Program.cs`
- Create: `templates/content/DrMoustache.Minimal/MyRecord.cs`
- Create: `templates/content/DrMoustache.Minimal/MyProvider.cs`
- Create: `templates/content/DrMoustache.Minimal/templates/report.md`
- Create: `templates/content/DrMoustache.Minimal/appsettings.json`
- Create: `templates/content/DrMoustache.Minimal/.gitignore`
- Modify: `DrMoustache.slnx`

**Interfaces:**
- Consumes: the whole `DrMoustache` package surface from Part 1.
- Produces: `dotnet new drmoustache -n <Name>` scaffolding a runnable project.

`MyProvider` yields **one hardcoded placeholder record** rather than an empty sequence. With an empty provider, a user writes their real provider, gets nothing, and cannot tell whether their provider is wrong or the scaffold never worked. One placeholder costs three lines and makes the scaffold self-evidently sound before it is touched.

- [ ] **Step 1: Create the template package project**

`templates/DrMoustache.Templates.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageId>DrMoustache.Templates</PackageId>
    <Title>DrMoustache project templates</Title>
    <Description>dotnet new templates for DrMoustache: render Markdown from your own data.</Description>
    <PackageTags>dotnet-new;templates;markdown;mustache</PackageTags>
    <TargetFramework>net10.0</TargetFramework>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <EnableDefaultItems>false</EnableDefaultItems>
    <IsPackable>true</IsPackable>
    <!-- A template package has no lib assemblies, which NU5128 objects to. -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="content/**/*" Exclude="content/**/bin/**;content/**/obj/**" />
    <Compile Remove="**/*" />
  </ItemGroup>
</Project>
```

`<Compile Remove="**/*" />` alongside `EnableDefaultItems=false` guarantees the scaffold's `.cs` files are treated as content, never compiled.

- [ ] **Step 2: Add to `DrMoustache.slnx`**

Add a new folder:

```xml
  <Folder Name="/templates/">
    <Project Path="templates/DrMoustache.Templates.csproj" />
  </Folder>
```

- [ ] **Step 3: Write the template manifest**

`templates/content/DrMoustache.Minimal/.template.config/template.json`:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Endpoint Systems",
  "classifications": [ "Console", "Markdown", "Reporting" ],
  "identity": "DrMoustache.Minimal",
  "name": "DrMoustache report",
  "description": "A console project that renders Markdown from a data provider you write.",
  "shortName": "drmoustache",
  "sourceName": "DrMoustache.Minimal",
  "defaultName": "MyReport",
  "tags": { "language": "C#", "type": "project" },
  "preferNameDirectory": true
}
```

`sourceName` is the string replaced by `-n`, in both file names and file contents — so every file below uses `DrMoustache.Minimal` as its namespace and project name.

- [ ] **Step 4: Write the scaffolded project**

`templates/content/DrMoustache.Minimal/DrMoustache.Minimal.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>DrMoustache.Minimal</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DrMoustache" Version="0.1.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="templates/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Versions are inline here deliberately: a scaffolded project is standalone and has no `Directory.Packages.props`.

`templates/content/DrMoustache.Minimal/Program.cs`:

```csharp
namespace DrMoustache.Minimal;

using DrMoustache;
using DrMoustache.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services
            .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
            .AddSource<MyProvider>("main");

        using var host = builder.Build();

        var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

        // Pass --check to validate without reading data or writing files.
        if (args.Contains("--check", StringComparer.Ordinal))
        {
            var check = await renderer.CheckAsync().ConfigureAwait(false);
            Console.Write(check.Summary);
            return check.Success ? 0 : 1;
        }

        var result = await renderer.RenderAsync().ConfigureAwait(false);
        Console.Write(result.Summary);
        return result.Success ? 0 : 1;
    }
}
```

`templates/content/DrMoustache.Minimal/MyRecord.cs`:

```csharp
namespace DrMoustache.Minimal;

using DrMoustache.Abstractions;

/// <summary>The shape of one row of your data.</summary>
/// <remarks>Name it for the business concept, not for where the data came
/// from. DrMoustache never needs to know about your transport.</remarks>
[DrMoustacheRecord("my-record")]
public sealed record MyRecord
{
    [Field("Used in the output file name")]
    public required string Id { get; init; }

    [Field] public required string Name { get; init; }

    [Field] public required int Value { get; init; }
}
```

`templates/content/DrMoustache.Minimal/MyProvider.cs`:

```csharp
namespace DrMoustache.Minimal;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>Supplies the records your templates render.</summary>
/// <remarks>This is the file to replace. A provider is an ordinary
/// dependency-injected service, so declare whatever you need in the
/// constructor — an HttpClient, a DbContext, a queue client, IOptions.</remarks>
public sealed class MyProvider(ILogger<MyProvider> logger) : DataProvider<MyRecord>
{
    /// <summary>Matched against the <c>source:</c> key in a template.</summary>
    public override string Name => "main";

    public override async IAsyncEnumerable<MyRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        logger.LogInformation("Reading records");

        // TODO: replace everything below with your real data source.
        // The placeholder exists so `dotnet run` works before you write
        // anything — delete it once your own reads are in place.
        await Task.CompletedTask.ConfigureAwait(false);

        yield return new MyRecord { Id = "sample-1", Name = "Placeholder", Value = 42 };
    }
}
```

`templates/content/DrMoustache.Minimal/templates/report.md`:

```markdown
---
drmoustache:
  source: main
  delivery: fanout
  output: reports/{{id}}.md
  onCollision: suffix
title: "{{name}}"
---

# {{name}}

Value: {{value}}
```

Note `title:` is quoted. A frontmatter value that **begins** with `{{` must be, because YAML reads a leading `{` as a flow mapping. A tag elsewhere in a value — as in `output:` above — needs no quotes.

`templates/content/DrMoustache.Minimal/appsettings.json`:

```json
{
  "DrMoustache": {
    "TemplateDirectory": "templates",
    "OutputDirectory": "dist",
    "Strict": false
  }
}
```

`templates/content/DrMoustache.Minimal/.gitignore`:

```gitignore
bin/
obj/
dist/
```

- [ ] **Step 5: Verify the round trip by hand**

Run: `dotnet pack DrMoustache.slnx -c Release -o /tmp/drm-feed`
Expected: four `.nupkg` files — `DrMoustache.Abstractions`, `DrMoustache.Core`, `DrMoustache`, `DrMoustache.Templates`.

Run:
```bash
dotnet new install /tmp/drm-feed/DrMoustache.Templates.0.1.0.nupkg
mkdir -p /tmp/drm-try && cd /tmp/drm-try
dotnet new drmoustache -n MyReport
cd MyReport
cat > NuGet.config <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local" value="/tmp/drm-feed" />
  </packageSources>
</configuration>
XML
dotnet run
```
Expected: builds, prints `rendered 1 document`, and creates `dist/reports/sample-1.md` containing `# Placeholder` and `Value: 42`.

Run: `cat dist/reports/sample-1.md`
Expected: begins with a `---` frontmatter block containing `title: Placeholder`.

Then clean up: `dotnet new uninstall DrMoustache.Templates`

Report the actual rendered file contents — that output is the whole point of the task.

- [ ] **Step 6: Commit**

```bash
git add DrMoustache.slnx templates
git commit -m "$(cat <<'EOF'
Add the templates package and the minimal template

MyProvider yields one placeholder record rather than an empty sequence.
With an empty provider a user writes their real one, gets nothing, and
cannot tell whether their provider is wrong or the scaffold never worked.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 9: The worked-example template

**Files:**
- Create: `templates/content/DrMoustache.Sample/.template.config/template.json`
- Create: `templates/content/DrMoustache.Sample/DrMoustache.Sample.csproj`
- Create: `templates/content/DrMoustache.Sample/Program.cs`
- Create: `templates/content/DrMoustache.Sample/ObservationRecord.cs`
- Create: `templates/content/DrMoustache.Sample/SqliteOptions.cs`
- Create: `templates/content/DrMoustache.Sample/SqliteObservationProvider.cs`
- Create: `templates/content/DrMoustache.Sample/templates/station.md`
- Create: `templates/content/DrMoustache.Sample/templates/summary.md`
- Create: `templates/content/DrMoustache.Sample/appsettings.json`
- Create: `templates/content/DrMoustache.Sample/.gitignore`
- Create: `templates/content/DrMoustache.Sample/README.md`

**Interfaces:**
- Consumes: the `DrMoustache` package; `Microsoft.Data.Sqlite`.
- Produces: `dotnet new drmoustache-sample -n <Name>`.

The sample demonstrates what is easy to get wrong, not what is easy. It seeds a SQLite database on first run so the template package stays text-only — `dotnet new` templates get reviewed like source.

SQLite is used because it is the only zero-configuration data source that is still a real database. (It was originally chosen for a different reason — its native assets stressed a plugin loader that no longer exists. The justification is recorded so the old reasoning does not mislead.)

- [ ] **Step 1: Write the manifest**

`templates/content/DrMoustache.Sample/.template.config/template.json`:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Endpoint Systems",
  "classifications": [ "Console", "Markdown", "Reporting", "Sample" ],
  "identity": "DrMoustache.Sample",
  "name": "DrMoustache report (worked example)",
  "description": "A worked DrMoustache example reading from SQLite, showing fan-out, collation, escaping and helpers.",
  "shortName": "drmoustache-sample",
  "sourceName": "DrMoustache.Sample",
  "defaultName": "WeatherReport",
  "tags": { "language": "C#", "type": "project" },
  "preferNameDirectory": true
}
```

- [ ] **Step 2: Write the project and record**

`templates/content/DrMoustache.Sample/DrMoustache.Sample.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>DrMoustache.Sample</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DrMoustache" Version="0.1.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="templates/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`templates/content/DrMoustache.Sample/ObservationRecord.cs`:

```csharp
namespace DrMoustache.Sample;

using DrMoustache.Abstractions;

/// <summary>One weather observation, projected out of SQLite.</summary>
/// <remarks>Named for the business concept, not the transport. DrMoustache
/// never learns a database was involved.</remarks>
[DrMoustacheRecord("observation")]
public sealed record ObservationRecord
{
    [Field("Station identifier — used in the output file name")]
    public required string Station { get; init; }

    [Field("Degrees Celsius")]
    public required decimal Temperature { get; init; }

    [Field] public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Absent for most rows — demonstrates a section as presence.</summary>
    [Field] public string? Note { get; init; }
}
```

`templates/content/DrMoustache.Sample/SqliteOptions.cs`:

```csharp
namespace DrMoustache.Sample;

/// <summary>Bound from the <c>Sqlite</c> section of appsettings.json.</summary>
public sealed class SqliteOptions
{
    public string DatabasePath { get; set; } = "weather.db";
}
```

- [ ] **Step 3: Write the provider**

`templates/content/DrMoustache.Sample/SqliteObservationProvider.cs`:

```csharp
namespace DrMoustache.Sample;

using System.Globalization;
using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Reads observations from SQLite.</summary>
/// <remarks>An ordinary dependency-injected service: options and logging
/// arrive through the constructor like any other .NET component.</remarks>
public sealed class SqliteObservationProvider(
    IOptions<SqliteOptions> options,
    ILogger<SqliteObservationProvider> logger)
    : DataProvider<ObservationRecord>
{
    public override string Name => "observations";

    public override async IAsyncEnumerable<ObservationRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var path = options.Value.DatabasePath;
        logger.LogInformation("Reading observations from {Path}", path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT station, temperature, recorded_at, note FROM observations ORDER BY rowid";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return new ObservationRecord
            {
                Station = reader.GetString(0),
                Temperature = reader.GetDecimal(1),
                RecordedAt = DateTimeOffset.Parse(
                    reader.GetString(2), CultureInfo.InvariantCulture),
                Note = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
    }
}
```

- [ ] **Step 4: Write `Program.cs` with first-run seeding**

`templates/content/DrMoustache.Sample/Program.cs`:

```csharp
namespace DrMoustache.Sample;

using DrMoustache;
using DrMoustache.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<SqliteOptions>(builder.Configuration.GetSection("Sqlite"));

        builder.Services
            .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
            .AddSource<SqliteObservationProvider>("observations");

        using var host = builder.Build();

        Seed(host.Services.GetRequiredService<IOptions<SqliteOptions>>().Value.DatabasePath);

        var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

        if (args.Contains("--check", StringComparer.Ordinal))
        {
            var check = await renderer.CheckAsync().ConfigureAwait(false);
            Console.Write(check.Summary);
            return check.Success ? 0 : 1;
        }

        var result = await renderer.RenderAsync().ConfigureAwait(false);
        Console.Write(result.Summary);
        return result.Success ? 0 : 1;
    }

    /// <summary>Creates and fills the database on first run, so the sample is
    /// self-contained and the template package stays text-only.</summary>
    private static void Seed(string path)
    {
        if (File.Exists(path)) return;

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE observations (
                station     TEXT NOT NULL,
                temperature REAL NOT NULL,
                recorded_at TEXT NOT NULL,
                note        TEXT);

            INSERT INTO observations (station, temperature, recorded_at, note) VALUES
                ('alpha',  12.5,  '2026-08-05T09:00:00+00:00', NULL),
                ('beta',   -3.25, '2026-08-05T09:05:00+00:00',
                 'sensor drift | readings *suspect*'),
                ('beta',   -3.00, '2026-08-05T09:10:00+00:00', NULL);
            """;

        command.ExecuteNonQuery();
    }
}
```

The `beta` rows are deliberate: two share a station name, so `onCollision: suffix` fires visibly on first run, and one note contains a pipe and asterisks, so the reader can see in the output that data cannot break the table or inject emphasis.

- [ ] **Step 5: Write the templates**

`templates/content/DrMoustache.Sample/templates/station.md`:

```markdown
---
drmoustache:
  source: observations
  delivery: fanout
  output: stations/{{station}}.md
  onCollision: suffix
title: "{{station}}"
layout: observation
---

# Station {{station}}

Recorded {{date recordedAt "yyyy-MM-dd HH:mm"}} at {{temperature}}°C.

{{#note}}> {{.}}
{{/note}}
```

`templates/content/DrMoustache.Sample/templates/summary.md`:

```markdown
---
drmoustache:
  source: observations
  delivery: collate
  output: summary.md
title: All observations
---

# {{count}} observations

| Station | Temperature | Recorded | Note |
|---------|-------------|----------|------|
{{#records}}| {{station}} | {{temperature}} | {{date recordedAt "yyyy-MM-dd HH:mm"}} | {{default note "—"}} |
{{/records}}
```

The table row is the case the whole escaping engine exists for. The `beta` note contains `|` and `*`; both survive as literal characters in the rendered cell rather than splitting the row or turning italic.

- [ ] **Step 6: Write the supporting files**

`templates/content/DrMoustache.Sample/appsettings.json`:

```json
{
  "DrMoustache": {
    "TemplateDirectory": "templates",
    "OutputDirectory": "dist",
    "Strict": false
  },
  "Sqlite": {
    "DatabasePath": "weather.db"
  }
}
```

`templates/content/DrMoustache.Sample/.gitignore`:

```gitignore
bin/
obj/
dist/
weather.db
```

`templates/content/DrMoustache.Sample/README.md`: a short guide pointing at each thing worth reading — the fan-out table row in `summary.md`, the pipe and asterisks in the `beta` note and what they render as, the `{{#note}}` section being presence rather than a conditional, `{{date}}` versus `{{format}}`, and the collision warning printed on first run. Explain that `Program.cs` seeds the database only because the sample must be self-contained.

- [ ] **Step 7: Verify the round trip by hand**

Run: `dotnet pack DrMoustache.slnx -c Release -o /tmp/drm-feed`

Run:
```bash
dotnet new install /tmp/drm-feed/DrMoustache.Templates.0.1.0.nupkg
mkdir -p /tmp/drm-try2 && cd /tmp/drm-try2
dotnet new drmoustache-sample -n WeatherReport
cd WeatherReport
cat > NuGet.config <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local" value="/tmp/drm-feed" />
  </packageSources>
</configuration>
XML
dotnet run
cat dist/summary.md
```

Expected: `rendered 4 documents` plus a collision warning for `beta`; `dist/summary.md` contains a three-row table; the `beta` note renders with a literal `|` and literal asterisks rather than splitting the row or italicising.

Then: `dotnet new uninstall DrMoustache.Templates`

**Paste `dist/summary.md` into your report.** If the pipe split the row or the asterisks became emphasis, that is a Core escaping bug and must be reported, not worked around in the template.

- [ ] **Step 8: Commit**

```bash
git add templates
git commit -m "$(cat <<'EOF'
Add the worked-example template

Seeds SQLite on first run so the sample is self-contained and the template
package stays text-only -- dotnet new templates get reviewed like source.

The fixture data is chosen to be instructive: two rows share a station so
onCollision fires visibly, and one note contains a pipe and asterisks so
the reader can see data cannot break the table or inject emphasis.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 10: Template verification in CI

**Files:**
- Create: `tests/DrMoustache.Templates.Tests/DrMoustache.Templates.Tests.csproj`
- Create: `tests/DrMoustache.Templates.Tests/TemplateFixture.cs`
- Create: `tests/DrMoustache.Templates.Tests/MinimalTemplateTests.cs`
- Create: `tests/DrMoustache.Templates.Tests/SampleTemplateTests.cs`
- Modify: `DrMoustache.slnx`

**Interfaces:**
- Consumes: the packed output of the whole solution.
- Produces: automated proof that both templates instantiate, restore, build, and run.

**This is the most important task in Part 2.** A template package is code nobody compiles until a stranger does. A scaffold that does not build is the single most damaging defect this project can ship, because it is the first thing every user sees.

The fixture packs the solution to a temp feed, installs the template package, and instantiates into a temp directory with a `NuGet.config` pointing at that feed. It runs once for the whole class.

- [ ] **Step 1: Create the test project**

`tests/DrMoustache.Templates.Tests/DrMoustache.Templates.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <RootNamespace>DrMoustache.Templates.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

No project references: these tests exercise the *packaged* artifacts, not the source tree. That is the point.

Add to `DrMoustache.slnx` under `/tests/`:

```xml
    <Project Path="tests/DrMoustache.Templates.Tests/DrMoustache.Templates.Tests.csproj" />
```

- [ ] **Step 2: Write the fixture**

`tests/DrMoustache.Templates.Tests/TemplateFixture.cs`:

```csharp
namespace DrMoustache.Templates.Tests;

using System.Diagnostics;

/// <summary>Packs the solution to a local feed and installs the template
/// package once for the whole test class.</summary>
public sealed class TemplateFixture : IDisposable
{
    private const string PackageId = "DrMoustache.Templates";

    public string Feed { get; }
    public string WorkRoot { get; }

    public TemplateFixture()
    {
        Feed = Directory.CreateTempSubdirectory("drm-feed-").FullName;
        WorkRoot = Directory.CreateTempSubdirectory("drm-work-").FullName;

        var repoRoot = FindRepositoryRoot();

        Run("dotnet", ["pack", Path.Combine(repoRoot, "DrMoustache.slnx"),
            "-c", "Release", "-o", Feed, "--nologo"], repoRoot);

        var nupkg = Directory.EnumerateFiles(Feed, $"{PackageId}.*.nupkg").Single();

        // Uninstall first so a leftover install from a previous run, or from
        // the developer's own machine, cannot mask a broken package.
        RunAllowingFailure("dotnet", ["new", "uninstall", PackageId], repoRoot);
        Run("dotnet", ["new", "install", nupkg], repoRoot);
    }

    /// <summary>Instantiates a template and points it at the local feed.</summary>
    /// <returns>The instantiated project directory.</returns>
    public string Instantiate(string shortName, string projectName)
    {
        var into = Path.Combine(WorkRoot, projectName);
        Directory.CreateDirectory(into);

        Run("dotnet", ["new", shortName, "-n", projectName, "-o", into], WorkRoot);

        File.WriteAllText(Path.Combine(into, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{Feed}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        return into;
    }

    public static (int ExitCode, string Output) TryRun(
        string file, string[] arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {file}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    private static void Run(string file, string[] arguments, string workingDirectory)
    {
        var (exitCode, output) = TryRun(file, arguments, workingDirectory);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"{file} {string.Join(' ', arguments)} failed ({exitCode}):\n{output}");
        }
    }

    private static void RunAllowingFailure(
        string file, string[] arguments, string workingDirectory)
        => TryRun(file, arguments, workingDirectory);

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("tests must run inside the repository");
    }

    public void Dispose()
    {
        RunAllowingFailure("dotnet", ["new", "uninstall", PackageId], WorkRoot);

        foreach (var d in new[] { Feed, WorkRoot })
        {
            try { Directory.Delete(d, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
```

- [ ] **Step 3: Write the minimal-template tests**

`tests/DrMoustache.Templates.Tests/MinimalTemplateTests.cs`:

```csharp
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
}
```

- [ ] **Step 4: Write the sample-template tests**

`tests/DrMoustache.Templates.Tests/SampleTemplateTests.cs`:

```csharp
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
```

Add one more test to `MinimalTemplateTests`, guarding the defect Task 8 uncovered:

```csharp
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

        scaffold.Should().Contain($"""Include="DrMoustache" Version="{declared}"""",
            "the scaffold must reference the version this repository packs");
    }
```

This needs `TemplateFixture.RepositoryRoot` to be exposed — change the fixture's private `FindRepositoryRoot()` into a `public static string RepositoryRoot { get; }` initialised once, and use it from both places.

`TheCollatedTableSurvivesHostileData` is the end-to-end proof of the project's whole premise: real data containing a pipe, rendered through the real pipeline into a real file, and the table is still a table.

- [ ] **Step 5: Run the tests**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~TemplateTests`
Expected: 8 tests passed. These are slow — each packs, installs, restores and builds — so expect minutes rather than seconds.

If `TheCollatedTableSurvivesHostileData` fails, **stop and report**: that is a Core escaping regression, not a template problem.

- [ ] **Step 6: Commit**

```bash
git add DrMoustache.slnx tests/DrMoustache.Templates.Tests
git commit -m "$(cat <<'EOF'
Verify both templates end to end

A template package is code nobody compiles until a stranger does, and a
scaffold that does not build is the most damaging defect this project can
ship. These tests pack, install, instantiate, restore, build and run both
templates, and assert the rendered output.

The collated-table test is the end-to-end proof of the premise: real data
containing a pipe, through the real pipeline, into a real file, and the
table is still a table.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 11: README, package readme, and metadata

**Files:**
- Create: `README.md`
- Create: `PACKAGE.md`
- Modify: `Directory.Build.props`

**Interfaces:**
- Consumes: everything.
- Produces: a publishable repository.

- [ ] **Step 1: Write `README.md`**

Cover, in this order:

1. **What it is**, in two sentences.
2. **The non-goals block, verbatim from the spec, prominently** — this is the thing that stops people asking for conditionals:
   > DrMoustache puts your fields into Markdown. It is not a logic engine. There are no conditionals, no expressions, no computation, no branching. If your output depends on a decision, make that decision in your provider — that is what it is for. DrMoustache writes files.
3. **A 60-second start**: `dotnet new install`, `dotnet new drmoustache`, edit the provider, `dotnet run`.
4. **What a template looks like** — the fan-out example, with the escaping point made explicitly: a value containing `|` cannot break the table.
5. **What a provider looks like** — the constructor-injection shape.
6. **The four packages**, and that `DrMoustache` is the one to reference.
7. **State plainly that you write the provider.** DrMoustache ships no data sources; that is the design, not a gap.
8. **Versioning**: the entire public surface is supported API; pre-1.0 means breaking changes may land in minor releases and will be listed in release notes.
9. Links to `docs/known-issues.md` and the spec.
10. How it composes with [Crucible](https://github.com/phoenixmldb/crucible) — `dotnet run` produces Markdown, Crucible turns it into a site.
11. Apache-2.0.

- [ ] **Step 2: Write `PACKAGE.md`**

Short, NuGet-facing: what the package is, the minimal `Program.cs`, a pointer to the templates and the repository. NuGet renders this on the package page, so keep it under a screen.

- [ ] **Step 3: Add metadata to `Directory.Build.props`**

Add to the second `<PropertyGroup>`. Note `<Version>0.1.0</Version>` is **already present** — Task 8's fix round moved it forward so the scaffold could reference a real version. Do not add it twice:

```xml
    <PackageReadmeFile>PACKAGE.md</PackageReadmeFile>
    <PackageTags>markdown;mustache;templating;reporting;codegen</PackageTags>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
```

and a new `<ItemGroup>`:

```xml
  <ItemGroup Condition="'$(IsPackable)' == 'true'">
    <None Include="$(MSBuildThisFileDirectory)PACKAGE.md" Pack="true" PackagePath="" />
  </ItemGroup>
```

- [ ] **Step 4: Verify packing**

Run: `dotnet pack DrMoustache.slnx -c Release -o /tmp/drm-pack`
Expected: four `.nupkg` files, no `NU5039` or `NU5048` warnings about the readme.

Run: `unzip -l /tmp/drm-pack/DrMoustache.0.1.0.nupkg | grep -E "PACKAGE.md|nuspec"`
Expected: `PACKAGE.md` present at the package root.

Run: `unzip -p /tmp/drm-pack/DrMoustache.0.1.0.nupkg DrMoustache.nuspec | grep -A6 dependencies`
Expected: `DrMoustache.Abstractions` and `DrMoustache.Core` listed as dependencies — the proof it is a working metapackage.

- [ ] **Step 5: Commit**

```bash
git add README.md PACKAGE.md Directory.Build.props
git commit -m "$(cat <<'EOF'
Add README, package readme, and metadata

The non-goals block is quoted verbatim and placed prominently: it is what
stops people asking for conditionals, and it is easier to point at than to
re-argue.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 12: CI and the release checklist

**Files:**
- Create: `.github/workflows/build.yml`
- Create: `docs/releasing.md`
- Modify: `docs/known-issues.md`

**Interfaces:**
- Consumes: everything.
- Produces: CI that would have caught every defect this plan guards against, and a written release procedure.

- [ ] **Step 1: Write the workflow**

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

      # Template tests pack and install, so they must not use --no-build:
      # they exercise the packaged artifacts, not this build's output.
      - name: Test
        run: dotnet test DrMoustache.slnx -c Release --no-build

      - name: Pack
        run: dotnet pack DrMoustache.slnx -c Release --no-build -o ./artifacts

      - uses: actions/upload-artifact@v4
        with:
          name: packages
          path: ./artifacts/*.nupkg
```

- [ ] **Step 2: Verify CI passes locally in the same order**

Run, in sequence, and report each result:

```bash
dotnet restore DrMoustache.slnx
dotnet build DrMoustache.slnx -c Release --no-restore
dotnet test DrMoustache.slnx -c Release --no-build
dotnet pack DrMoustache.slnx -c Release --no-build -o ./artifacts
```

Expected: 0 warnings from build; every test passing; four packages produced.

If the template tests fail under `--no-build` but pass otherwise, report it — the fixture packs the solution itself, so there may be an ordering assumption to fix rather than a flag to change.

- [ ] **Step 3: Write `docs/releasing.md`**

A checklist, not prose:

1. Every test green, including the template tests
2. `docs/known-issues.md` reviewed — anything fixed this cycle removed
3. `<Version>` bumped in `Directory.Build.props`
4. Release notes drafted, **listing any breaking change** — required, because pre-1.0 permits them in minor releases and the README promises they will be listed
5. `dotnet pack -c Release`
6. Smoke test from the packed output: install the templates package from the local feed, instantiate both templates, `dotnet run` each
7. `dotnet nuget push` in dependency order — `Abstractions`, `Core`, `DrMoustache`, `Templates`
8. Tag the commit, publish a GitHub release with the notes

Step 6 is not optional: the template package references the other packages **by version**, so a version bump that misses one produces a scaffold that cannot restore — and it will restore fine locally from the source tree while being broken for everyone else.

- [ ] **Step 4: Update `docs/known-issues.md`**

Remove the two entries this work resolves:

- **R5** — `TemplateValidator.Validate`'s `DeliveryMode` is now required, and the renderer passes each template's own mode
- **Package metadata is missing** — resolved by Task 11

Add one entry under a new *Delivery model* heading, recording the pivot so the reasoning is not lost:

> **The CLI and plugin loading are deferred.** v1 ships as a `dotnet new` template with dependency injection. The plugin design is preserved in `docs/superpowers/plans/2026-08-02-drmoustache-cli*.md`, marked deferred, and becomes worth building when a provider is written once and consumed by many rather than written alongside the templates that use it.

- [ ] **Step 5: Commit**

```bash
git add .github docs
git commit -m "$(cat <<'EOF'
Add CI and the release checklist

The release checklist's smoke step is not optional: the templates package
references the other packages by version, so a bump that misses one
produces a scaffold that cannot restore -- and it will restore fine
locally from the source tree while being broken for everyone else.

Closes known-issues R5 and the package-metadata entry.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

## Self-Review

**1. Spec coverage.** Every section of `2026-08-04-drmoustache-v1-design.md` maps to a task:

| Spec section | Task |
|---|---|
| Packages — four at 0.1.0, `DrMoustache` as metapackage | P1-2, 11 |
| Provider contract — `ProviderContext` deleted, `ReadAsync(ct)` | P1-1 |
| Abstractions has zero package dependencies | P1-1 (asserted by test) |
| `AddDrMoustache`, `IDrMoustacheBuilder`, `AddSource<T>` | P1-3 |
| `SourceRegistration` and why it exists | P1-3 |
| `DrMoustacheOptions` incl. `Culture` | P1-2, and P1-1 for the `Plan` parameter |
| `IDocumentRenderer`, `RenderResult`, `CheckResult`, `RecordSkip` | P1-6, P1-7 |
| `RenderAsync` validates before reading | P1-7 |
| Validation — `DeliveryMode` required, R5 resolved | P1-1, P1-7, 12 |
| Record-level skips, `Strict` | P1-6, P1-7 |
| Documents never partially written | P1-4 |
| Minimal template with a placeholder record | 8 |
| Worked-example template, seeded SQLite, the six teaching points | 9 |
| Templates instantiated, built and run in CI | 10 |
| Versioning and the API commitment | 11 |
| Deferred CLI recorded | 12 |
| Testing at three levels | P1 unit tasks, P1-7 integration, 10 template |

**2. Placeholder scan.** No `TBD` or "add error handling" instructions. Every code step carries complete, compilable code. Two deliberate exceptions, both long-form prose rather than code, and both with the required content enumerated: the sample's `README.md` (Task 9 Step 6) and the repository `README.md` (Task 11 Step 1). `docs/releasing.md` is specified as a numbered checklist with every item stated.

**3. Type consistency.** Verified across both parts: `DrMoustacheOptions` (P1-2) is consumed by P1-3, P1-7. `SourceRegistration` (P1-3) is consumed by `DocumentRenderer` (P1-7). `RecordSkip`, `RenderResult`, `CheckResult` are all defined once in P1-6's `RenderResult.cs` and consumed by P1-7 and Task 10's assertions. `TemplateFile` (P1-5) is consumed by P1-7. `DocumentWriter` (P1-4) is consumed by P1-7. `RenderException` (P1-5) is thrown by both P1-5 and P1-7. `IDocumentRenderer` (P1-7) is resolved in both templates (Tasks 8, 9) and asserted in Task 10.

Two gaps found and closed while reviewing:

- `DeliveryPlanner.Plan` had no culture parameter, so `DrMoustacheOptions.Culture` would have bound from configuration and then silently done nothing. Added to P1-1.
- Task 10's fixture originally installed the template package without uninstalling first, so a leftover install from a developer's machine could mask a broken package and make CI disagree with local runs. It now uninstalls first.
