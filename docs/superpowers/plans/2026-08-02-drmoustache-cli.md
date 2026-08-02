# DrMoustache CLI Implementation Plan — Part 1: Host and Plugin Loading

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `DrMoustache.Cli` host — configuration, plugin acquisition, and plugin loading — so that a third-party provider packaged as a NuGet package can be resolved, loaded with its full transitive dependency tree, and asked for records.

**Architecture:** The CLI builds an `IConfiguration` chain (optional `appsettings.json` → environment → command line), reads a `drmoustache.yaml` describing plugins and sources, acquires each plugin by generating a throwaway project and running `dotnet publish` on it, then loads the published output in a collectible `AssemblyLoadContext` backed by an `AssemblyDependencyResolver`. `DrMoustache.Abstractions` is deliberately deferred to the default load context so type identity holds across the boundary.

**Tech Stack:** .NET 10, `System.CommandLine` 2.0.5 (GA), `Microsoft.Extensions.Configuration` (+ Json, EnvironmentVariables, CommandLine), `YamlDotNet`, xunit.v3, FluentAssertions 6.12.2.

**Part 2** (`2026-08-02-drmoustache-cli-part2.md`) covers the `render`, `check` and `init` verbs, atomic document writing, record-level error collection, `docs/plugin-architecture.md`, and package metadata.

## Global Constraints

Copied from the spec and from Part 1's established conventions. Every task's requirements implicitly include this section.

- `TargetFramework`: `net10.0`; `LangVersion`: `preview`; `Nullable`: `enable`; `ImplicitUsings`: `enable`
- `TreatWarningsAsErrors`: `true`, `EnforceCodeStyleInBuild`: `true`, `AnalysisLevel`: `latest-all`
- Central package management — every `PackageReference` gets its version from `Directory.Packages.props`, never inline
- FluentAssertions pinned at **6.12.2** (last MIT release). Do not bump.
- Namespace root `DrMoustache`. File-scoped namespaces, `using` directives inside the namespace.
- License Apache-2.0; Authors/Company `Endpoint Systems`
- **Fix the cause of any warning rather than suppressing it.** If a suppression is genuinely unavoidable, scope it tightly (never file-wide), and justify it at the site. A file-wide pragma was rejected in review during Part 1.
- `DrMoustache.Core` must remain I/O-free. **All filesystem access added by this plan lives in `DrMoustache.Cli`.** Core's architecture tests will fail the build if this is violated.
- Governing principle: **"Markdown is the layout, Mustache is the data."**

## Two decisions this plan makes, stated up front

**1. Plugins are acquired with `dotnet publish`, not the NuGet client libraries.**

The CLI generates a throwaway `.csproj` containing a single `PackageReference` and runs `dotnet publish` on it. The published output folder is the plugin: a flat directory of assemblies plus the `.deps.json` that `AssemblyDependencyResolver` needs, with the full transitive closure and any native assets already laid out correctly.

The alternative — `NuGet.Protocol` + `NuGet.Packaging` — means resolving the dependency graph, selecting target-framework assets, and laying out native runtime assets by hand. That is a large, version-coupled dependency and a great deal of code to reproduce something the SDK already does correctly.

**Trade-off:** this requires the .NET **SDK** at runtime, not merely the runtime. That is acceptable because `dotnet tool install` requires the SDK anyway, so anyone who can install DrMoustache already has it. Task 5 fails with a clear message if `dotnet` is absent.

**2. Records are materialized before delivery planning.**

`DeliveryPlanner.Plan` takes `IReadOnlyList<object>`, not `IAsyncEnumerable`. This is inherent rather than an oversight: collision detection cannot name *any* output file until it has seen *every* record, because a collision is a property of the whole set. The provider contract stays `IAsyncEnumerable` so a provider can stream and be cancelled, but the CLI drains it before planning. Task 7 documents this in the provider-facing docs.

## File Structure

```
src/DrMoustache.Cli/
  DrMoustache.Cli.csproj
  Program.cs                              root command wiring only
  Configuration/CliConfiguration.cs       builds the IConfiguration chain
  Configuration/DrMoustacheFile.cs        drmoustache.yaml model + loader
  Plugins/PluginLoadContext.cs            ALC + AssemblyDependencyResolver
  Plugins/PluginPackage.cs                a located plugin on disk
  Plugins/PluginAcquisition.cs            generate project, dotnet publish, cache
  Plugins/ProviderRegistry.cs             config sources -> IDataProvider

samples/DrMoustache.Sample.Plugin/
  DrMoustache.Sample.Plugin.csproj
  ObservationRecord.cs
  SqliteObservationProvider.cs

tests/DrMoustache.Cli.Tests/
  DrMoustache.Cli.Tests.csproj
  Configuration/CliConfigurationTests.cs
  Configuration/DrMoustacheFileTests.cs
  Plugins/PluginLoadContextTests.cs
  Plugins/ProviderRegistryTests.cs
  Plugins/PluginIntegrationTests.cs
```

---

### Task 1: CLI project scaffolding and command shell

**Files:**
- Create: `src/DrMoustache.Cli/DrMoustache.Cli.csproj`
- Create: `src/DrMoustache.Cli/Program.cs`
- Create: `tests/DrMoustache.Cli.Tests/DrMoustache.Cli.Tests.csproj`
- Modify: `Directory.Packages.props`
- Modify: `DrMoustache.slnx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: a runnable `drmoustache` executable with `render`, `check` and `init` subcommands whose actions are stubs returning exit code 0. Later tasks replace the stub bodies. `Program.BuildRootCommand()` is `internal static RootCommand BuildRootCommand()` so tests can parse without launching a process.

**The `System.CommandLine` 2.0.5 API has been verified by compiling and running it.** Use exactly these shapes — older beta APIs (`AddOption`, `SetHandler`, `Command.Handler`) do not exist in 2.0.x:

```csharp
new Option<string>("--source") { Description = "…", Required = true }
new Argument<DirectoryInfo>("templates") { Description = "…" }
command.Options.Add(opt); command.Arguments.Add(arg); root.Subcommands.Add(cmd);
command.SetAction((ParseResult pr, CancellationToken ct) => Task.FromResult(0));
return await root.Parse(args).InvokeAsync();
parseResult.GetValue(option)
```

- [ ] **Step 1: Add packages to `Directory.Packages.props`**

Add inside the first `<ItemGroup>`:

```xml
    <PackageVersion Include="System.CommandLine" Version="2.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.CommandLine" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="10.0.0" />
```

`Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Logging.Abstractions` are already present from Part 1.

- [ ] **Step 2: Create `src/DrMoustache.Cli/DrMoustache.Cli.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>true</IsPackable>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>drmoustache</ToolCommandName>
    <RootNamespace>DrMoustache.Cli</RootNamespace>
    <AssemblyName>drmoustache</AssemblyName>
    <Description>Renders Markdown templates against data supplied by a provider plugin.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
    <PackageReference Include="Microsoft.Extensions.Configuration.CommandLine" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
    <PackageReference Include="YamlDotNet" />
    <ProjectReference Include="../DrMoustache.Core/DrMoustache.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DrMoustache.Cli.Tests" />
  </ItemGroup>
</Project>
```

`AssemblyName` is `drmoustache` so the tool's own assembly name matches the command; `RootNamespace` keeps the namespace `DrMoustache.Cli`.

- [ ] **Step 3: Create `tests/DrMoustache.Cli.Tests/DrMoustache.Cli.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <RootNamespace>DrMoustache.Cli.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/DrMoustache.Cli/DrMoustache.Cli.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add both projects to `DrMoustache.slnx`**

Add to the existing `/src/` folder:
```xml
    <Project Path="src/DrMoustache.Cli/DrMoustache.Cli.csproj" />
```
and to the existing `/tests/` folder:
```xml
    <Project Path="tests/DrMoustache.Cli.Tests/DrMoustache.Cli.Tests.csproj" />
```

