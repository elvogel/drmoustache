# DrMoustache — Engine Design

**Date:** 2026-08-01
**Status:** Implemented. Partially superseded.

> **Partially superseded by
> [`2026-08-04-drmoustache-v1-design.md`](2026-08-04-drmoustache-v1-design.md).**
>
> This document remains **authoritative for the rendering engine**: Markdown-aware
> escaping, the template format, the manifest, delivery modes, collision policies,
> helpers, culture, and per-value slugification.
>
> Four sections are **superseded** and describe a delivery model that was not built —
> *Configuration*, *Configuration visibility to plugins*, *Plugin distribution and
> loading*, and *CLI*. v1 ships as a `dotnet new` project template with dependency
> injection instead of a global tool loading NuGet plugins, which deletes
> `ProviderContext`, `scopeConfig`, and the hand-built configuration chain entirely.
> Read the v1 design for what those sections became.
>
> The *Provider contract* section is also superseded in one respect: `ReadAsync` no
> longer takes a `ProviderContext`.
**Repo:** https://github.com/elvogel/drmoustache
**License:** Apache-2.0

## Purpose

DrMoustache renders Markdown templates written in Mustache syntax against data
supplied by the caller, and writes the resulting Markdown to disk.

Its one distinguishing capability is that it is **Markdown-aware**: interpolated
values are escaped according to their position in the document, so a value
containing `|` cannot shatter a table row and a value beginning with `#` cannot
become a heading.

### Non-goals

These are stated prominently in the README, not merely implied:

> DrMoustache puts your fields into Markdown. It is not a logic engine.
> There are no conditionals, no expressions, no computation, no branching.
> If your output depends on a decision, make that decision in your provider —
> that is what it is for. DrMoustache writes files.

Sections (`{{#items}}`) iterate collections and test presence. That is the entire
control surface, and it is the Mustache contract already.

Further non-goals:

- **DrMoustache does not read data.** It has no notion of files, databases,
  queues, or endpoints. All ingestion is the caller's, via a plugin.
- **DrMoustache does not emit HTML.** It emits Markdown. Rendering to HTML is a
  downstream concern (see *Relationship to Crucible*).
- **DrMoustache does not infer schemas.** Providers declare what they supply.

## Relationship to Crucible

