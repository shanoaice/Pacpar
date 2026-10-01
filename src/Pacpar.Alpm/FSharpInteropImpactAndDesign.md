# F# Interop Error Bridge: Impact and Preliminary Design

**Status:** preliminary design  
**Date:** 2026-10-01  
**Scope:** the C# changes needed to support the F# wrapper's `Result`/`option` error model

## 1. Decision Summary

The C# layer should expose an internal, failure-returning bridge to `Pacpar.Alpm.FSharp` through `InternalsVisibleTo`. Every operation that can report an expected ALPM operation failure gets a `TryX` method using `out value, out failure`; the existing public C# methods remain exception-based facades over the same implementation.

The bridge is not a raw-binding escape hatch. C# remains responsible for native return conventions, immediate errno capture, payload ownership, and native destructor selection. F# maps managed failure values into its operation-specific discriminated unions.

The error boundary is:

- Expected ALPM operation failures become F# `Result` errors.
- A completed lookup with no match remains `None` or an empty collection.
- `Remove` returning “not present” remains a successful `false` result.
- Argument errors, disposed objects, stale lifetime tokens, invalid state transitions, OOM, missing native error state, and cleanup/release failures remain exceptions.

The agreed shape is `TryX + out value + out failure`, not an `AlpmResult<T>` type. The failure contract uses stable `AlpmFailureCode` values plus a numeric `NativeCode` fallback. See [ADR 0001](../../docs/adr/0001-fsharp-error-boundary.md) and [ADR 0002](../../docs/adr/0002-stable-failure-codes-for-fsharp.md).

## 2. Project-Level Impact

| Project/file | Current state | Required change |
| --- | --- | --- |
| [`Pacpar.Alpm.csproj`](Pacpar.Alpm.csproj) | exposes internals to `Pacpar.Alpm.Tests` only | add `<InternalsVisibleTo Include="Pacpar.Alpm.FSharp" />` |
| [`Pacpar.Alpm.FSharp.fsproj`](../Pacpar.Alpm.FSharp/Pacpar.Alpm.FSharp.fsproj) | has no C# project reference | add a `ProjectReference` to `Pacpar.Alpm`; add an F# interop translation file before `Library.fs` |
| [`Pacpar.slnx`](../../Pacpar.slnx) | already includes both projects | no solution change expected |

The friend-assembly name must exactly match the F# assembly name. If either package is strong-named later, the `InternalsVisibleTo` entry must include the public key.

## 3. C# Code Impact

### 3.1 `Alpm.cs`

Add failure-returning core operations for:

- `Alpm` creation: `alpm_initialize` writes an out-error and returns a nullable handle.
- `LoadPackage`: return code plus handle errno.
- `GetLocalDatabase` and `GetSyncDatabases`: pointer result plus current handle errno.
- `RegisterSyncDatabase`: nullable database pointer plus current handle errno.
- `UnregisterAllSyncDatabases`: return code plus handle errno.

The public constructor and methods keep their signatures and exceptions. A static/internal creation path is needed because a C# constructor cannot return a failure value. The core must allocate and free the initialize out-error exactly once and must not expose `SafeAlpmHandle` to F#.

### 3.2 `Database.cs`

Add `Try*` paths for:

- `GetPackageCache`, `GetGroupCache`, `GetServers`, and `GetCacheServers`, preserving the rule that `null` plus `ALPM_ERR_OK` is an empty view.
- `Unregister`, which returns a native status code.
- `Validate`, which reports invalidity with a handle errno.

`GetPackage` and `GetGroup` remain lookup APIs returning nullable values. Their miss is a normal answer, not a failure. Read-only properties and `IsValid` remain ordinary values.

### 3.3 `Transaction.cs`

Add failure-returning operations for:

- transaction initialization;
- `AddPackage(PackageView)` and `AddPackage(LoadedPackage)`;
- `RemovePackage`;
- `SystemUpgrade`;
- `Interrupt`;
- `Prepare`;
- `Commit`.

