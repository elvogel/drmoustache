# Releasing

A checklist for cutting a DrMoustache release. Work through it in order.

1. Every test green, including the template tests.
2. `docs/known-issues.md` reviewed — anything fixed this cycle removed.
3. `<Version>` bumped in `Directory.Build.props`.
4. Release notes drafted, **listing any breaking change**. This is required,
   not optional: DrMoustache is pre-1.0, which permits breaking changes in a
   minor release, and the README promises they will be listed.
5. `dotnet pack -c Release`.
6. Smoke test from the packed output: install the templates package from the
   local feed, instantiate both templates, `dotnet run` each.
7. `dotnet nuget push` in dependency order — `Abstractions`, `Core`,
   `DrMoustache`, `Templates`.
8. Tag the commit, publish a GitHub release with the notes.

## Why step 6 is not optional

The templates package references the other three packages **by version**, not
by project reference. A version bump that misses one package produces a
scaffold that restores fine on your machine — your local NuGet cache still
has the old version alongside the new one, or the source tree resolves it
another way — and is broken for everyone else: their restore reaches for a
version of a dependency package that was never pushed. That failure mode
only surfaces on a clean, cold machine, which is exactly what a new user's
first `dotnet new` run is. Skipping step 6 means shipping something you
haven't actually tested.

A test added during development, `ReferencesTheVersionThisRepositoryActuallyPacks`,
guards the same drift in CI on every push. Step 6 is belt and braces on top
of that guard for the release itself — it exercises the packed, pushed
artifacts a real consumer would get, which CI's in-repo build cannot.
