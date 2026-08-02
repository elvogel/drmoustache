# Known issues and deferred work

Findings that survived the Core implementation plan (2026-08-01) with a written
ruling rather than a fix. Each was reproduced or traced; none is speculative.

Carried here from the execution ledger so the reasoning outlives the scratch
workspace. Plan 2 (the CLI) should triage this list.

## Open — needs a product decision

### The spec documents escaping overrides that do not exist

`docs/superpowers/specs/2026-08-01-drmoustache-design.md` describes `{{cell x}}`,
`{{inline x}}` and `{{text x}}` as manual overrides for when context inference
guesses wrong — *"inference is a default, never a cage"*. They were never
implemented. They currently parse as helpers and throw `unknown helper 'cell'`.

The implementation plan's own self-review wrongly claimed them covered, which is
how it went unnoticed until the final whole-branch review.

**Decide:** implement them, or strike them from the spec. `{{{raw}}}` remains the
only escape hatch either way, and it is a blunt one — it disables escaping
entirely rather than selecting a different rule.

## Open — correctness, ruled non-blocking

### R2 — an unterminated code span still narrows escaping

`ContextScanner.CloseUnmatchedCodeSpan` back-fills a leaked code span's positions
to `Inline`, rather than re-deriving what each position would otherwise have been.
So a position at column 0 inside a leaked span loses its block-marker rules, and a
value of `# Pwned` there renders as a live heading.

**Ruling:** real, and it does leave a data-controlled injection reachable — but it
requires the *template author* to have left an unbalanced backtick, and templates
are trusted input. A template-authoring hazard that unlocks a data injection, not a
data-only attack.

**Proper fix:** re-derive scanner geometry over the back-filled range instead of
blanket-assigning `Inline`.

### R3 — a frontmatter value of `null` becomes YAML null

A passthrough frontmatter value of `"null"`, `"~"`, `"NULL"` or `"Null"` is emitted
unquoted and reads back as null rather than as the string.

**Ruling:** narrow. Every other coercion-prone scalar tested round-trips correctly
as a string — `yes`, `no`, `true`, `false`, `on`, `off`, `0755`, `1.0`, `12`,
`12:30`. **Fix:** force-quote YAML null tokens on emit.

### R4 — `\r\n` inside a frontmatter value normalises to `\n`

YAML's own line-break normalisation. Arguably unavoidable; recorded because it is a
second case where the parsed value does not equal the original.

### R6 — two block constructs the escaper does not cover at column 0

- `<div>x</div>` parses as an `HtmlBlock`. This is **spec-sanctioned**: the spec
  states HTML entities are never escaped, because the output is Markdown and a
  downstream renderer handles HTML.
- A leading **tab** produces an indented code block. `BlockRuleLength` tests for
  four literal spaces only. This one looks like an oversight rather than a decision
  and is the more likely of the two to want fixing.

## Open — API and ergonomics

### R5 — `TemplateValidator.Validate` defaults `DeliveryMode` to `Fanout`

There is no production caller yet, so nothing can currently hit the wrong default.
But a `collate` template validated with the default silently reproduces the bug
where `check` reports `unknown field 'count'` and then skips the loop body
entirely — failing valid templates while passing broken ones.

**Ruling:** the CLI plan builds the first caller. Make the parameter required at
that point, or derive it from the template's own config.

### Package metadata is missing

No `<Version>`, no `PackageReadmeFile`, no `.editorconfig`, and no public-API
surface guard — on two packages intended for third-party consumption.

## Deferred minor findings

Each was raised by a task review, judged Minor, and traced or verified harmless.

| Area | Finding |
|---|---|
| Build | `Deterministic=true` only under `Release`; CI may want it unconditionally |
| `RecordManifest` | `FailsOnUnknownPath` uses `out _`, so the documented null-on-failure guarantee is untested |
| `FieldType` | CA1720 pragma is file-wide rather than scoped (file holds one type) |
| `ManifestBuilder` | `GetElementType`'s `FirstOrDefault` is order-dependent for a type implementing `IEnumerable<T>` twice |
| `ManifestBuilder` | `ToCamelCase` renders `ID` as `iD`, `URLPath` as `uRLPath`; consider a leading-acronym rule |
| `ManifestBuilder` | A non-generic `IEnumerable` (e.g. `ArrayList`) classifies as `Object` and is reflected over |
| `ContextScanner` | `LooksLikeTableRow` skips unbounded leading whitespace — a 4-space-indented pipe line is a code block, not a table |
| `ContextScanner` | The spec's second table rule, "or following a delimiter row", is not implemented |
| `TemplateLexer` | No CRLF test for `LineOf`; no empty-quoted-argument test; no indented-standalone-**comment** test |
| `FrontmatterParser` | No test for a `----` rule or bare `---` in body content, frontmatter filling the entire file, or nested indentation under a passthrough key |
| `ValueResolver` | `FlattenHierarchy` is a no-op for instance properties; `AsSequence` does not special-case `IDictionary` |
| `TemplateRenderer` | No test for scope-stack balance when a section body throws (verified harmless — scopes are per-call); none for `{{.}}` at top level |
| `TemplateValidator` | Redundant double lookup at root scope |
| `DeliveryPlanner` | `Path.IsPathRooted` is platform-dependent, inside a component whose architecture test asserts Core has no filesystem knowledge; `SanitizePath` splits on `/` only, not `\` |
| `DeliveryPlanner` | Passthrough frontmatter is re-lexed for every record |
| `MarkdownEscaper` | `EscapeLinkTarget` encodes space and parens but not `<`, `>`, `"` |
| `RoundTripTests` | `ExtractText` throws on an unexpected inline type, so a value containing `&amp;` crashes the test rather than failing it |

## Not carried forward

Two rulings worth recording because they are deliberate design, not debt:

- **`IsTruthy` then `AsSequence` enumerates a value twice.** Re-enumerability is
  documented as an upstream invariant rather than enforced, because the manifest
  only classifies an `IEnumerable<T>` record property as `Collection` and those are
  materialized in practice. Materializing inside the resolver would change the
  contract for no reachable benefit.
- **`ordinal` collision suffixes are not stable across runs.** That is the
  documented trade-off against `suffix`, which hashes record content and is stable.