`Prepare` and `Commit` must route their caller-owned output list through one failure-payload builder. The builder copies managed snapshots and frees each native list with the destructor required by the errno. `AlpmTransactionException` should become an adapter over that failure value, preserving its existing public payload properties and regression tests.

`Dispose`, `alpm_trans_release`, `alpm_release`, and other cleanup paths are not ROP operations. A failed release leaves native ownership uncertain and should surface as a cleanup/runtime exception rather than being converted into a normal `Result`.

### 3.4 Options and collections

`Options.cs`, `Options/AlpmOptionList.cs`, and the option subclasses (`Architecture`, `CacheDirectories`, `HookDirectories`, `OverwritableFiles`, `NoUpgrade`, `NoExtract`, `IgnorePackages`, `IgnoreGroups`, and `AssumeInstalled`) need core paths for:

- scalar option setters;
- list `Add`;
- list `Remove`.

`Remove` needs a three-state contract: operation succeeded and removed an item, operation succeeded and found no item, or operation failed. The bridge should expose `TryRemove(..., out bool removed, out AlpmFailure? failure)` so F# can return `Result<bool, _>` instead of treating a missing entry as an error.

Pure collection reads (`Count`, `Contains`, enumeration, `CopyTo`) do not need an ALPM failure channel. Argument validation and lifetime checks remain exceptions.

### 3.5 Callbacks

`Events/Callback.cs` should add failure-returning registration/unregistration paths for the event, fetch, question, download, progress, and log callbacks. These are configuration operations and libalpm can reject them.

Callback invocation is deliberately excluded. The existing `SafeInvoke` behavior already prevents user handler exceptions from crossing the FFI boundary; those exceptions are runtime/handler failures and must not be hidden inside `Result`.

### 3.6 Package access

`Package/PackageBase.GetSignature()` should expose a `Try*` path because `alpm_pkg_get_sig` reports a return code and handle errno. A successful call with no signature is `value == null`, not an error. The existing managed byte-array copy and native buffer release must remain in C#.

`GetRequiredBy`, `GetOptionalFor`, package property getters, `CheckMd5Sum`, `ShouldIgnore`, `HasScriptlet`, and `FindGroupPackages` do not currently expose a distinct ALPM failure signal and should remain ordinary values or nullable/empty results.

### 3.7 Error construction

`Exceptions/ErrorHandler.cs` and `Exceptions/AlpmTransactionException.cs` are the main refactoring points. The proposed internal split is:

```text
FailureFactory.FromErrno(...)
FailureFactory.TakeTransactionFailure(...)
ExceptionFactory.FromFailure(...)
```

`FailureFactory` owns the stable semantic code, native numeric code, description, context, and typed payload. `ExceptionFactory` preserves the existing C# exception hierarchy. No `Try*` facade should independently re-read or re-classify errno.

## 4. Bridge Contract

The bridge lives under `Pacpar.Alpm.Internal` and is available to F# through the friend assembly. It uses `Try*` methods with an explicit nullable failure output:

```csharp
internal static bool TryCreate(
  string root,
  string dbpath,
  out Alpm? value,
  out AlpmFailure? failure);

internal static bool TryLoad(
  Alpm session,
  string filename,
  bool full,
  SigLevel level,
  out LoadedPackage? value,
  out AlpmFailure? failure);

internal static bool TryPrepare(
  Transaction transaction,
  out AlpmFailure? failure);

internal static bool TryRemove(
  OptionList list,
  object item,
  out bool removed,
  out AlpmFailure? failure);
```

The Boolean means “the ALPM operation completed without an expected ALPM failure.” It does not mean “the optional value is non-null.” A lookup miss is therefore `true` with `value == null`; an ALPM failure is `false` with a populated `failure`.

`AlpmFailure` should be a managed value containing:

