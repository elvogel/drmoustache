# DrMoustache v1 — Product Design

**Date:** 2026-08-04
**Status:** Approved for planning
**Supersedes:** the delivery-model sections of
[`2026-08-01-drmoustache-design.md`](2026-08-01-drmoustache-design.md) — specifically
the CLI, plugin distribution, plugin loading, and configuration-chain sections. That
document remains authoritative for the rendering engine: Markdown-aware escaping,
the template format, delivery modes, collision policies, helpers, and the manifest.

## What this is for

> **Rapid answers to data questions, flexible enough for any data set.**

Every decision below is tested against that sentence. Two things follow from it
immediately, and they are why this document exists:

- **Rapid** rules out a delivery model whose first step is publishing a NuGet
  package. It has to be runnable within a minute of deciding to use it.
- **Any data set** rules out a fixed catalogue of built-in data sources. The user
  brings the reader; DrMoustache brings the rendering.

## The change from the original design

The original design shipped DrMoustache as a global `dotnet tool` that loaded data
providers as NuGet plugins through `AssemblyLoadContext`. That model has a first run
which reads, in full:

```
write a provider in a separate project
pack it into a .nupkg
publish it, or stand up a local feed
drmoustache init
edit drmoustache.yaml to point at the package
drmoustache render
```

Publishing a package in order to look at a queue once fails *rapid* badly. The tool
model pays off only when a provider is written once and consumed by many — a
mature-ecosystem scenario, not a first-release one.

**v1 ships as a project template instead.** The user scaffolds a small console
project, writes a provider class in it, and runs it:

```
dotnet new drmoustache -n dlq-peek
cd dlq-peek
# write your provider
dotnet run
# look at dist/
rm -rf dlq-peek
```

A scaffolded project is a deletable folder, which matches how these reports are
actually used.

### What this deletes rather than solves

Collapsing the plugin boundary removes an entire category of design, not just some
code:

| Removed | Why it existed |
|---|---|
| `AssemblyLoadContext`, `AssemblyDependencyResolver`, the shared-contract trap | A provider arriving at runtime as a stranger |
| NuGet acquisition, a plugin cache, `dotnet publish` orchestration | Getting that stranger onto disk |
| `drmoustache.yaml` | Naming the stranger and its settings |
| The hand-built `IConfiguration` chain | Bridging settings across the boundary |
| `scopeConfig`, and the documented "a plugin can read every secret the host can" | A stranger being able to read the host's configuration |
| `ProviderContext` | Handing a stranger its configuration, section, and logger |

None of it was inherent to templating. When the provider is a class in the user's own
project, `new MyProvider(...)` — or rather, dependency injection — replaces all of it.

## Packages

Four packages, all at `0.1.0`.

| Package | Contents | Who references it |
|---|---|---|
| `DrMoustache.Abstractions` | Provider contract: `IDataProvider`, `DataProvider<TRecord>`, `[DrMoustacheRecord]`, `[Field]` | Anyone writing a provider |
| `DrMoustache.Core` | The rendering engine. Performs no I/O. | Transitively |
| `DrMoustache` | **Metapackage.** DI extensions, template discovery, orchestration, file writing. Depends on the other two. | What users actually reference |
| `DrMoustache.Templates` | `dotnet new` template package | `dotnet new install` |

`DrMoustache` is the metapackage deliberately: someone searching NuGet for
"DrMoustache" should find the thing that works, not a contract they cannot run.

## The provider contract

**Breaking change from the original design, taken now because nothing is published.**
`ProviderContext` is deleted and `ReadAsync` loses its parameter:

```csharp
namespace DrMoustache.Abstractions;

public interface IDataProvider
{
    string Name { get; }
    Type RecordType { get; }
    IAsyncEnumerable<object> ReadAsync(CancellationToken ct);
}

public abstract class DataProvider<TRecord> : IDataProvider
    where TRecord : class
{
    public abstract string Name { get; }
    public Type RecordType => typeof(TRecord);
    public abstract IAsyncEnumerable<TRecord> ReadAsync(CancellationToken ct);
}
```