- [ ] **Step 5: Write the failing test**

`tests/DrMoustache.Cli.Tests/CommandShellTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests;

using System.CommandLine;
using FluentAssertions;
using Xunit;

public class CommandShellTests
{
    private static RootCommand Root() => Program.BuildRootCommand();

    [Theory]
    [InlineData("render")]
    [InlineData("check")]
    [InlineData("init")]
    public void ExposesTheThreeVerbs(string verb)
        => Root().Subcommands.Should().ContainSingle(c => c.Name == verb);

    [Fact]
    public void ExposesNoOtherVerbs()
        => Root().Subcommands.Select(c => c.Name)
            .Should().BeEquivalentTo(["render", "check", "init"]);

    [Fact]
    public void RenderTakesATemplateDirectoryAndAnOutputDirectory()
    {
        var result = Root().Parse("render ./templates --output ./dist");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void RenderRejectsAMissingTemplateDirectoryArgument()
        => Root().Parse("render --output ./dist").Errors.Should().NotBeEmpty();

    [Fact]
    public void RenderAcceptsStrict()
        => Root().Parse("render ./t --output ./d --strict").Errors.Should().BeEmpty();

    [Fact]
    public void CheckTakesATemplateDirectoryButNoOutput()
    {
        Root().Parse("check ./templates").Errors.Should().BeEmpty();
        Root().Parse("check ./templates --output ./dist").Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InitTakesAnOptionalDirectory()
    {
        Root().Parse("init").Errors.Should().BeEmpty();
        Root().Parse("init ./somewhere").Errors.Should().BeEmpty();
    }

    [Fact]
    public void ConfigOptionIsAvailableOnEveryVerb()
    {
        Root().Parse("render ./t --output ./d --config ./c.yaml").Errors.Should().BeEmpty();
        Root().Parse("check ./t --config ./c.yaml").Errors.Should().BeEmpty();
        Root().Parse("init --config ./c.yaml").Errors.Should().BeEmpty();
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CommandShellTests`
Expected: compilation failure — `Program.BuildRootCommand` does not exist.

- [ ] **Step 7: Write `Program.cs`**

```csharp
namespace DrMoustache.Cli;

using System.CommandLine;

/// <summary>Entry point and command wiring. Verb bodies live in their own
/// types; this file only describes the surface.</summary>
internal static class Program
{
    internal static async Task<int> Main(string[] args)
        => await BuildRootCommand().Parse(args).InvokeAsync().ConfigureAwait(false);

    internal static RootCommand BuildRootCommand()
    {
        var config = new Option<FileInfo?>("--config")
        {
            Description = "Path to drmoustache.yaml. Defaults to ./drmoustache.yaml if present.",
        };

        var root = new RootCommand(
            "Renders Markdown templates against data supplied by a provider plugin.");

        root.Subcommands.Add(BuildRenderCommand(config));
        root.Subcommands.Add(BuildCheckCommand(config));
        root.Subcommands.Add(BuildInitCommand(config));

        return root;
    }

    private static Command BuildRenderCommand(Option<FileInfo?> config)
    {
        var templates = new Argument<DirectoryInfo>("templates")
        {
            Description = "Directory containing .md templates.",
        };

        var output = new Option<DirectoryInfo>("--output")
        {
            Description = "Directory to write rendered Markdown into.",
            Required = true,
        };

        var strict = new Option<bool>("--strict")
        {
            Description = "Treat any skipped record as a failure (exit 1).",
        };

        var command = new Command("render", "Bind data to templates and write Markdown.");
        command.Arguments.Add(templates);
        command.Options.Add(output);
        command.Options.Add(strict);
        command.Options.Add(config);

        // Task 9 replaces this body.
        command.SetAction((ParseResult _, CancellationToken _) => Task.FromResult(0));
        return command;
    }

    private static Command BuildCheckCommand(Option<FileInfo?> config)
    {
        var templates = new Argument<DirectoryInfo>("templates")
        {
            Description = "Directory containing .md templates.",
        };

        var command = new Command(
            "check", "Validate templates and plugin loading without reading data or writing output.");
        command.Arguments.Add(templates);
        command.Options.Add(config);

        // Task 11 replaces this body.
        command.SetAction((ParseResult _, CancellationToken _) => Task.FromResult(0));
        return command;
    }

    private static Command BuildInitCommand(Option<FileInfo?> config)
    {
        var directory = new Argument<DirectoryInfo?>("directory")
        {
            Description = "Where to scaffold. Defaults to the current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command("init", "Scaffold a starter template and drmoustache.yaml.");
        command.Arguments.Add(directory);
        command.Options.Add(config);

        // Task 12 replaces this body.
        command.SetAction((ParseResult _, CancellationToken _) => Task.FromResult(0));
        return command;
    }
}
```

- [ ] **Step 8: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CommandShellTests`
Expected: 12 tests passed.

Run: `dotnet build DrMoustache.slnx`
Expected: build succeeds, **0 warnings**.

- [ ] **Step 9: Verify the tool actually runs**

Run: `dotnet run --project src/DrMoustache.Cli -- --help`
Expected: usage listing `render`, `check`, `init`.

Run: `dotnet run --project src/DrMoustache.Cli -- render --help`
Expected: shows the `templates` argument and `--output` marked `(REQUIRED)`.

- [ ] **Step 10: Commit**

```bash
git add Directory.Packages.props DrMoustache.slnx src/DrMoustache.Cli tests/DrMoustache.Cli.Tests
git commit -m "$(cat <<'EOF'
Add CLI project and command shell

Three verbs -- render, check, init -- with parsing verified by tests that
call BuildRootCommand directly rather than launching a process. Verb
bodies are stubs; later tasks fill them in.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 2: The configuration chain

**Files:**
- Create: `src/DrMoustache.Cli/Configuration/CliConfiguration.cs`
- Test: `tests/DrMoustache.Cli.Tests/Configuration/CliConfigurationTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `internal static IConfigurationRoot CliConfiguration.Build(string? appSettingsPath, IReadOnlyDictionary<string, string?> commandLineValues, string basePath)`

The spec's requirement, verbatim: *"`IConfiguration` first, environment variables second. The CLI builds a chain: `appsettings.json` (optional; a missing file is not an error) → environment variables → command-line arguments. Because environment is a provider in the chain, a user with no configuration file still gets environment variables with no special-casing, and `dotnet user-secrets` works in development for free."*

Environment variables are read with the prefix `DRMOUSTACHE_`, and the provider strips it — so `DRMOUSTACHE_sources__dlq__connection` sets `sources:dlq:connection`.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Configuration/CliConfigurationTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Configuration;

using DrMoustache.Cli.Configuration;
using FluentAssertions;
using Xunit;

public class CliConfigurationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drm-config-").FullName;
    private readonly List<string> _setVariables = [];

    private void SetEnv(string key, string? value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _setVariables.Add(key);
    }

    public void Dispose()
    {
        foreach (var key in _setVariables) Environment.SetEnvironmentVariable(key, null);
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingAppSettingsIsNotAnError()
    {
        var config = CliConfiguration.Build(
            appSettingsPath: Path.Combine(_dir, "does-not-exist.json"),
            commandLineValues: new Dictionary<string, string?>(),
            basePath: _dir);

        config.Should().NotBeNull();
        config["anything"].Should().BeNull();
    }

    [Fact]
    public void ReadsAppSettingsWhenPresent()
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, """{ "sources": { "dlq": { "kind": "from-file" } } }""");

        var config = CliConfiguration.Build(path, new Dictionary<string, string?>(), _dir);

        config["sources:dlq:kind"].Should().Be("from-file");
    }

    [Fact]
    public void EnvironmentOverridesAppSettings()
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, """{ "sources": { "dlq": { "kind": "from-file" } } }""");
        SetEnv("DRMOUSTACHE_sources__dlq__kind", "from-env");

        var config = CliConfiguration.Build(path, new Dictionary<string, string?>(), _dir);

        config["sources:dlq:kind"].Should().Be("from-env");
    }

    [Fact]
    public void EnvironmentWorksWithNoConfigurationFileAtAll()
    {
        SetEnv("DRMOUSTACHE_sources__dlq__kind", "from-env");

        var config = CliConfiguration.Build(
            appSettingsPath: null, new Dictionary<string, string?>(), _dir);

        config["sources:dlq:kind"].Should().Be("from-env");
    }

    [Fact]
    public void CommandLineOverridesEnvironment()
    {
        SetEnv("DRMOUSTACHE_sources__dlq__kind", "from-env");

        var config = CliConfiguration.Build(
            appSettingsPath: null,
            commandLineValues: new Dictionary<string, string?>
            {
                ["sources:dlq:kind"] = "from-args",
            },
            basePath: _dir);

        config["sources:dlq:kind"].Should().Be("from-args");
    }

    [Fact]
    public void UnprefixedEnvironmentVariablesAreIgnored()
    {
        SetEnv("sources__dlq__kind", "leaked");

        var config = CliConfiguration.Build(null, new Dictionary<string, string?>(), _dir);

        config["sources:dlq:kind"].Should().BeNull();
    }

    [Fact]
    public void SectionsAreRetrievableForScopedProviderAccess()
    {
        SetEnv("DRMOUSTACHE_sources__dlq__kind", "x");
        SetEnv("DRMOUSTACHE_sources__other__kind", "y");

        var config = CliConfiguration.Build(null, new Dictionary<string, string?>(), _dir);

        config.GetSection("sources:dlq")["kind"].Should().Be("x");
        config.GetSection("sources:dlq")["other"].Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CliConfigurationTests`
