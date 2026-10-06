# Pacpar.Alpm.Tests

This project contains the C# test harness for `Pacpar.Alpm`.

## Test layout

- `Unit/` contains managed-only tests and hermetic libalpm tests that run against isolated temporary directory trees (`IsolatedAlpmEnvironment`).
- `Fixtures/` contains reusable environment setup that can also be shared by future F# tests.
- `Pending/` contains the acceptance tests for the features recorded in the coverage ledger
  (`src/Pacpar.Alpm/COMPLETENESS.md`). Those features exist now, so the folder builds and runs with
  the rest of the suite; the name records only that the files have not moved to `Unit/` yet.
- `native/` contains a C caller for the `va_list` forwarding test, compiled by this project into
  its intermediate output. It is test-only: `Pacpar.Alpm` itself ships no native artifact.

## Feature tests in `Pending/`

The coverage ledger (`src/Pacpar.Alpm/COMPLETENESS.md` §4) records the native APIs that landed
after the first implementation batch. Their acceptance tests live in `Pending/` and run with the
rest of the suite:

```bash
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj
```

Move a file from `Pending/` to `Unit/` once it no longer needs to be singled out.

## Running locally

The host must provide `libalpm`, `pkg-config`, Rust, a C compiler (`cc`, from `gcc` or `clang`), and the .NET SDK.

```bash
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj
```

## Running in an OCI container

Use `scripts/run-alpm-tests-in-container.sh`. The script auto-detects `podman`, `nerdctl`, or `docker`, and builds `containers/alpm-tests/Containerfile`.