[Crucible](https://github.com/phoenixmldb/crucible) is a static documentation
generator: Markdown + YAML frontmatter → XML intermediate → XSLT 4.0 → HTML. It
has no data binding; every document is hand-authored.

The two tools compose as a pipe and share no code:

```
drmoustache render  →  ./docs/*.md  →  crucible build  →  ./dist
```

DrMoustache is deliberately **not** implemented as an `ICrucibleExtension`. That
interface operates per-Markdig-node during XML emission, which is far too late and
too granular for data binding. Keeping them separate keeps both single-purpose and
lets DrMoustache feed Hugo, Jekyll, MkDocs, or nothing at all.

A secondary goal: this project doubles as a worked example of **plugin
architecture in .NET 10**, so its loader, sample plugin, and documentation are
written to be read by other developers.

## Architecture

```
        CALLER'S PLUGIN                       DRMOUSTACHE
  ┌──────────────────────┐            ┌────────────────────────┐
  │ reads whatever       │            │  parse template        │
  │ it likes, merges,    │  manifest  │  bind fields           │
  │ joins, projects      │ ─────────► │  escape per md context │
  │ → one record type    │  records   │  apply delivery        │
  └──────────────────────┘            │  write .md files       │
                                      └────────────────────────┘
     knows I/O, auth,                    knows Markdown and
     schemas, transports                 Mustache, nothing else
```

The caller's provider dissolves the distinction between transport metadata and
payload content *before* DrMoustache sees anything. The engine receives a flat or
nested set of named, typed fields with no transport concepts attached.

### Projects

| Project | Purpose | Dependencies |
|---|---|---|
| `DrMoustache.Abstractions` | Plugin contract: attributes, `IDataProvider`, `ProviderContext` | `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` |
| `DrMoustache.Core` | Engine: template parse, bind, escape, delivery planning. **No I/O.** | `YamlDotNet`, Abstractions |
| `DrMoustache.Cli` | `dotnet tool`: plugin acquisition, configuration, file writing | Core, `System.CommandLine`, NuGet client |
| `DrMoustache.Sample.Plugin` | Worked example with real transitive dependencies | Abstractions |

`Abstractions` is a separate package so plugin authors reference a small, stable
surface. A plugin built against Abstractions 1.0 must keep working when the engine
reaches 1.4; if plugins referenced `Core`, every engine change would threaten them.

`Core` performs no I/O so the escaping engine — the subtle part — is tested against
plain in-memory dictionaries with no fixtures, containers, or network.

## Provider contract

Read-only by construction. There is no write, delete, or acknowledge surface
anywhere in the interface, and the naming reinforces it. This matters because a
plausible early use is browsing a dead-letter queue, where a destructive read would
be unrecoverable.

```csharp
namespace DrMoustache.Abstractions;

/// <summary>Supplies records to the rendering engine. Read-only: providers
/// must not mutate their underlying source.</summary>
public interface IDataProvider
{
    string Name { get; }                    // matched by `source:` in a template
    Type RecordType { get; }                // the [DrMoustacheRecord] POCO
    IAsyncEnumerable<object> ReadAsync(ProviderContext ctx, CancellationToken ct);
}

/// <summary>Typed base class — what plugin authors derive from.</summary>
public abstract class DataProvider<TRecord> : IDataProvider where TRecord : class
{
    public abstract string Name { get; }
    public Type RecordType => typeof(TRecord);
    public abstract IAsyncEnumerable<TRecord> ReadAsync(
        ProviderContext ctx, CancellationToken ct);
    // explicit interface implementation bridges typed -> object for the loader
}

public sealed class ProviderContext
{
    public required IConfiguration Configuration { get; init; }  // full chain
    public required IConfigurationSection Source { get; init; }  // this source only
    public required ILogger Logger { get; init; }
}
```

The non-generic `IDataProvider` exists so the loader has something to activate
against; authors always derive from `DataProvider<TRecord>`.

`IAsyncEnumerable` rather than a materialized collection: a source with 50,000
records should not have to fit in memory before the first document renders, and it
gives natural cancellation.

## Manifest

Declared by the provider as an attributed POCO. There is no inference, sampling, or
probing anywhere in the system. The provider author has already assembled the
projection, so declaring it is zero marginal work and yields compile-time safety.

```csharp
[DrMoustacheRecord("failed-order")]
public sealed record FailedOrder
{
    [Field] public required long    SequenceNumber    { get; init; }
    [Field("Why it dead-lettered")]
            public required string  Reason            { get; init; }
    [Field("The customer-facing order number")]
            public required string  OrderId           { get; init; }
    [Field] public required decimal Total             { get; init; }
    [Field] public DateTimeOffset   PlacedAt          { get; init; }
    [Field] public int              RetryCount        { get; init; }
    [Field] public string?          PromoCode         { get; init; }
    [Field] public required OrderInfo        Order    { get; init; }
    [Field] public IReadOnlyList<LineItem>   Items    { get; init; } = [];
}
```

The record is named for a business concept. Transport nouns never appear.

### Name mapping

| Declaration | Template reference |
|---|---|
| `OrderId` | `{{orderId}}` |
| `Order.Total` | `{{order.total}}` |
| `Items` (collection) | `{{#items}}…{{/items}}` |
| `PromoCode` (nullable) | `{{#promoCode}}…{{/promoCode}}` guards it |

Property names map to camelCase tags; nesting maps to dotted paths.

### What types do

The manifest carries a `FieldType` per field, which drives three behaviours:

1. **Escaping.** `DateTimeOffset`, numerics, `bool`, and `Guid` cannot produce
   Markdown-significant characters and skip escaping entirely. Only `string` (and
   `object.ToString()`) goes through the rules table.
2. **Validation.** `check` rejects `{{order}}` with *"`order` is an object — did
   you mean `{{order.id}}`?"*
3. **Helper arity.** `{{date placedAt "yyyy-MM-dd"}}` is valid only on a temporal
   field.

`required` is load-bearing beyond compile safety: it is how the manifest knows a
field is non-nullable, which lets `check` report that `{{#promoCode}}` is necessary
around a nullable field but redundant around a required one.

The manifest is reflected once per provider load, not per record, so it is off the
hot path. `Manifest` is designed as a plain data structure holding no `PropertyInfo`,
so a source generator can produce it at compile time later without changing the
consuming code (see *Deferred*).

## Template format

One YAML frontmatter block. The reserved `drmoustache` key configures the render and
is stripped from output. **Every other key is itself a template**, interpolated per
record and emitted as the output document's frontmatter — so a downstream tool such
as Crucible receives real, per-record frontmatter.

```markdown
---
drmoustache:
  source: dlq
  delivery: fanout
  output: dlq/{{reason}}/{{sequenceNumber}}.md
  onCollision: suffix
title: "{{orderId}} — {{reason}}"
layout: message
tags: [deadletter, "{{reason}}"]
---

# Order {{orderId}}

Dead-lettered {{date placedAt "yyyy-MM-dd HH:mm"}} after {{retryCount}} attempts.

> {{reason}}

| Item | Qty | Price |
|------|-----|-------|
{{#items}}| {{name}} | {{qty}} | {{format price "C"}} |
{{/items}}
```

### YAML and leading braces

In YAML, `{` is an indicator character **only at the start of a plain scalar**. A
value beginning with `{{` is therefore read as a flow mapping and fails; a value
with `{{` anywhere else parses correctly.

```yaml
output: dlq/{{sequenceNumber}}.md    # valid — plain scalar starting with 'd'
output: "{{sequenceNumber}}.md"      # quotes REQUIRED — starts with '{'
title: "{{orderId}}"                 # quotes REQUIRED
```

DrMoustache does not auto-quote, because a value that legitimately is a flow mapping
would be corrupted. Instead it detects this specific YAML failure and rewrites the
error into an actionable message (see *Errors*). Templates produced by `init` model
the quoted form.

## Delivery

Declared in the reserved block. Same provider and manifest, different distribution
across files.

### `fanout` — one document per record

Root context is the record. `output` is a Mustache pattern over the record's fields.

**Interpolated values are slugified individually, before they are spliced into the
pattern — never after.** Slugification lowercases, converts whitespace to `-`, and
strips path separators, dot segments, and filesystem-unsafe characters. Directory
structure comes only from literal `/` written by the template author; a value
containing `/` collapses to a single segment.

This is a security boundary, not a formatting preference. Records frequently
originate in untrusted external input — a dead-letter queue message, an API
response — and a value such as `../../etc/passwd` must not be able to write outside
the output directory. Slugifying the *composed* path rather than each value cannot
provide this guarantee, because by then the data's separators are indistinguishable
from the template's own.

`onCollision` is **required** and has no library default. Choosing a collision policy
is the user's decision, not the tool's; omitting it is a template error listing the
four choices.

| Policy | Behaviour |
|---|---|
| `error` | Fail the render, naming the colliding values |
| `suffix` | Append a short hash of the record's declared field values, in manifest order. Deterministic across runs regardless of the order records arrive in |
| `ordinal` | Append `-2`, `-3` by encounter order. Not stable if the provider's order changes |
| `overwrite` | Last record wins; earlier ones are discarded |

`suffix` and `ordinal` emit a warning naming the colliding field.

### `collate` — all records into one document

Root context exposes `{{#records}}` and `{{count}}`.

```markdown
---
drmoustache:
  source: dlq
  delivery: collate
  output: reports/deadletter.md
---
# Dead-letter queue — {{count}} messages

| Seq | Order | Reason |
|-----|-------|--------|
{{#records}}| {{sequenceNumber}} | {{orderId}} | {{reason}} |
{{/records}}
```

## Escaping engine

A single forward pass over the template text maintains a small state machine. There
is no Markdown AST, because a template is not valid Markdown until expanded —
`{{#items}}| {{name}} |` is a paragraph to any parser, and only becomes a table row
after the section expands.

### Contexts

| Context | Detected by |
|---|---|
| `FencedCode` | Fence character and run length |
| `CodeSpan` | Backtick run length |
| `TableRow` | Line matching `^\s*\|`, or following a delimiter row |
| `LinkTarget` | Inside `](` … `)` |
| `LineStart` | Columns 0–3 of a line, outside code |
| `Inline` | Everything else |

### Rules

| Context | Escaped | Left alone |
|---|---|---|
| `FencedCode` | nothing | everything |
| `CodeSpan` | nothing; widen the fence if the value contains a backtick run | everything |
| `TableRow` | everything `Inline` escapes, plus newline → `<br>` | `#`, `>`, `-` |
| `LineStart` | leading `#` `>` `-` `*` `+`, `1.`, four-space indent | everything after column 0 |
| `LinkTarget` | percent-encode space, `(`, `)` | — |
| `Inline` | `*` `_` `[` `]` `` ` `` `\|` | `#` `-` `>` |

**Never escaped anywhere:** `&`, `<`, `>` as HTML entities. This is Markdown output,
not HTML; the downstream renderer handles HTML escaping. `O'Brien & Sons` is emitted
verbatim.

`|` is escaped in `Inline` as well as `TableRow`. One invariant that is always true
is preferable to one whose correctness depends on table detection having succeeded.
The cost is nil: `|` is ASCII punctuation, so CommonMark renders `\|` as a plain `|`
with no visible backslash.

**Markdown trumps Mustache.** A table cell escapes emphasis and link characters as
well as pipes, so a value of `*Special*` renders as literal `*Special*` rather than
becoming italic. Interpolated data must never change the document's Markdown
structure — that would be Mustache content overriding the template author's intent.
An author who wants formatting to come from data uses `{{{raw}}}`.

This is also what makes the round-trip property in *Testing* achievable: a cell that
leaves `*` live cannot return a value containing `*` unchanged, because the parser
consumes it as emphasis.

**Autolinks are left alone.** A value that looks like a URL becomes a link, which is
usually what is wanted.

### Overrides

| Form | Behaviour |
|---|---|
| `{{x}}` | Inferred from context |
| `{{{x}}}` | Raw, always, no escaping |

An earlier draft of this document also specified `{{cell x}}`, `{{inline x}}` and
`{{text x}}` to force a particular escaping context. **They were never implemented
and have been struck** — see the v1 design. No case has yet arisen where inference
guessed wrong, and `{{{raw}}}` already serves as an escape hatch. They can return
when a concrete template demonstrates the need, with that template as the test.

### Sections and whitespace

Mustache's standalone-tag rule — a tag alone on a line consumes that line — was
written for HTML and gets Markdown wrong; it would consume the newline terminating a
table row. The rule here: **a section tag alone on a line consumes the line only
when it is not inside a `TableRow` or list context.** This produces one row per item:

```markdown
{{#items}}| {{name}} | {{qty}} |
{{/items}}
```

## Helpers

A closed set. There is no user-defined helper registry; extensibility is the
provider's job, and a value that needs computing should be computed there.

| Helper | Applies to | Example |
|---|---|---|
| `date` | temporal | `{{date placedAt "yyyy-MM-dd"}}` |
| `format` | numeric | `{{format total "N2"}}` |
| `upper` / `lower` | string | `{{upper reason}}` |
| `join` | collection of scalars | `{{join tags ", "}}` |
| `default` | any nullable | `{{default promoCode "none"}}` |

Helper output is escaped by the same context rules as a plain interpolation.
Helpers do not bypass escaping.

### Culture

**Formatting is invariant by default.** Numbers, dates, and any other
`IFormattable` value render identically regardless of the machine's locale, so the
same template and data produce byte-identical output on a developer's laptop and in
CI. For a tool whose artifacts are committed and diffed, a build that varies by host
locale is a defect.

A caller who wants localised output opts in explicitly:

```csharp
TemplateRenderer.Render(nodes, model, CultureInfo.GetCultureInfo("de-DE"));
```

The culture flows through to helpers, so `{{format total "N2"}}` and
`{{date placedAt "d"}}` honour it too.

One consequence worth stating: `true` and `false` render lowercase, and a single
shared formatter is used for both plain interpolations and helper output — the same
value must never render differently depending on whether a helper touched it.

## Configuration

`IConfiguration` first, environment variables second.

The CLI builds a chain: `appsettings.json` (optional; a missing file is not an
error) → environment variables → command-line arguments. Because environment is a
provider *in* the chain, a user with no configuration file still gets environment
variables with no special-casing, and `dotnet user-secrets` works in development for
free. When the library is embedded, the host's `IConfiguration` is used as supplied.

### Configuration visibility to plugins

The **full configuration chain** is passed to `ProviderContext.Configuration`, since
providers are near-certain to need it.

This is a documented security consideration, not an oversight: a plugin can read
every secret the host can, including connection strings for sources unrelated to it.
Because plugins arrive as NuGet packages, this is a supply-chain surface.

An opt-in narrows it per source:

```yaml
sources:
  dlq:
    type: servicebus-dlq
    scopeConfig: true      # provider sees only its own section
```

Default is the full chain. `ProviderContext.Source` always exposes just the source's
own section regardless of this setting.

## Plugin distribution and loading

Configuration names a NuGet package and version. The CLI restores it to a local
cache and loads it in a collectible `AssemblyLoadContext` backed by an
`AssemblyDependencyResolver` over the package's `.deps.json`.

```yaml
plugins:
  - DrMoustache.ServiceBus@1.0.0

sources:
  dlq:
    type: servicebus-dlq
    namespace: ${SB_NAMESPACE}
    queue: orders
```

Dependency resolution via `.deps.json` is essential, not optional: a realistic
plugin carries a deep transitive tree, and a loader that only calls
`LoadFromAssemblyPath` will load the plugin successfully and then throw
`FileNotFoundException` at the first call into any dependency.

**`DrMoustache.Abstractions` must resolve from the default context, not the
plugin's.** If both contexts load their own copy, `is IDataProvider` returns false —
same type name, different `AssemblyLoadContext`, therefore not the same `Type` — and
plugins are silently skipped. The custom load context defers this one assembly
explicitly.

Contexts are collectible and their handles are retained, so a future `watch` mode
can unload and reload a rebuilt plugin without restarting the process.

## CLI

Three verbs.

| Verb | Purpose |
|---|---|
| `render` | Read templates, load plugins, bind data, write Markdown |
| `init` | Scaffold a starter template and `drmoustache.yaml` |
| `check` | Validate templates against manifests and probe plugin loading, without writing output |

`check` performs no provider reads — it validates that every referenced field exists
in the declared manifest, that delivery configuration is complete, and that named
plugins load. This makes it a fast CI gate.

There is no `serve`, `watch`, or `describe` verb. Viewing rendered output is
downstream (Crucible or any Markdown viewer).

## Errors

Two tiers, distinguished by blast radius.

### Template errors — static, fail immediately

These affect every record, so they are raised before any provider is asked for data.

```
error: unknown field 'orderid'                    dlq.md.mustache:14
       record 'failed-order' has no such field
       did you mean 'orderId'?  (string, the customer-facing order number)

error: cannot interpolate 'order' directly        dlq.md.mustache:9
       'order' is an object — reference a field:  {{order.id}}, {{order.total}}

error: 'onCollision' is required for fanout       dlq.md.mustache:5
       choose one: error | suffix | ordinal | overwrite

error: value must be quoted                       dlq.md.mustache:7
       title: {{orderId}}
              ^ YAML reads a leading '{' as a flow mapping
       write:  title: "{{orderId}}"
```

The `did you mean` suggestion uses the `[Field("…")]` description, which is the
reason descriptions exist.

### Record errors — per-item, collected

One malformed record must not destroy a large report. Failures are collected,
rendering continues, and the summary is honest about what was lost.

```
rendered 4,997 documents to ./dlq  (3 skipped, 1 collision)
  skipped  seq 10432  'total' was null but declared required
  skipped  seq 10433  'total' was null but declared required
  skipped  seq 10891  provider threw: JsonException at $.order.total
  renamed  seq 10102  order-42.md → order-42.9c3f1a.md
exit 0
```

`--strict` promotes any skip to a hard failure with exit 1 — the CI posture. The
default is lenient because ad-hoc inspection wants the 4,997 successful documents
rather than a stack trace.

**Documents are never partially written.** A record renders fully to memory and
reaches disk only when complete, so a mid-render failure leaves no half-written
file and re-running is always safe.

Silently dropping a record is never acceptable. A report that omits records without
saying so is worse than one that fails, because it will be trusted.

## Testing

The I/O-free core is what makes this inexpensive.

- **Escaping — table-driven, and the bulk of the suite.** `(value, context,
  expected)` triples over plain dictionaries. Every cell of the rules table gets
  rows, plus adversarial cases: pipes, backtick runs, leading `#`, CRLF,
  whitespace-only values, empty strings, unicode, values containing `{{`.
- **Round-trip property test.** For any string value, rendering it into a table cell
  and parsing the result with Markdig must yield a table whose cell content equals
  the original. This catches escaping defects the rules table misses and is the
  highest-value test in the project.
- **Golden-file tests** for template→output pairs under `tests/fixtures`, covering
  both delivery modes and every collision policy.
- **Plugin loading, end to end in CI.** Loads `DrMoustache.Sample.Plugin` from a
  published output folder with real transitive dependencies, proving the
  `AssemblyDependencyResolver` path rather than assuming it.
- **Architecture test.** Asserts `DrMoustache.Core` references no I/O or transport
  assembly and that its sources contain no transport nouns (`message`, `queue`,
  `row`, `endpoint`). A cheap guard on the boundary that matters most.

## Repository

```
src/DrMoustache.Abstractions
src/DrMoustache.Core
src/DrMoustache.Cli
samples/DrMoustache.Sample.Plugin
tests/DrMoustache.Core.Tests
tests/DrMoustache.Cli.Tests
docs/plugin-architecture.md
```

Build conventions mirror Crucible: `net10.0`, `LangVersion preview`, nullable
enabled, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel
latest-all`, central package management, xunit.v3, FluentAssertions pinned at
6.12.2 (the last MIT release). GitHub Actions for build, test, and pack.

Three packages ship: `DrMoustache.Abstractions`, `DrMoustache.Core`, and
`DrMoustache.Cli` as a `dotnet tool`.

`docs/plugin-architecture.md` is a deliberate teaching artifact covering
`AssemblyLoadContext`, `AssemblyDependencyResolver`, the shared-abstractions-assembly
trap, collectible contexts and unloading, and why the contract lives in its own
package.

## Deferred

Explicitly out of scope for v1, recorded so the design does not foreclose them:

- **Source-generated manifests** for Native AOT, replacing reflection. `Manifest`
  is designed as a plain data structure specifically to permit this later.
- **`watch` mode.** Load contexts are already collectible and retained for it.
- **First-party providers** for JSON, CSV, SQL, or HTTP. If they ever ship they are
  ordinary plugins with no privileges, proving the contract by construction.
- **User-defined helpers.** The closed set is deliberate; reconsider only with
  evidence that provider-side computation is genuinely insufficient.