Expected: compilation failure — `CliConfiguration` does not exist.

- [ ] **Step 3: Write `CliConfiguration`**

`src/DrMoustache.Cli/Configuration/CliConfiguration.cs`:

```csharp
namespace DrMoustache.Cli.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>Builds the configuration chain the spec requires:
/// appsettings.json (optional) then environment then command line.</summary>
/// <remarks>Environment is a provider *in* the chain rather than a separate
/// lookup, so a user with no configuration file still gets environment
/// variables with no special-casing, and <c>dotnet user-secrets</c> works in
/// development for free.</remarks>
internal static class CliConfiguration
{
    /// <summary>Environment variables must carry this prefix; the provider
    /// strips it, so DRMOUSTACHE_sources__dlq__kind sets sources:dlq:kind.</summary>
    internal const string EnvironmentPrefix = "DRMOUSTACHE_";

    internal static IConfigurationRoot Build(
        string? appSettingsPath,
        IReadOnlyDictionary<string, string?> commandLineValues,
        string basePath)
    {
        ArgumentNullException.ThrowIfNull(commandLineValues);
        ArgumentNullException.ThrowIfNull(basePath);

        var builder = new ConfigurationBuilder().SetBasePath(basePath);

        if (!string.IsNullOrWhiteSpace(appSettingsPath))
            builder.AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false);

        builder.AddEnvironmentVariables(EnvironmentPrefix);
        builder.AddInMemoryCollection(commandLineValues);

        return builder.Build();
    }
}
```

`AddInMemoryCollection` is used rather than `AddCommandLine` because the CLI has already parsed its arguments with `System.CommandLine`; re-parsing `args` a second time with a different parser would let the two disagree. The caller projects parsed values into the dictionary.

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~CliConfigurationTests`
Expected: 7 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Cli/Configuration tests/DrMoustache.Cli.Tests/Configuration
git commit -m "$(cat <<'EOF'
Add the configuration chain

appsettings.json (optional) then environment then command line. Environment
is a provider in the chain rather than a separate lookup, so a user with no
config file still gets environment variables and user-secrets works in dev.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 3: The `drmoustache.yaml` model

**Files:**
- Create: `src/DrMoustache.Cli/Configuration/DrMoustacheFile.cs`
- Test: `tests/DrMoustache.Cli.Tests/Configuration/DrMoustacheFileTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `internal sealed record PluginReference(string PackageId, string Version)` with `static PluginReference Parse(string spec)` accepting `Id@Version`
  - `internal sealed record SourceDefinition(string Type, bool ScopeConfig, IReadOnlyDictionary<string, string?> Settings)`
  - `internal sealed record DrMoustacheFile(IReadOnlyList<PluginReference> Plugins, IReadOnlyDictionary<string, SourceDefinition> Sources)` with `static DrMoustacheFile Parse(string yaml)` and `static DrMoustacheFile Load(string path)`
  - `internal sealed class ConfigurationException : Exception`

The file shape, from the spec:

```yaml
plugins:
  - DrMoustache.ServiceBus@1.0.0

sources:
  dlq:
    type: servicebus-dlq
    scopeConfig: true
    namespace: ${SB_NAMESPACE}
    queue: orders
```

`type` and `scopeConfig` are reserved; every other key under a source is a free-form setting passed through to the provider. `${VAR}` expansion is **not** done here — the settings reach the provider through `IConfiguration`, which the CLI populates from this file plus the environment chain in Task 6.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Configuration/DrMoustacheFileTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Configuration;

using DrMoustache.Cli.Configuration;
using FluentAssertions;
using Xunit;

public class DrMoustacheFileTests
{
    private const string Sample = """
        plugins:
          - DrMoustache.Sample.Plugin@1.0.0
          - Other.Provider@2.1.3

        sources:
          dlq:
            type: servicebus-dlq
            scopeConfig: true
            queue: orders
          rows:
            type: sqlite
            path: ./app.db
        """;

    [Fact]
    public void ReadsPluginReferences()
    {
        var file = DrMoustacheFile.Parse(Sample);
        file.Plugins.Should().HaveCount(2);
        file.Plugins[0].PackageId.Should().Be("DrMoustache.Sample.Plugin");
        file.Plugins[0].Version.Should().Be("1.0.0");
        file.Plugins[1].Version.Should().Be("2.1.3");
    }

    [Fact]
    public void ReadsSourceTypes()
    {
        var file = DrMoustacheFile.Parse(Sample);
        file.Sources.Should().ContainKeys("dlq", "rows");
        file.Sources["dlq"].Type.Should().Be("servicebus-dlq");
        file.Sources["rows"].Type.Should().Be("sqlite");
    }

    [Fact]
    public void ScopeConfigDefaultsToFalse()
    {
        var file = DrMoustacheFile.Parse(Sample);
        file.Sources["dlq"].ScopeConfig.Should().BeTrue();
        file.Sources["rows"].ScopeConfig.Should().BeFalse();
    }

    [Fact]
    public void FreeFormSettingsArePreservedAndReservedKeysAreNot()
    {
        var file = DrMoustacheFile.Parse(Sample);
        file.Sources["dlq"].Settings.Should().ContainKey("queue")
            .WhoseValue.Should().Be("orders");
        file.Sources["dlq"].Settings.Should().NotContainKey("type");
        file.Sources["dlq"].Settings.Should().NotContainKey("scopeConfig");
    }

    [Fact]
    public void VariableSyntaxIsPreservedVerbatimForLaterExpansion()
    {
        var file = DrMoustacheFile.Parse("""
            sources:
              dlq:
                type: x
                connection: ${SB_CONNECTION}
            """);

        file.Sources["dlq"].Settings["connection"].Should().Be("${SB_CONNECTION}");
    }

    [Fact]
    public void AnEmptyFileYieldsNoPluginsAndNoSources()
    {
        var file = DrMoustacheFile.Parse("");
        file.Plugins.Should().BeEmpty();
        file.Sources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("NoVersion")]
    [InlineData("Too@Many@Ats")]
    [InlineData("@1.0.0")]
    [InlineData("Package@")]
    public void MalformedPluginReferencesAreRejected(string spec)
    {
        var act = () => PluginReference.Parse(spec);
        act.Should().Throw<ConfigurationException>()
            .WithMessage("*PackageId@Version*");
    }