`ProviderContext` supplied exactly three things — an `IConfiguration`, that source's
section, and an `ILogger`. Under dependency injection all three arrive the normal
way, and so does everything else:

```csharp
public sealed class DeadLetterProvider(
    ServiceBusClient bus,
    IOptions<DeadLetterOptions> options,
    ILogger<DeadLetterProvider> logger)
    : DataProvider<FailedOrder>
{
    public override async IAsyncEnumerable<FailedOrder> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct) { … }
}
```

The contract got smaller while getting more capable: it stopped trying to be a
service locator, so a provider can now take an `HttpClient` from
`IHttpClientFactory`, a `DbContext`, a resilience pipeline, or anything else in the
container.

The read-only property is unchanged and remains load-bearing: `IDataProvider`
exposes no write, delete, or acknowledge surface. A plausible early use is browsing a
dead-letter queue, where a destructive read is unrecoverable.

**One cost, stated plainly:** providers are no longer constructible with `new`, so a
unit test for a provider builds it with its dependencies rather than with a
`ProviderContext`. That is normal for a DI-registered service.

## Registration and the host API

Registration follows the `AddX().AddY()` builder pattern that `AddAuthentication`
and `AddLogging` established.

```csharp
public static class DrMoustacheServiceCollectionExtensions
{
    public static IDrMoustacheBuilder AddDrMoustache(
        this IServiceCollection services,
        Action<DrMoustacheOptions>? configure = null);
}

public interface IDrMoustacheBuilder
{
    IServiceCollection Services { get; }

    IDrMoustacheBuilder AddSource<TProvider>(string name)
        where TProvider : class, IDataProvider;
}
```

`AddSource<TProvider>(name)` does two things:

1. Registers the provider as a **keyed** singleton under `name`, which is what a
   template's `source:` key resolves against.
2. Registers a `SourceRegistration(string Name, Type ProviderType)` singleton.

The second exists because keyed DI is write-only for discovery — it cannot enumerate
its own keys. Without it, a template naming an unregistered source produces a bare
lookup failure instead of *"no source named 'orders' — registered: 'dlq',
'stations'"*. This is deliberate, not redundant.

### Options

A plain class, so it binds from `appsettings.json` with no extra work:

```csharp
public sealed class DrMoustacheOptions
{
    public string TemplateDirectory { get; set; } = "templates";
    public string OutputDirectory { get; set; } = "dist";

    /// <summary>Treat any skipped record as a failure.</summary>
    public bool Strict { get; set; }

    /// <summary>Null means invariant, so output is byte-identical
    /// across machines. Set it only to localise deliberately.</summary>
    public CultureInfo? Culture { get; set; }
}
```

### Rendering and checking

```csharp
public interface IDocumentRenderer
{
    Task<RenderResult> RenderAsync(CancellationToken ct = default);

    /// <summary>Validates every template against its source's manifest
    /// without reading any data or writing any output.</summary>
    Task<CheckResult> CheckAsync(CancellationToken ct = default);
}

public sealed record RenderResult(
    int DocumentsWritten,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecordSkip> Skips,
    IReadOnlyList<string> Faults,
    bool Success)
{
    public string Summary { get; }
}

public sealed record CheckResult(
    int TemplatesChecked,
    IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;
    public string Summary { get; }
}

/// <param name="Source">Which source the record came from. One project can
/// render several sources in one pass.</param>
/// <param name="Ordinal">Zero-based position in the provider's sequence — the
/// only stable identifier available before a record is bound.</param>
public sealed record RecordSkip(string Source, int Ordinal, string Reason);
```

`Success` lives on the result rather than being re-derived by the caller, because it
depends on `Strict`. A caller who must remember to compare `Skips.Count` against an
option set elsewhere will eventually forget.

