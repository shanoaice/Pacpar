# Pacpar.Alpm.Tests

This project contains the C# test harness for `Pacpar.Alpm`.

## Test layout

- `Unit/` contains managed-only tests that do not require an initialized libalpm handle.
- `Integration/` contains isolated libalpm tests that exercise the native binding against a temporary pacman database layout.
- `Fixtures/` contains reusable environment setup that can also be shared by future F# tests.
- `native/` contains a C caller for the `va_list` forwarding test, compiled by this project into
  its intermediate output. It is test-only: `Pacpar.Alpm` itself ships no native artifact.

## Running locally

The host must provide `libalpm`, `pkg-config`, Rust, a C compiler (`cc`, from `gcc` or `clang`), and the .NET SDK.

```bash
dotnet test src/Pacpar.Alpm.Tests/Pacpar.Alpm.Tests.csproj
```

## Running in an OCI container

Use `scripts/run-alpm-tests-in-container.sh`. The script auto-detects `podman`, `nerdctl`, or `docker`, and builds `containers/alpm-tests/Containerfile`.