    [Fact]
    public void ASourceWithoutATypeIsRejected()
    {
        var act = () => DrMoustacheFile.Parse("""
            sources:
              dlq:
                queue: orders
            """);

        act.Should().Throw<ConfigurationException>()
            .WithMessage("*dlq*type*");
    }

    [Fact]
    public void MalformedYamlIsReportedWithContext()
    {
        var act = () => DrMoustacheFile.Parse("plugins:\n  - [unclosed\n");
        act.Should().Throw<ConfigurationException>()
            .WithMessage("*drmoustache.yaml*");
    }

    [Fact]
    public void LoadReportsAMissingFileByPath()
    {
        var act = () => DrMoustacheFile.Load("/nonexistent/drmoustache.yaml");
        act.Should().Throw<ConfigurationException>()
            .WithMessage("*/nonexistent/drmoustache.yaml*");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DrMoustacheFileTests`
Expected: compilation failure — the types do not exist.

- [ ] **Step 3: Write the model**

`src/DrMoustache.Cli/Configuration/DrMoustacheFile.cs`:

```csharp
namespace DrMoustache.Cli.Configuration;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

/// <summary>A fault in drmoustache.yaml or the configuration derived from it.</summary>
internal sealed class ConfigurationException : Exception
{
    public ConfigurationException() { }

    public ConfigurationException(string message) : base(message) { }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <param name="PackageId">NuGet package id, e.g. <c>DrMoustache.ServiceBus</c>.</param>
/// <param name="Version">Exact version, e.g. <c>1.0.0</c>.</param>
internal sealed record PluginReference(string PackageId, string Version)
{
    /// <summary>Parses the <c>Id@Version</c> form used in drmoustache.yaml.</summary>
    internal static PluginReference Parse(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var parts = spec.Split('@');

        if (parts.Length != 2
            || string.IsNullOrWhiteSpace(parts[0])
            || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ConfigurationException(
                $"plugin reference '{spec}' is not valid; write PackageId@Version, "
                + "for example DrMoustache.ServiceBus@1.0.0");
        }

        return new PluginReference(parts[0].Trim(), parts[1].Trim());
    }
}

/// <param name="Type">Matched against <c>IDataProvider.Name</c>.</param>
/// <param name="ScopeConfig">When true the provider sees only its own
/// section rather than the full configuration chain.</param>
/// <param name="Settings">Every key except the reserved <c>type</c> and
/// <c>scopeConfig</c>, passed through to the provider verbatim.</param>
internal sealed record SourceDefinition(
    string Type,
    bool ScopeConfig,
    IReadOnlyDictionary<string, string?> Settings);

internal sealed record DrMoustacheFile(
    IReadOnlyList<PluginReference> Plugins,
    IReadOnlyDictionary<string, SourceDefinition> Sources)
{
    private const string TypeKey = "type";
    private const string ScopeConfigKey = "scopeConfig";

    internal static DrMoustacheFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
            throw new ConfigurationException($"configuration file not found: {path}");

        return Parse(File.ReadAllText(path));
    }

    internal static DrMoustacheFile Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        if (string.IsNullOrWhiteSpace(yaml))
            return new DrMoustacheFile([], new Dictionary<string, SourceDefinition>());

        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new ConfigurationException(
                $"drmoustache.yaml could not be read: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
            return new DrMoustacheFile([], new Dictionary<string, SourceDefinition>());

        var root = stream.Documents[0].RootNode as YamlMappingNode
            ?? throw new ConfigurationException(
                "drmoustache.yaml must contain a mapping at its top level");

        return new DrMoustacheFile(ReadPlugins(root), ReadSources(root));
    }

    private static List<PluginReference> ReadPlugins(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("plugins"), out var node))
            return [];

        if (node is not YamlSequenceNode sequence)
            throw new ConfigurationException("'plugins' must be a list of PackageId@Version");

        return [.. sequence.Children
            .OfType<YamlScalarNode>()
            .Select(s => PluginReference.Parse(s.Value ?? ""))];
    }

    private static Dictionary<string, SourceDefinition> ReadSources(YamlMappingNode root)
    {
        var sources = new Dictionary<string, SourceDefinition>(StringComparer.Ordinal);

        if (!root.Children.TryGetValue(new YamlScalarNode("sources"), out var node))
            return sources;

        if (node is not YamlMappingNode mapping)
            throw new ConfigurationException("'sources' must be a mapping of name to definition");

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var name = ((YamlScalarNode)keyNode).Value ?? "";

            if (valueNode is not YamlMappingNode definition)
                throw new ConfigurationException($"source '{name}' must be a mapping");

            sources[name] = ReadSource(name, definition);
        }