A **skip** and a **fault** are different events. A skip is one record that could not
be used; the read continued, so what was lost is known exactly, and `Strict` decides
whether that is fatal. A fault is a read that died — the database went away, the
credentials expired — and how many records were never seen is unknown. `Success` is
therefore false whenever `Faults` is non-empty, whatever `Strict` says: a scheduled
job whose provider could not connect must not go green having produced nothing.

`RenderAsync` runs the same validation `CheckAsync` does, before touching any
provider. Template faults are static and affect every record, so they must fail
before any data is read.

### The scaffolded entry point, in full

```csharp
using DrMoustache;
using DrMoustache.Discovery;
using DrMoustache.Output;
using DrMoustache.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDrMoustache(o => o.OutputDirectory = "dist")
    .AddSource<SampleProvider>("sample");

using var host = builder.Build();

var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

try
{
    if (args.Contains("--check"))
    {
        var check = await renderer.CheckAsync();
        Console.Write(check.Summary);
        return check.Success ? 0 : 1;
    }

    var result = await renderer.RenderAsync();
    Console.Write(result.Summary);
    return result.Success ? 0 : 1;
}
catch (Exception ex) when (ex is RenderException or OutputException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
```

A broken template or an unwritable output directory is the user's problem to fix,
not a bug to report, so it gets a message rather than a stack trace. Only those two
types are caught: anything else really is a bug, and the trace is what helps.

`Host.CreateApplicationBuilder` is what makes `appsettings.json`, environment
variables, user-secrets, and command-line arguments work with no code — the exact
chain the original design specified by hand. The configuration section of this
design is therefore a description of what the framework already does, not something
to build.

## Validation

`TemplateValidator.Validate`'s `DeliveryMode` parameter **becomes required**; its
`DeliveryMode.Fanout` default is removed.

This resolves item R5 in [`known-issues.md`](../../known-issues.md) rather than
inheriting it. A `collate` template validated as `Fanout` reports `count` and
`records` as unknown fields and then never descends into the loop body — failing
valid templates while passing broken ones. There is now a real caller, so the
compiler should ask the question rather than a default answering it wrongly.

The renderer passes each template's own `Config.Delivery`.

## Templates

Both ship in `DrMoustache.Templates`, installed with
`dotnet new install DrMoustache.Templates`.

### `dotnet new drmoustache -n MyReport` — the skeleton

```
MyReport/
  MyReport.csproj              references DrMoustache
  Program.cs                   the entry point above
  MyRecord.cs                  attributed POCO, three fields
  MyProvider.cs                the file you replace
  templates/report.md          one fanout template
  appsettings.json             DrMoustache section with the defaults
  .gitignore                   dist/ bin/ obj/
```

`MyProvider` yields **one hardcoded placeholder record** rather than an empty
sequence, so `dotnet run` produces a document immediately. This is deliberate: with
an empty provider, a user writes their real provider, gets nothing, and cannot tell
whether their provider is wrong or the scaffold never worked. One placeholder costs
three lines and makes the scaffold self-evidently sound before it is touched. It is
marked unmistakably as the thing to delete.

### `dotnet new drmoustache-sample -n WeatherReport` — the worked example

Self-contained and runnable with no external service: it creates a SQLite database
on first run, seeds it, and renders it.

```
WeatherReport/
  Program.cs                   DI wiring plus first-run seeding
  ObservationRecord.cs         four fields, one nullable
  SqliteObservationProvider.cs constructor-injected IOptions and ILogger
  SqliteOptions.cs             bound from appsettings.json
  templates/
    station.md                 fanout — one page per station
    summary.md                 collate — one page, a table over all records
  appsettings.json
  README.md                    what to look at, and why
```

The sample demonstrates what is easy to get wrong, not what is easy:

