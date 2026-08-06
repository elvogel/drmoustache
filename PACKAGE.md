# DrMoustache

Renders Markdown templates against records from a data provider you write.
Every value is escaped for the exact Markdown context it lands in, so a
stray `|` or `*` in your data cannot break a table or turn into italics.

It is not a logic engine — no conditionals, no expressions, no computation.
If your output depends on a decision, make that decision in your provider.

## Minimal usage

```csharp
builder.Services
    .AddDrMoustache(builder.Configuration.GetSection("DrMoustache").Bind)
    .AddSource<MyProvider>("main");

using var host = builder.Build();
var renderer = host.Services.GetRequiredService<IDocumentRenderer>();

var result = await renderer.RenderAsync();
Console.Write(result.Summary);
```

`MyProvider` derives from `DataProvider<TRecord>` and is registered like any
other DI service — DrMoustache ships no data sources of its own; you write
the provider.

## Get started fast

```bash
dotnet new install DrMoustache.Templates
dotnet new drmoustache -n MyReport
```

Full documentation, the worked SQLite example, and the escaping example
live in the repository:
[github.com/elvogel/drmoustache](https://github.com/elvogel/drmoustache).

Pre-1.0 (`0.1.0`): the full public surface is supported API, but breaking
changes may land in a minor release and will be called out in its notes.

Apache-2.0.
