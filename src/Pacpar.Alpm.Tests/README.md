# Pacpar.Alpm.Tests

This project contains the C# test harness for `Pacpar.Alpm`.

## Test layout

- `Unit/` contains managed-only tests that do not require an initialized libalpm handle.
- `Integration/` contains isolated libalpm tests that exercise the native binding against a temporary pacman database layout.
- `Fixtures/` contains reusable environment setup that can also be shared by future F# tests.
- `Pending/` contains the specification tests for features that are not implemented yet (report item
  M). They are excluded from the build by default, see below.
- `native/` contains a C caller for the `va_list` forwarding test, compiled by this project into
  its intermediate output. It is test-only: `Pacpar.Alpm` itself ships no native artifact.

## Pending feature tests

`Pending/` describes the API of the features listed in the implementation guide
(`.dsh-scratch/missing-features-guide.md`). Those members do not exist yet, so the folder is kept
out of the build:

```bash
# The current, building test set:
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj --filter "Category!=Integration"

# Turn the specification tests on. Until the API exists this fails to compile, and the compiler
# errors are the to-do list; once it exists they are the acceptance criteria.
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj -p:PacparPendingFeatureTests=true --filter "Category!=Integration"
```

When a feature lands, move its file from `Pending/` to `Unit/`.

## Running locally

The host must provide `libalpm`, `pkg-config`, Rust, a C compiler (`cc`, from `gcc` or `clang`), and the .NET SDK.

```bash
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj
```

## Running in an OCI container

Use `scripts/run-alpm-tests-in-container.sh`. The script auto-detects `podman`, `nerdctl`, or `docker`, and builds `containers/alpm-tests/Containerfile`.
