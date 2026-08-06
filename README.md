# DrMoustache

DrMoustache renders Markdown templates against records supplied by a data
provider you write. It escapes every value for the exact Markdown context it
lands in, so a stray `|` or `*` in your data cannot break a table or turn
into italics.

## What it is not

> DrMoustache puts your fields into Markdown. It is not a logic engine.
> There are no conditionals, no expressions, no computation, no branching.
> If your output depends on a decision, make that decision in your
> provider — that is what it is for. DrMoustache writes files.

## 60-second start

```bash
dotnet new install DrMoustache.Templates
dotnet new drmoustache -n MyReport
cd MyReport
# edit MyProvider.cs to read your real data, then:
dotnet run
```

`dotnet new drmoustache` scaffolds a console project with a placeholder
provider, a placeholder record, and one template. Replace the provider's
`ReadAsync` with your real data source and run it. Pass `--check` instead of
running to validate configuration and templates without touching data or
writing output.

For a fuller worked example — fan-out, collation, an optional field, and the
escaping payoff below, backed by a seeded SQLite database — use:

```bash
dotnet new drmoustache-sample -n WeatherReport
```

## What a template looks like

Every DrMoustache template is a Markdown file with YAML frontmatter
declaring where its data comes from and how the output is delivered, and a
body written in a small Mustache-like syntax. This is the collated
`summary.md` template from the sample:

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

`{{#records}}...{{/records}}` is a section: it fans out once per record.
`{{date ...}}` and `{{default ...}}` are helpers (see below). Here is real
rendered output from that template, against the sample's seeded data:

```
| Station | Temperature | Recorded | Note |
|---------|-------------|----------|------|
| alpha | 12.5 | 2026-08-05 09:00 | — |
| beta | -3.25 | 2026-08-05 09:05 | sensor drift \| readings \*suspect\* |
| beta | -3 | 2026-08-05 09:10 | — |
```

The `beta` note in the source data is the literal string
`sensor drift | readings *suspect*`. DrMoustache knows that position is a
table cell, so it escaped the pipe and the asterisks — the row still has
four columns, and the note reads as plain text instead of half-italicized.
That escaping is the entire reason this project exists.

Helpers are a closed, deliberate set: `date`, `format`, `upper`, `lower`,
`join`, `default`. There is no way to register your own — logic belongs in
the provider, not the template.

## What a provider looks like

A provider is an ordinary dependency-injected service. Derive from
`DataProvider<TRecord>`, override `Name` and `ReadAsync`, and take whatever
you need — an `HttpClient`, a `DbContext`, `IOptions<T>` — through the
constructor:

```csharp
public sealed class MyProvider(ILogger<MyProvider> logger) : DataProvider<MyRecord>
{
    public override string Name => "main";

    public override async IAsyncEnumerable<MyRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        logger.LogInformation("Reading records");
        yield return new MyRecord { Id = "sample-1", Name = "Placeholder", Value = 42 };
    }
}
```

Records are plain classes or records annotated with `[DrMoustacheRecord]`
and `[Field]`:

```csharp
[DrMoustacheRecord("my-record")]
public sealed record MyRecord
{
    [Field("Used in the output file name")]
    public required string Id { get; init; }

    [Field] public required string Name { get; init; }
    [Field] public required int Value { get; init; }
}
```

Wire it up with dependency injection, then render:

```csharp
builder.Services
    .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
    .AddSource<MyProvider>("main");

using var host = builder.Build();
var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

var result = await renderer.RenderAsync();
Console.Write(result.Summary);
```

## The packages

| Package | What it is |
|---|---|
| `DrMoustache` | The metapackage: DI registration, source discovery, rendering, writing. **This is the one to reference.** |
| `DrMoustache.Abstractions` | The provider contract — `DataProvider<TRecord>`, `[DrMoustacheRecord]`, `[Field]`. Zero package dependencies. |
| `DrMoustache.Core` | The rendering engine (parsing, escaping, binding). No I/O; used internally by `DrMoustache`. |
| `DrMoustache.Templates` | The `dotnet new` templates. |

A normal project needs only `DrMoustache`; it brings in `Abstractions` and
`Core` as dependencies.

## You write the provider

DrMoustache ships no data sources of its own — no built-in SQL, HTTP, or
file readers. That is deliberate, not a gap: a provider is just a class you
write against your own data, so DrMoustache places no assumptions on where
your data comes from and works with any data set. The sample template's
SQLite provider is ~40 lines, and it is representative of the shape, not a
special case.

## Versioning

DrMoustache is at `0.1.0`. The entire public surface — every type across
all four packages — is supported API, but this is pre-1.0: breaking changes
may land in a minor release, and each one will be called out in that
release's notes.

## More

- [Known issues](docs/known-issues.md) — traced findings with written
  rulings, not silent gaps.
- [Design spec](docs/superpowers/specs/2026-08-04-drmoustache-v1-design.md)

## Composing with Crucible

DrMoustache and [Crucible](https://github.com/phoenixmldb/crucible) are
meant to be piped together: `dotnet run` on a DrMoustache project produces
Markdown files, and Crucible turns a directory of Markdown into a static
site. DrMoustache doesn't know Crucible exists, and Crucible doesn't know
DrMoustache exists — the interface between them is just files on disk.

## License

Apache-2.0. See [LICENSE](LICENSE).
