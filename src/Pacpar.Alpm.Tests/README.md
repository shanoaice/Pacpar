# Pacpar.Alpm.Tests

This project contains the C# test harness for `Pacpar.Alpm`.

## Test layout

Every folder under `Unit/` is one topic and one namespace: a file's namespace is
`Pacpar.Alpm.Tests.Unit.<Folder>`.

- `Unit/Callbacks/` — the callback thunks, their registration and the log `va_list` forwarding.
- `Unit/Databases/` — `Database` behaviour: search, update, servers, usage.
- `Unit/Errors/` — errno classification, the `Try*` seam, and what a failed transaction reports.
- `Unit/GcAnchor/` — the IL audit that every read of unmanaged memory names its anchor, plus the
  IL decoder it runs on.
- `Unit/Interop/` — drift between the hand-written `[LibraryImport]` layer and the generated one.
- `Unit/Lifetime/` — lifetimes: the domains and stamps themselves, and the sites that invalidate them.
- `Unit/Options/` — the option collections and the options the collections do not cover.
- `Unit/Ownership/` — which side owns a package, a list or a transaction.
- `Unit/Packages/` — reading a package: files, mtree, changelog, dependency resolution.
- `Unit/Session/` — `AlpmSession`-level APIs that need no database: unlock, logaction, fetch,
  dependency checks, version and capabilities.
- `Unit/Snapshots/` — the managed values copied out of native memory, and the enums that mirror
  their native counterparts.
- `Fixtures/` contains reusable environment setup that can also be shared by future F# tests.
- `native/` contains a C caller for the `va_list` forwarding test, compiled by this project into
  its intermediate output. It is test-only: `Pacpar.Alpm` itself ships no native artifact.

Tests that cover one of the APIs recorded in the coverage ledger
(`src/Pacpar.Alpm/COMPLETENESS.md` §4) live in the topic folder with everything else; their class
remarks name the ledger entry and the libalpm behaviour that was measured for it.

## Running locally

The host must provide `libalpm`, `pkg-config`, Rust, a C compiler (`cc`, from `gcc` or `clang`), and the .NET SDK.

```bash
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj
```

## Running in an OCI container

Use `scripts/run-alpm-tests-in-container.sh`. The script auto-detects `podman`, `nerdctl`, or `docker`, and builds `containers/alpm-tests/Containerfile`.
