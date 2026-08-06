# DrMoustache report (worked example)

A fuller example than the minimal scaffold, built to be read rather than
just run. Everything below is there to teach something specific; read the
files in this order.

## `Program.cs`: it seeds a database, but that's not the point

`Program.cs` fills a SQLite database on first run (`Seed`, guarded by
`File.Exists`). That's scaffolding, not the lesson — a `dotnet new` template
package ships as text, so it cannot carry a `.db` file, and this sample has
to be runnable standalone. Everything else in `Program.cs` — building the
host, calling `AddDrMoustache`, resolving `IDocumentRenderer`, the
`--check` branch — is identical in shape to the minimal scaffold. Once the
database exists, `SqliteObservationProvider` (an ordinary DI service) reads
it; DrMoustache never learns a database was involved.

## `templates/summary.md`: the table row is the whole point of this project

```
{{#records}}| {{station}} | {{temperature}} | {{date recordedAt "yyyy-MM-dd HH:mm"}} | {{default note "—"}} |
{{/records}}
```

The seeded `beta` observation has the note `sensor drift | readings
*suspect*` — a literal pipe and literal asterisks. Run the sample and look
at `dist/summary.md`: that pipe does not split the table row, and those
asterisks do not become italics. That's the entire premise of DrMoustache's
escaping made visible in output you can inspect yourself, not just take on
faith.

`{{default note "—"}}` is the `default` helper: most observations have no
note, so this substitutes an em dash rather than leaving the cell blank.

## `templates/station.md`: a section is presence, not a condition

```
{{#note}}> {{.}}
{{/note}}
```

`{{#note}}...{{/note}}` looks like `if note != null`, and for an optional
scalar field it behaves that way, but that is a special case of what a
section actually does: test whether the value is present, and if so render
the block once per value (for a list, once per item; `{{.}}` is "the
current item"). Two of the three seeded rows have no note, so the fan-out
run produces two station files without a quote block and one with — the
same syntax, driven purely by whether the data is there.

`station.md` also delivers with `delivery: fanout` and
`onCollision: suffix`, one file per station. Two rows share the station
name `beta`, so the second write collides with the first and you'll see a
collision warning printed on first run — `onCollision: suffix` resolves it
by appending a suffix rather than overwriting or failing.

## `{{date}}` vs `{{format}}`

`{{date recordedAt "yyyy-MM-dd HH:mm"}}` formats a `DateTimeOffset` with a
.NET custom date/time format string. `{{format}}` (not used here, but
available) applies a `string.Format`-style composite format to any value —
reach for `date` when formatting dates and times, `format` for everything
else, including numbers.

## Running it

```bash
dotnet run
```

First run seeds `weather.db`, renders three station files under
`dist/stations/`, one collated `dist/summary.md`, and prints a collision
warning for `beta`. Pass `--check` to validate configuration and templates
without touching the database or writing output.