- `AlpmFailureCode Code` (stable semantic code);
- `uint NativeCode` (numeric libalpm code for diagnostics and unknown-code fallback);
- `Description` and optional operation `Context`;
- typed payload records for missing dependencies, conflicting dependencies, file conflicts, and invalid package names.

`ALPM_ERR_MEMORY` is not an ordinary bridge failure: it is surfaced as `OutOfMemoryException`. An unknown but valid native code maps to `UnknownNative`, preserving its numeric value and message.

## 5. Native Signal Mapping

| Signal family | Core interpretation | F# result |
| --- | --- | --- |
| initialization out-errno and null handle | failure with the out-error | `Result` error |
| return code plus handle errno | non-zero is an expected ALPM failure | `Result` error |
| pointer result plus current errno | null plus non-OK errno is failure | `Result` error |
| null pointer plus `ALPM_ERR_OK` | empty value, not failure | empty collection / `None` |
| nullable lookup result | no match is a completed lookup | `option` |
| remove result `1/0/-1` | removed / not present / failure | `Result<bool, _>` |
| managed state or lifetime check | programming or runtime condition | exception |

The core must read handle errno immediately after the relevant native call, before another libalpm call can overwrite state. The generated binding type `_alpm_errno_t` stays internal to C#; F# sees only the stable failure code and managed payload.

## 6. Compatibility and Risk

### Compatibility

- Existing public C# method and property signatures remain unchanged.
- Existing public exceptions and payload properties remain available.
- `AlpmTransactionException.TakeFailure` can remain as a compatibility shim while tests migrate to the shared failure builder.
- The public F# surface must not expose raw pointers, `SafeAlpmHandle`, `Lifetime`, or `_alpm_errno_t`.

### Risks

1. `InternalsVisibleTo` grants access to all internals, not only the bridge. Keep F# references confined to `Pacpar.Alpm.Internal` and review that boundary explicitly.
2. A stable `AlpmFailureCode` mapping must be tested for every known generated errno, with a deliberate `UnknownNative` fallback.
3. Transaction payload ownership is the highest-risk path. Wrong element destructors can abort the process, so the builder must remain centralized and covered by the existing real transaction tests.
4. `Try*` signatures need a documented rule for nullable outputs and `removed == false`; otherwise F# translation can accidentally turn normal absence into an error.
5. Cleanup failures must not be swallowed by an F# bracket. Preserve exceptions or add a separate, explicit fatal cleanup report later.

## 7. Preliminary Implementation Order

1. Add the project reference and F# friend-assembly entry.
2. Add `AlpmFailureCode`, `AlpmFailure` payload records, `FailureFactory`, and `ExceptionFactory` with exhaustive mapping tests.
3. Refactor transaction failure consumption first, because it owns the most dangerous native memory contract.
4. Add `Try*` cores and delegate the existing C# facades to them for session, package, database, options, callback, and transaction groups.
5. Add one F# interop translation module, then expose operation-specific F# error unions above it.
6. Add C# facade/core parity tests and F# `Result`/`option`/`Result<bool, _>` tests.
7. Run host tests with `dotnet test --filter "FullyQualifiedName!~Integration"`. Use BenchmarkDotNet only for any claimed performance benefit; do not use stopwatch measurements.

## 8. Non-Goals

This bridge does not redesign callbacks, event streaming, transaction ownership, or lifetime tokens. It does not make every native call return `Result`, and it does not turn caller mistakes or cleanup failures into business errors. The immediate goal is one consistent C# failure boundary that F# can translate without writing native errno or destructor logic.

## References

- [F# wrapper design](../Pacpar.Alpm.FSharp/DESIGN.md)
- [Glossary](../../GLOSSARY.md)
- [ADR 0001: failure boundary](../../docs/adr/0001-fsharp-error-boundary.md)
- [ADR 0002: stable failure codes](../../docs/adr/0002-stable-failure-codes-for-fsharp.md)