        return sources;
    }

    private static SourceDefinition ReadSource(string name, YamlMappingNode definition)
    {
        string? type = null;
        var scopeConfig = false;
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (keyNode, valueNode) in definition.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? "";
            var value = (valueNode as YamlScalarNode)?.Value;

            switch (key)
            {
                case TypeKey:
                    type = value;
                    break;
                case ScopeConfigKey:
                    scopeConfig = bool.TryParse(value, out var parsed) && parsed;
                    break;
                default:
                    settings[key] = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(type))
            throw new ConfigurationException(
                $"source '{name}' has no 'type'; it must name a provider, "
                + "for example  type: servicebus-dlq");

        return new SourceDefinition(type, scopeConfig, settings);
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~DrMoustacheFileTests`
Expected: 14 tests passed (the `[Theory]` contributes 4).

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Cli/Configuration tests/DrMoustache.Cli.Tests/Configuration
git commit -m "$(cat <<'EOF'
Add the drmoustache.yaml model

Plugins as PackageId@Version; sources with a reserved type and scopeConfig
plus free-form settings passed through to the provider. Variable syntax is
preserved verbatim -- expansion happens through the configuration chain.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 4: The sample plugin

This task comes **before** the loader deliberately: the loader needs something real to load, and a sample with a genuine transitive dependency tree is the only way to prove `AssemblyDependencyResolver` works. A dependency-free sample would pass a broken loader — which is exactly how the equivalent bug survived in Crucible.

**Files:**
- Create: `samples/DrMoustache.Sample.Plugin/DrMoustache.Sample.Plugin.csproj`
- Create: `samples/DrMoustache.Sample.Plugin/ObservationRecord.cs`
- Create: `samples/DrMoustache.Sample.Plugin/SqliteObservationProvider.cs`
- Modify: `Directory.Packages.props`
- Modify: `DrMoustache.slnx`

**Interfaces:**
- Consumes: `DrMoustache.Abstractions` — `[DrMoustacheRecord(string)]`, `[Field]` / `[Field(string)]`, `ProviderContext` (`Configuration`, `Source`, `Logger`), `DataProvider<TRecord>` (`abstract string Name`, `abstract IAsyncEnumerable<TRecord> ReadAsync(ProviderContext, CancellationToken)`).
- Produces: a plugin package whose provider `Name` is `sample-sqlite`, yielding `ObservationRecord`.

`Microsoft.Data.Sqlite` is chosen deliberately: it pulls a real transitive tree (`SQLitePCLRaw.bundle_e_sqlite3` → `SQLitePCLRaw.core`, `SQLitePCLRaw.provider.e_sqlite3`, plus **native** assets per RID). Native asset resolution is a separate `AssemblyDependencyResolver` code path from managed resolution, so this sample exercises both.

- [ ] **Step 1: Add the package version**

In `Directory.Packages.props`, add to the first `<ItemGroup>`:

```xml
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.0" />
```

- [ ] **Step 2: Create the project**

`samples/DrMoustache.Sample.Plugin/DrMoustache.Sample.Plugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <RootNamespace>DrMoustache.Sample.Plugin</RootNamespace>
    <Description>Worked example of a DrMoustache data provider. Reads rows from SQLite.</Description>
    <!-- Deliberately has a transitive dependency tree, including native
         assets, so the plugin loader's AssemblyDependencyResolver is
         exercised for real rather than assumed. -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <ProjectReference Include="../../src/DrMoustache.Abstractions/DrMoustache.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add to `DrMoustache.slnx`**

Add a new folder before `/tests/`:

```xml
  <Folder Name="/samples/">
    <Project Path="samples/DrMoustache.Sample.Plugin/DrMoustache.Sample.Plugin.csproj" />
  </Folder>
```

- [ ] **Step 4: Write the record**

`samples/DrMoustache.Sample.Plugin/ObservationRecord.cs`:

```csharp
namespace DrMoustache.Sample.Plugin;

using DrMoustache.Abstractions;

/// <summary>What this provider projects out of SQLite.</summary>
/// <remarks>Named for the business concept, not the transport. DrMoustache
/// never learns that a database was involved.</remarks>
[DrMoustacheRecord("observation")]
public sealed record ObservationRecord
{
    [Field("Station identifier")]
    public required string Station { get; init; }

    [Field("Degrees Celsius")]
    public required double Temperature { get; init; }

    [Field] public required DateTimeOffset RecordedAt { get; init; }

    [Field("Free-text note, absent for most rows")]
    public string? Note { get; init; }
}
```

- [ ] **Step 5: Write the provider**

`samples/DrMoustache.Sample.Plugin/SqliteObservationProvider.cs`:

```csharp
namespace DrMoustache.Sample.Plugin;

using System.Globalization;
using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

/// <summary>Reads observation rows from a SQLite database.</summary>
/// <remarks>Read-only by contract: this provider only ever issues SELECT.</remarks>
public sealed class SqliteObservationProvider : DataProvider<ObservationRecord>
{
    public override string Name => "sample-sqlite";

    public override async IAsyncEnumerable<ObservationRecord> ReadAsync(
        ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var path = ctx.Source["path"]
            ?? throw new InvalidOperationException(
                "source setting 'path' is required and must point at a SQLite file");

        ctx.Logger.LogInformation("Reading observations from {Path}", path);

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
                Temperature = reader.GetDouble(1),
                RecordedAt = DateTimeOffset.Parse(
                    reader.GetString(2), CultureInfo.InvariantCulture),
                Note = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
    }
}
```

- [ ] **Step 6: Verify it builds and publishes**

Run: `dotnet build DrMoustache.slnx`
Expected: 0 warnings.

Run: `dotnet publish samples/DrMoustache.Sample.Plugin -o /tmp/drm-sample-publish`

Run: `ls /tmp/drm-sample-publish/*.deps.json && ls /tmp/drm-sample-publish | grep -ci sqlite`
Expected: a `.deps.json` exists, and several SQLite assemblies are present — confirming there is a real dependency tree for Task 5's loader to resolve. Record the count in your report.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props DrMoustache.slnx samples
git commit -m "$(cat <<'EOF'
Add the sample provider plugin

Reads observation rows from SQLite. Microsoft.Data.Sqlite is deliberate:
it brings a real transitive tree including native assets, so the plugin
loader is exercised rather than assumed. A dependency-free sample would
pass a broken loader -- which is how the equivalent bug survived in
Crucible (phoenixmldb/crucible#4).

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 5: `PluginLoadContext`

**Files:**
- Create: `src/DrMoustache.Cli/Plugins/PluginPackage.cs`
- Create: `src/DrMoustache.Cli/Plugins/PluginLoadContext.cs`
- Test: `tests/DrMoustache.Cli.Tests/Plugins/PluginLoadContextTests.cs`

**Interfaces:**
- Consumes: `DrMoustache.Abstractions.IDataProvider`.
- Produces:
  - `internal sealed record PluginPackage(string PackageId, string Version, string EntryAssemblyPath)`
  - `internal sealed class PluginLoadContext : AssemblyLoadContext` — constructor `PluginLoadContext(string entryAssemblyPath)`, `IReadOnlyList<IDataProvider> CreateProviders()`, and it is `IDisposable`-adjacent via `Unload()` (inherited).
  - `internal sealed class PluginLoadException : Exception`

**The trap this task exists to avoid.** `AssemblyLoadContext.Load` must return `null` for `DrMoustache.Abstractions` so it resolves from the **default** context. If the plugin's own copy is loaded instead, `is IDataProvider` returns false — same type name, different `AssemblyLoadContext`, therefore not the same `Type` — and every provider is silently skipped with no error. This is not hypothetical; it is the failure mode that makes plugin systems appear to work and then find nothing.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Plugins/PluginLoadContextTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Plugins;

using System.Runtime.Loader;
using DrMoustache.Abstractions;
using DrMoustache.Cli.Plugins;
using FluentAssertions;
using Xunit;

/// <summary>Publishes the sample plugin once and shares the output.</summary>
public sealed class SamplePluginFixture : IDisposable
{
    public string PublishDirectory { get; }
    public string EntryAssemblyPath { get; }

    public SamplePluginFixture()
    {
        PublishDirectory = Directory.CreateTempSubdirectory("drm-sample-").FullName;

        var repoRoot = FindRepositoryRoot();
        var project = Path.Combine(
            repoRoot, "samples", "DrMoustache.Sample.Plugin",
            "DrMoustache.Sample.Plugin.csproj");

        var publish = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "publish", project, "-o", PublishDirectory, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("could not start dotnet publish");

        publish.WaitForExit();

        if (publish.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "publishing the sample plugin failed:\n"
                + publish.StandardOutput.ReadToEnd()
                + publish.StandardError.ReadToEnd());
        }

        EntryAssemblyPath = Path.Combine(
            PublishDirectory, "DrMoustache.Sample.Plugin.dll");
    }

    public void Dispose()
    {
        try { Directory.Delete(PublishDirectory, recursive: true); }
        catch (IOException) { /* best effort on a temp directory */ }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null
            && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("tests must run inside the repository");
    }
}

public class PluginLoadContextTests : IClassFixture<SamplePluginFixture>
{
    private readonly SamplePluginFixture _sample;

    public PluginLoadContextTests(SamplePluginFixture sample) => _sample = sample;

    [Fact]
    public void DiscoversProvidersInThePlugin()
    {
        using var context = new PluginLoadContext(_sample.EntryAssemblyPath);

        var providers = context.CreateProviders();

        providers.Should().ContainSingle()
            .Which.Name.Should().Be("sample-sqlite");
    }

    [Fact]
    public void ProvidersSatisfyTheHostsInterfaceIdentity()
    {
        using var context = new PluginLoadContext(_sample.EntryAssemblyPath);

        var provider = context.CreateProviders().Single();

        // If Abstractions loaded into the plugin context instead of the
        // default one, this cast target would be a different Type despite
        // the identical name, and CreateProviders would have found nothing.
        provider.Should().BeAssignableTo<IDataProvider>();
        provider.GetType().Assembly.Should().NotBeSameAs(typeof(IDataProvider).Assembly);
        typeof(IDataProvider).Assembly.Should().BeSameAs(
            AssemblyLoadContext.Default.Assemblies
                .Single(a => a.GetName().Name == "DrMoustache.Abstractions"));
    }

    [Fact]
    public void ResolvesTheTransitiveDependencyTree()
    {
        using var context = new PluginLoadContext(_sample.EntryAssemblyPath);

        var provider = context.CreateProviders().Single();
        var recordType = provider.RecordType;

        // Touching RecordType forces the plugin assembly to resolve its own
        // references. A loader without AssemblyDependencyResolver loads the
        // entry assembly fine and only fails here.
        recordType.Name.Should().Be("ObservationRecord");

        var sqlite = context.Assemblies
            .Select(a => a.GetName().Name)
            .Where(n => n is not null && n.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));

        sqlite.Should().NotBeEmpty(
            "the plugin's transitive dependencies must resolve through the context");
    }

    [Fact]
    public void AMissingEntryAssemblyIsReportedByPath()
    {
        var act = () => new PluginLoadContext("/nonexistent/Plugin.dll");
        act.Should().Throw<PluginLoadException>()
            .WithMessage("*/nonexistent/Plugin.dll*");
    }

    [Fact]
    public void AnAssemblyWithNoProvidersIsReportedClearly()
    {
        // DrMoustache.Abstractions itself declares no concrete provider.
        var abstractions = typeof(IDataProvider).Assembly.Location;

        using var context = new PluginLoadContext(abstractions);

        var act = () => context.CreateProviders();
        act.Should().Throw<PluginLoadException>()
            .WithMessage("*no*IDataProvider*");
    }

    [Fact]
    public void TheContextIsCollectible()
    {
        using var context = new PluginLoadContext(_sample.EntryAssemblyPath);
        context.IsCollectible.Should().BeTrue(
            "a future watch mode must be able to unload and reload a rebuilt plugin");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~PluginLoadContextTests`
Expected: compilation failure — `PluginLoadContext` does not exist.

- [ ] **Step 3: Write `PluginPackage`**

`src/DrMoustache.Cli/Plugins/PluginPackage.cs`:

```csharp
namespace DrMoustache.Cli.Plugins;

/// <param name="EntryAssemblyPath">The plugin's own assembly inside a
/// published output folder. Its sibling .deps.json drives dependency
/// resolution.</param>
internal sealed record PluginPackage(string PackageId, string Version, string EntryAssemblyPath);
```

- [ ] **Step 4: Write `PluginLoadContext`**

`src/DrMoustache.Cli/Plugins/PluginLoadContext.cs`:

```csharp
namespace DrMoustache.Cli.Plugins;

using System.Reflection;
using System.Runtime.Loader;
using DrMoustache.Abstractions;

/// <summary>A plugin failed to load or contained nothing loadable.</summary>
internal sealed class PluginLoadException : Exception
{
    public PluginLoadException() { }

    public PluginLoadException(string message) : base(message) { }

    public PluginLoadException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Loads one plugin and its transitive dependencies in isolation.</summary>
/// <remarks>
/// <para>Collectible so a future watch mode can unload and reload a rebuilt
/// plugin without restarting the process.</para>
/// <para><see cref="DrMoustache.Abstractions"/> is deliberately resolved from
/// the default context. If the plugin's own copy loaded here instead, the
/// host's <see cref="IDataProvider"/> and the plugin's would be different
/// <see cref="Type"/>s despite identical names, and every provider would be
/// silently skipped.</para>
/// </remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext, IDisposable
{
    private static readonly string SharedContract =
        typeof(IDataProvider).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _entryAssemblyPath;

    internal PluginLoadContext(string entryAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(entryAssemblyPath), isCollectible: true)
    {
        ArgumentNullException.ThrowIfNull(entryAssemblyPath);

        if (!File.Exists(entryAssemblyPath))
            throw new PluginLoadException($"plugin assembly not found: {entryAssemblyPath}");

        _entryAssemblyPath = entryAssemblyPath;
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning null defers to the default context, which is required
        // for the shared contract and harmless for framework assemblies.
        if (string.Equals(assemblyName.Name, SharedContract, StringComparison.Ordinal))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>Instantiates every concrete <see cref="IDataProvider"/> the
    /// plugin exports.</summary>
    internal IReadOnlyList<IDataProvider> CreateProviders()
    {
        Assembly assembly;

        try
        {
            assembly = LoadFromAssemblyPath(_entryAssemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
        {
            throw new PluginLoadException(
                $"'{_entryAssemblyPath}' could not be loaded as a .NET assembly", ex);
        }

        var providerTypes = assembly.GetExportedTypes()
            .Where(t => typeof(IDataProvider).IsAssignableFrom(t)
                && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (providerTypes.Count == 0)
        {
            throw new PluginLoadException(
                $"'{Path.GetFileName(_entryAssemblyPath)}' exports no IDataProvider. "
                + "A provider must be a public, non-abstract class deriving from "
                + "DataProvider<TRecord>.");
        }

        var providers = new List<IDataProvider>(providerTypes.Count);

        foreach (var type in providerTypes)
        {
            try
            {
                providers.Add((IDataProvider)Activator.CreateInstance(type)!);
            }
            catch (MissingMethodException ex)
            {
                throw new PluginLoadException(
                    $"provider '{type.FullName}' has no public parameterless constructor", ex);
            }
            catch (TargetInvocationException ex)
            {
                throw new PluginLoadException(
                    $"provider '{type.FullName}' threw while being constructed: "
                    + $"{ex.InnerException?.Message}", ex);
            }
        }

        return providers;
    }

    public void Dispose() => Unload();
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~PluginLoadContextTests`
Expected: 6 tests passed.

`ResolvesTheTransitiveDependencyTree` is the one that matters. If it fails with `FileNotFoundException` naming a SQLite assembly, `AssemblyDependencyResolver` is not wired correctly. If `ProvidersSatisfyTheHostsInterfaceIdentity` fails, the shared-contract deferral is wrong — and note that `DiscoversProvidersInThePlugin` would *also* fail in that case, by finding zero providers rather than by throwing.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Cli/Plugins tests/DrMoustache.Cli.Tests/Plugins
git commit -m "$(cat <<'EOF'
Add the plugin load context

AssemblyDependencyResolver over the plugin's .deps.json resolves both
managed and native dependencies. DrMoustache.Abstractions is deferred to
the default context: without that, the host's IDataProvider and the
plugin's are different Types despite identical names, and every provider
is silently skipped.

Proven against the sample plugin's real dependency tree, not asserted.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 6: Plugin acquisition

**Files:**
- Create: `src/DrMoustache.Cli/Plugins/PluginAcquisition.cs`
- Test: `tests/DrMoustache.Cli.Tests/Plugins/PluginAcquisitionTests.cs`

**Interfaces:**
- Consumes: `PluginReference` (Task 3), `PluginPackage` (Task 5), `ConfigurationException` (Task 3).
- Produces: `internal sealed class PluginAcquisition` with constructor `PluginAcquisition(string cacheRoot)` and `Task<PluginPackage> AcquireAsync(PluginReference reference, CancellationToken ct)`.

**How it works.** For `Id@Version`, the cache directory is `<cacheRoot>/<Id>/<Version>/`. If it already contains `<Id>.dll`, that is returned immediately. Otherwise a throwaway project is written to a temp directory:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="<Id>" Version="<Version>" />
  </ItemGroup>
</Project>
```

`dotnet publish` runs on it with output into the cache directory. `ManagePackageVersionsCentrally` is explicitly disabled so the generated project is not affected by a `Directory.Packages.props` further up the filesystem — a real hazard when the tool runs inside a repository that uses central package management.

Default cache root: `<user local app data>/drmoustache/plugins`.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Plugins/PluginAcquisitionTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Plugins;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Plugins;
using FluentAssertions;
using Xunit;

public class PluginAcquisitionTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("drm-cache-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AcquiresAPackageFromNuGet()
    {
        var acquisition = new PluginAcquisition(_cache);

        // Newtonsoft.Json is used only as a stand-in for "some real package
        // that exists on nuget.org" -- this test proves acquisition works,
        // not that the package is a provider.
        var package = await acquisition.AcquireAsync(
            new PluginReference("Newtonsoft.Json", "13.0.3"),
            TestContext.Current.CancellationToken);

        File.Exists(package.EntryAssemblyPath).Should().BeTrue();
        Path.GetFileName(package.EntryAssemblyPath).Should().Be("Newtonsoft.Json.dll");
        File.Exists(Path.ChangeExtension(package.EntryAssemblyPath, ".deps.json"))
            .Should().BeTrue("AssemblyDependencyResolver needs the deps file");
    }

    [Fact]
    public async Task ASecondAcquisitionIsServedFromCache()
    {
        var acquisition = new PluginAcquisition(_cache);
        var reference = new PluginReference("Newtonsoft.Json", "13.0.3");

        var first = await acquisition.AcquireAsync(
            reference, TestContext.Current.CancellationToken);
        var firstWrite = File.GetLastWriteTimeUtc(first.EntryAssemblyPath);

        var second = await acquisition.AcquireAsync(
            reference, TestContext.Current.CancellationToken);

        second.EntryAssemblyPath.Should().Be(first.EntryAssemblyPath);
        File.GetLastWriteTimeUtc(second.EntryAssemblyPath).Should().Be(firstWrite,
            "a cached package must not be re-published");
    }

    [Fact]
    public async Task VersionsAreCachedSeparately()
    {
        var acquisition = new PluginAcquisition(_cache);

        var a = await acquisition.AcquireAsync(
            new PluginReference("Newtonsoft.Json", "13.0.3"),
            TestContext.Current.CancellationToken);
        var b = await acquisition.AcquireAsync(
            new PluginReference("Newtonsoft.Json", "13.0.2"),
            TestContext.Current.CancellationToken);

        a.EntryAssemblyPath.Should().NotBe(b.EntryAssemblyPath);
    }

    [Fact]
    public async Task AnUnknownPackageFailsWithTheNuGetError()
    {
        var acquisition = new PluginAcquisition(_cache);

        var act = async () => await acquisition.AcquireAsync(
            new PluginReference("DrMoustache.NoSuchPackage.Ever", "9.9.9"),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<PluginLoadException>())
            .WithMessage("*DrMoustache.NoSuchPackage.Ever*");
    }
}
```

These tests reach nuget.org. If the environment has no network, they will fail — report that rather than deleting them; the acquisition path cannot be proven any other way.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~PluginAcquisitionTests`
Expected: compilation failure — `PluginAcquisition` does not exist.

- [ ] **Step 3: Write `PluginAcquisition`**

`src/DrMoustache.Cli/Plugins/PluginAcquisition.cs`:

```csharp
namespace DrMoustache.Cli.Plugins;

using System.Diagnostics;
using System.Globalization;
using DrMoustache.Cli.Configuration;

/// <summary>Resolves a plugin package to a published folder on disk.</summary>
/// <remarks>Acquisition generates a throwaway project and runs
/// <c>dotnet publish</c> on it, rather than using the NuGet client
/// libraries. The SDK already resolves the dependency graph, selects
/// target-framework assets, and lays out native runtime assets correctly —
/// and it emits the .deps.json the plugin loader needs. The cost is a
/// dependency on the .NET SDK at run time, which anyone who installed this
/// tool already has.</remarks>
internal sealed class PluginAcquisition(string cacheRoot)
{
    private readonly string _cacheRoot = cacheRoot
        ?? throw new ArgumentNullException(nameof(cacheRoot));

    internal static string DefaultCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "drmoustache", "plugins");

    internal async Task<PluginPackage> AcquireAsync(
        PluginReference reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var target = Path.Combine(_cacheRoot, reference.PackageId, reference.Version);
        var entry = Path.Combine(target, reference.PackageId + ".dll");

        if (File.Exists(entry))
            return new PluginPackage(reference.PackageId, reference.Version, entry);

        await PublishAsync(reference, target, ct).ConfigureAwait(false);

        if (!File.Exists(entry))
        {
            throw new PluginLoadException(
                $"package '{reference.PackageId}@{reference.Version}' was restored but "
                + $"contains no assembly named '{reference.PackageId}.dll'. "
                + "A DrMoustache plugin package's assembly name must match its package id.");
        }

        return new PluginPackage(reference.PackageId, reference.Version, entry);
    }

    private static async Task PublishAsync(
        PluginReference reference, string target, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("drm-acquire-");

        try
        {
            var project = Path.Combine(work.FullName, "acquire.csproj");

            // ManagePackageVersionsCentrally is disabled explicitly: without
            // it, a Directory.Packages.props anywhere above the temp folder
            // would reject this project's inline Version attribute.
            await File.WriteAllTextAsync(project, string.Format(
                CultureInfo.InvariantCulture,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{0}" Version="{1}" />
                  </ItemGroup>
                </Project>
                """,
                reference.PackageId, reference.Version), ct).ConfigureAwait(false);

            Directory.CreateDirectory(target);

            var (exitCode, output) = await RunDotnetAsync(
                ["publish", project, "-o", target, "--nologo"], ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new PluginLoadException(
                    $"could not acquire '{reference.PackageId}@{reference.Version}':\n{output}");
            }
        }
        finally
        {
            try { work.Delete(recursive: true); }
            catch (IOException) { /* best effort on a temp directory */ }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string[] arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        Process process;

        try
        {
            process = Process.Start(info)
                ?? throw new PluginLoadException("could not start 'dotnet'");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new PluginLoadException(
                "'dotnet' was not found on PATH. Plugin acquisition needs the .NET SDK; "
                + "install it from https://dotnet.microsoft.com/download", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return (process.ExitCode,
                await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        }
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~PluginAcquisitionTests`
Expected: 4 tests passed. These reach the network and take longer than the rest of the suite.

- [ ] **Step 5: Commit**

```bash
git add src/DrMoustache.Cli/Plugins tests/DrMoustache.Cli.Tests/Plugins
git commit -m "$(cat <<'EOF'
Add plugin acquisition

Generates a throwaway project and runs dotnet publish, rather than using
the NuGet client libraries: the SDK already resolves the graph, selects
framework assets, lays out native assets, and emits the .deps.json the
loader needs. Central package management is disabled on the generated
project so a Directory.Packages.props above the temp folder cannot reject it.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

### Task 7: The provider registry

**Files:**
- Create: `src/DrMoustache.Cli/Plugins/ProviderRegistry.cs`
- Test: `tests/DrMoustache.Cli.Tests/Plugins/ProviderRegistryTests.cs`

**Interfaces:**
- Consumes: `DrMoustacheFile`, `SourceDefinition`, `PluginReference`, `ConfigurationException` (Task 3); `PluginLoadContext`, `PluginLoadException` (Task 5); `PluginAcquisition` (Task 6); `IDataProvider`, `ProviderContext` (Abstractions).
- Produces:
  - `internal sealed record ResolvedSource(string Name, IDataProvider Provider, ProviderContext Context)`
  - `internal sealed class ProviderRegistry : IDisposable` with `static Task<ProviderRegistry> LoadAsync(DrMoustacheFile file, IConfiguration configuration, ILoggerFactory loggers, PluginAcquisition acquisition, CancellationToken ct)` and `ResolvedSource Resolve(string sourceName)`

This is where `scopeConfig` is honoured: `ProviderContext.Configuration` receives the full chain by default, or only the source's own section when `scopeConfig: true`. `ProviderContext.Source` is always the source's own section regardless.

Source settings from `drmoustache.yaml` are layered into the configuration under `sources:<name>:<key>` so that `${VAR}`-style values and environment overrides resolve through the normal chain rather than through bespoke expansion code.

- [ ] **Step 1: Write the failing test**

`tests/DrMoustache.Cli.Tests/Plugins/ProviderRegistryTests.cs`:

```csharp
namespace DrMoustache.Cli.Tests.Plugins;

using DrMoustache.Cli.Configuration;
using DrMoustache.Cli.Plugins;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ProviderRegistryTests : IClassFixture<SamplePluginFixture>, IDisposable
{
    private readonly SamplePluginFixture _sample;
    private readonly string _cache = Directory.CreateTempSubdirectory("drm-reg-").FullName;

    public ProviderRegistryTests(SamplePluginFixture sample)
    {
        _sample = sample;

        // Seed the cache so LoadAsync serves from it instead of reaching
        // the network: <cache>/<Id>/<Version>/<Id>.dll.
        var target = Path.Combine(_cache, "DrMoustache.Sample.Plugin", "1.0.0");
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(_sample.PublishDirectory))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string Yaml = """
        plugins:
          - DrMoustache.Sample.Plugin@1.0.0

        sources:
          obs:
            type: sample-sqlite
            path: ./weather.db
          scoped:
            type: sample-sqlite
            scopeConfig: true
            path: ./other.db
        """;

    private async Task<ProviderRegistry> LoadAsync(IConfiguration? configuration = null)
        => await ProviderRegistry.LoadAsync(
            DrMoustacheFile.Parse(Yaml),
            configuration ?? new ConfigurationBuilder().Build(),
            NullLoggerFactory.Instance,
            new PluginAcquisition(_cache),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ResolvesASourceToItsProvider()
    {
        using var registry = await LoadAsync();

        registry.Resolve("obs").Provider.Name.Should().Be("sample-sqlite");
    }

    [Fact]
    public async Task SourceSettingsReachTheProvidersOwnSection()
    {
        using var registry = await LoadAsync();

        registry.Resolve("obs").Context.Source["path"].Should().Be("./weather.db");
    }

    [Fact]
    public async Task TheFullChainIsVisibleByDefault()
    {
        var host = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["unrelated:secret"] = "visible",
            })
            .Build();

        using var registry = await LoadAsync(host);

        registry.Resolve("obs").Context.Configuration["unrelated:secret"]
            .Should().Be("visible");
    }

    [Fact]
    public async Task ScopeConfigHidesEverythingButTheSourcesOwnSection()
    {
        var host = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["unrelated:secret"] = "hidden",
            })
            .Build();

        using var registry = await LoadAsync(host);
        var scoped = registry.Resolve("scoped");

        scoped.Context.Configuration["unrelated:secret"].Should().BeNull();
        scoped.Context.Configuration["path"].Should().Be("./other.db");
        scoped.Context.Source["path"].Should().Be("./other.db",
            "Source is always the source's own section regardless of scopeConfig");
    }

    [Fact]
    public async Task AnUnknownSourceNameIsReportedWithTheAvailableOnes()
    {
        using var registry = await LoadAsync();

        var act = () => registry.Resolve("nope");
        act.Should().Throw<ConfigurationException>()
            .WithMessage("*nope*")
            .And.Message.Should().Contain("obs");
    }

    [Fact]
    public async Task ASourceNamingAnUnloadedProviderTypeIsReported()
    {
        var yaml = """
            plugins:
              - DrMoustache.Sample.Plugin@1.0.0
            sources:
              obs:
                type: no-such-provider
            """;

        var act = async () => await ProviderRegistry.LoadAsync(
            DrMoustacheFile.Parse(yaml),
            new ConfigurationBuilder().Build(),
            NullLoggerFactory.Instance,
            new PluginAcquisition(_cache),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ConfigurationException>())
            .WithMessage("*no-such-provider*")
            .And.Message.Should().Contain("sample-sqlite");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ProviderRegistryTests`
Expected: compilation failure — `ProviderRegistry` does not exist.

- [ ] **Step 3: Write `ProviderRegistry`**

`src/DrMoustache.Cli/Plugins/ProviderRegistry.cs`:

```csharp
namespace DrMoustache.Cli.Plugins;

using DrMoustache.Abstractions;
using DrMoustache.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <param name="Context">Built per source, honouring <c>scopeConfig</c>.</param>
internal sealed record ResolvedSource(
    string Name, IDataProvider Provider, ProviderContext Context);

/// <summary>Acquires and loads the configured plugins, then maps each
/// configured source onto the provider that serves it.</summary>
internal sealed class ProviderRegistry : IDisposable
{
    private readonly List<PluginLoadContext> _contexts;
    private readonly Dictionary<string, ResolvedSource> _sources;

    private ProviderRegistry(
        List<PluginLoadContext> contexts, Dictionary<string, ResolvedSource> sources)
    {
        _contexts = contexts;
        _sources = sources;
    }

    internal static async Task<ProviderRegistry> LoadAsync(
        DrMoustacheFile file,
        IConfiguration configuration,
        ILoggerFactory loggers,
        PluginAcquisition acquisition,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(acquisition);

        var contexts = new List<PluginLoadContext>();
        var providers = new Dictionary<string, IDataProvider>(StringComparer.Ordinal);

        foreach (var reference in file.Plugins)
        {
            var package = await acquisition.AcquireAsync(reference, ct).ConfigureAwait(false);
            var context = new PluginLoadContext(package.EntryAssemblyPath);
            contexts.Add(context);

            foreach (var provider in context.CreateProviders())
            {
                if (providers.TryGetValue(provider.Name, out var existing))
                {
                    throw new ConfigurationException(
                        $"two plugins both provide '{provider.Name}': "
                        + $"'{existing.GetType().FullName}' and '{provider.GetType().FullName}'. "
                        + "Provider names must be unique across all loaded plugins.");
                }

                providers[provider.Name] = provider;
            }
        }

        var effective = Layer(configuration, file);
        var sources = new Dictionary<string, ResolvedSource>(StringComparer.Ordinal);

        foreach (var (name, definition) in file.Sources)
        {
            if (!providers.TryGetValue(definition.Type, out var provider))
            {
                var available = providers.Count == 0
                    ? "none — no plugin exported a provider"
                    : string.Join(", ", providers.Keys.Order());

                throw new ConfigurationException(
                    $"source '{name}' names provider type '{definition.Type}', "
                    + $"which no loaded plugin exports. Available: {available}");
            }

            var section = effective.GetSection($"sources:{name}");

            sources[name] = new ResolvedSource(name, provider, new ProviderContext
            {
                Configuration = definition.ScopeConfig ? section : effective,
                Source = section,
                Logger = loggers.CreateLogger(provider.GetType()),
            });
        }

        return new ProviderRegistry(contexts, sources);
    }

    /// <summary>Layers the file's source settings beneath the host chain, so
    /// environment and command line still win and no bespoke variable
    /// expansion is needed.</summary>
    private static IConfigurationRoot Layer(IConfiguration host, DrMoustacheFile file)
    {
        var fromFile = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, definition) in file.Sources)
        {
            fromFile[$"sources:{name}:type"] = definition.Type;

            foreach (var (key, value) in definition.Settings)
                fromFile[$"sources:{name}:{key}"] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(fromFile)
            .AddConfiguration(host)
            .Build();
    }

    internal ResolvedSource Resolve(string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        if (_sources.TryGetValue(sourceName, out var resolved))
            return resolved;

        var available = _sources.Count == 0
            ? "none are configured"
            : string.Join(", ", _sources.Keys.Order());

        throw new ConfigurationException(
            $"no source named '{sourceName}' is configured. Available: {available}");
    }

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
        _sources.Clear();
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test DrMoustache.slnx --filter FullyQualifiedName~ProviderRegistryTests`
Expected: 6 tests passed.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test DrMoustache.slnx`
Expected: all Core tests (431) plus the CLI tests pass. Build with 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/DrMoustache.Cli/Plugins tests/DrMoustache.Cli.Tests/Plugins
git commit -m "$(cat <<'EOF'
Add the provider registry

Maps configured sources onto loaded providers and builds each
ProviderContext. scopeConfig narrows Configuration to the source's own
section; Source is always scoped regardless.

Source settings are layered beneath the host chain rather than expanded by
hand, so environment and command line still win and ${VAR} resolves through
normal configuration.

Built by Endpoint Systems https://endpointsystems.com
Claude-Session: https://claude.ai/code/session_017g5BxzAb33wVKHrMr7UPdb
EOF
)"
```

---

**Part 2** — `docs/superpowers/plans/2026-08-02-drmoustache-cli-part2.md` — covers:

- **Task 8:** Atomic document writing (never a partial file on disk)
- **Task 9:** The `render` verb end to end
- **Task 10:** Record-level error collection, `--strict`, and the summary report
- **Task 11:** The `check` verb
- **Task 12:** The `init` verb
- **Task 13:** `docs/plugin-architecture.md` — the .NET 10 teaching artifact
- **Task 14:** README, package metadata, and CI