| Shown | Why |
|---|---|
| `{{#records}}\| {{station}} \| {{temperature}} \|` in a table | The table-row case the escaping engine exists for |
| A value containing `\|` and `*` | Proves data cannot reshape the table — visible in the output |
| `{{date recordedAt "yyyy-MM-dd"}}` | The helper set, and why `format` rejects temporal values |
| `{{#note}}…{{/note}}` on a nullable | Section-as-presence, not as `if` |
| `{{{raw}}}` on one field | The escape hatch, and when it is justified |
| `onCollision: suffix` with a real collision | Makes the warning visible on first run |

Sections look like `if` statements and are not; a sample showing only happy-path
interpolation would teach nothing the README does not.

SQLite is used because it is the only zero-configuration data source that is still a
real database. It was originally chosen because its native assets stressed
`AssemblyDependencyResolver`; that reason no longer applies, and the justification is
recorded here so the old reasoning does not confuse a later reader.

The database is seeded on first run rather than shipped as a `.db`, keeping the
template package text-only — `dotnet new` templates get reviewed like source.

## Escaping and the template format

Unchanged from [`2026-08-01-drmoustache-design.md`](2026-08-01-drmoustache-design.md),
which remains authoritative: Markdown-aware escaping by document position, the
`markdown is the layout, Mustache is the data` principle, delivery modes, collision
policies, the closed helper set, and per-value slugification as a security boundary.

**One change:** the `{{cell x}}`, `{{inline x}}` and `{{text x}}` escaping overrides
are **struck**. They were documented but never implemented, and currently throw
`unknown helper`. No case has yet arisen where context inference guessed wrong, and
`{{{raw}}}` already exists as an escape hatch. They can be added when a concrete
template demonstrates the need, with that template as the test.

The overrides section is removed from the original spec rather than left describing
behaviour that does not exist.

## Versioning and API commitment

Packages ship at **`0.1.0`**.

**The entire public surface of every package is supported API.** There is no
hidden-but-reachable tier and no "internal, do not use" convention. What is public
is what is promised.

Pre-1.0 means breaking changes may land in minor releases. They will be listed in
release notes. `1.0.0` follows once the library has been used against a real
integration.

This matters more than usual here: `ContextScanner` and `MarkdownEscaper` each
changed behaviour several times during implementation, and both are public. Freezing
them at `1.0.0` before the library has rendered real data would be premature.

## Deferred

Recorded with reasoning, so the decisions are reviewable rather than merely listed.

### The CLI and plugin loading

The global tool, `AssemblyLoadContext` plugin loading, NuGet acquisition, and
`drmoustache.yaml`. The written design survives as
[`2026-08-02-drmoustache-cli.md`](../plans/2026-08-02-drmoustache-cli.md) and its
part 2, both marked deferred.

**When it becomes worth building:** a provider written once and consumed by many —
an organisation shipping `Acme.DrMoustache.Dataverse` to its teams, where the people
writing templates are not the people who wrote the reader. It costs more than it
returns when the provider and the templates are written together, which is the v1
case.

If built, the CLI becomes a host that loads providers as plugins and calls the same
rendering library the template model uses, so the two paths stay coherent.

### Everything in `known-issues.md`

Unchanged, except:

- **R5** is resolved by this design (required `DeliveryMode`).
- **Package metadata missing** is resolved by shipping.

### Also deferred, from the original spec

Source-generated manifests for Native AOT; a `watch` mode; first-party JSON, CSV,
SQL or HTTP providers; user-defined helpers.

## Testing

Core's 431 tests stand unchanged — the engine is untouched by this design.

New work is tested at three levels:

- **Unit.** DI registration produces the expected keyed resolutions; options bind
  from configuration; `SourceRegistration` enumerates what was registered;
  `RenderResult.Success` honours `Strict`.
- **Integration.** A real `IServiceProvider`, a real provider yielding real records,
  real templates on disk, real files written. This is where the pipeline is proven.
- **Template.** Both `dotnet new` templates are instantiated into a temporary
  directory, built, and run in CI, and their output is asserted. A scaffold that does
  not compile is the single most damaging defect this project can ship, because it is
  the first thing every user sees.

The last one is not optional. A template package is code that nobody compiles until a
stranger does.
