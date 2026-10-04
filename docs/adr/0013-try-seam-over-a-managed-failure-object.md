# The failure-returning core is a `Try*` seam over a managed failure object

---
status: proposed
---

`Pacpar.Alpm` grows an **internal** `Try*` seam. Every public operation that can end in an
ALPM operation failure gets an internal twin with the BCL `Try` shape: a `bool` answer, the result
as an `out` parameter, and the failure as the trailing `out` parameter of type `AlpmFailure`. The
public throwing API becomes a one-line wrapper over that seam, and `Pacpar.Alpm.FSharp` calls the
seam directly and maps `AlpmFailure` into operation-specific unions.

`AlpmFailure` is a managed value, not an exception. It carries a stable `AlpmFailureCode`, the raw
numeric native code, libalpm's own message, and a typed failure detail when libalpm dumped one. The
public exception hierarchy stays the throwing surface and becomes a projection of the failure,
produced by `AlpmFailure.ToException(string?)`.

## The shape

### The `Try` signature

```csharp
// Pacpar.Alpm.Alpm
internal bool TryLoadPackage(string filename, bool full, SigLevel level,
                             [NotNullWhen(true)] out LoadedPackage? package,
                             [NotNullWhen(false)] out AlpmFailure? failure);

// Pacpar.Alpm.Transaction
internal bool TryPrepare([NotNullWhen(false)] out AlpmFailure? failure);
```

Four rules fix the contract:

1. **`false` means an ALPM operation failure.** `failure` is then non-null and `package` is `null`.
2. **`true` means the call succeeded.** `failure` is then `null`. A `null` result on `true` is a
   query miss, never a failure — three states, one `bool` and one nullable result.
3. **The result comes first, the failure last**, as in every BCL `Try` overload that reports a
   reason.
4. **The annotations carry the contract.** `[NotNullWhen(false)] out AlpmFailure?` lets the wrapper
   write `throw failure.ToException(...)` with no `!`.

The public wrapper is one line:

```csharp
public void Prepare()
{
  if (!TryPrepare(out var failure)) throw failure.ToException("Failed to prepare transaction");
}
```

### The failure object

```csharp
internal sealed class AlpmFailure
{
  /// The stable semantic code. This is what the F# layer matches on.
  internal AlpmFailureCode Code { get; }

  /// libalpm's own pm_errno, kept for diagnostics and forward compatibility (ADR 0009).
  internal int NativeCode { get; }

  /// libalpm's own description of NativeCode (alpm_strerror). Never null; not stable text.
  internal string? Message { get; }

  /// Structured evidence libalpm dumped, such as missing dependencies. Null when it dumped none.
  internal AlpmFailureDetail? Detail { get; }

  /// The public exception this failure is reported as. Reads no native memory but alpm_strerror.
  internal Exception ToException(string? context = null);

  /// The failure for a call that signalled failure through its return value. No payload.
  internal static AlpmFailure Of(int errno);

  /// The failure for a failed prepare/commit. Always consumes data.
  internal static unsafe AlpmFailure Take(int errno, _alpm_list_t* data);
}
```

`AlpmFailure` carries no operation context on purpose: one failure can be reported by two facades,
and each renders its own. Context is passed to `ToException` at the throw site.

Two invariants hold for every instance: `NativeCode` is neither `0` nor `ALPM_ERR_MEMORY`, because
both are thrown rather than returned (see below).

### The failure code

`AlpmFailureCode` is the project's own vocabulary, one member per errno meaning. It is the stable
half of ADR 0003: libalpm may add errnos, which land on `UnknownNative` without breaking anything,
but a meaning the project already named keeps its code. The generated `_alpm_errno_t` stays internal
(ADR 0006) and never reaches a public signature.

| Code | errno | Reported as |
|---|---|---|
| `DatabaseOpen`, `DatabaseCreate`, `DatabaseNull`, `DatabaseNotNull`, `DatabaseNotFound`, `DatabaseInvalid`, `DatabaseInvalidSignature`, `DatabaseVersion`, `DatabaseWrite`, `DatabaseRemove` | `ALPM_ERR_DB_*` | `AlpmDatabaseException` |
| `PackageNotFound`, `PackageIgnored`, `PackageMissingSignature`, `PackageOpen`, `PackageCannotRemove`, `PackageInvalidName` | `ALPM_ERR_PKG_NOT_FOUND`, `_IGNORED`, `_MISSING_SIG`, `_OPEN`, `_CANT_REMOVE`, `_INVALID_NAME` | `AlpmPackageException` |
| `TransactionNotNull`, `TransactionNull`, `DuplicateTarget`, `DuplicateFileName`, `TransactionNotInitialized`, `TransactionNotPrepared`, `TransactionAbort`, `TransactionType`, `TransactionNotLocked`, `HookFailed` | `ALPM_ERR_TRANS_*` | `AlpmTransactionException` (base state) |
| `UnsatisfiedDependencies` | `ALPM_ERR_UNSATISFIED_DEPS` | `MissingDependencies` case, with detail |
| `ConflictingDependencies` | `ALPM_ERR_CONFLICTING_DEPS` | `ConflictingDependencies` case, with detail |
| `FileConflicts` | `ALPM_ERR_FILE_CONFLICTS` | `ConflictingFiles` case, with detail |
| `PackageInvalid`, `PackageInvalidChecksum`, `PackageInvalidSignature`, `PackageInvalidArchitecture` | `ALPM_ERR_PKG_INVALID`, `_INVALID_CHECKSUM`, `_INVALID_SIG`, `_INVALID_ARCH` | `InvalidPackage*` case when a payload was dumped, base state when it was not (ADR 0007) |
| `SignatureMissing`, `SignatureInvalid`, `MissingCapabilitySignatures` | `ALPM_ERR_SIG_*`, `ALPM_ERR_MISSING_CAPABILITY_SIGNATURES` | `AlpmSignatureException` |
| `RetrievePrepare`, `Retrieve`, `LibCurl`, `ExternalDownload` | `ALPM_ERR_RETRIEVE*`, `_LIBCURL`, `_EXTERNAL_DOWNLOAD` | `AlpmRetrieveException` |
| `BadPermissions`, `System`, `NotAFile`, `NotADirectory`, `WrongArguments`, `DiskSpace`, `HandleNull`, `HandleNotNull`, `HandleLock`, `ServerBadUrl`, `ServerNone`, `InvalidRegex`, `LibArchive`, `Gpgme` | the ungrouped errnos | `AlpmException` |
| `UnknownNative` | any valid errno the mapping does not know | `AlpmException` |

Two switches produce the code and the category: `ErrorHandler.CodeOf(int)` maps an errno to a code
and falls back to `UnknownNative`; `ErrorHandler.CategoryOf(AlpmFailureCode)` maps a code to the
`AlpmErrorCategory` that picks the exception type. `ErrorHandler.Categorize` disappears, so the
category is derived from the code rather than written a second time.

### The failure detail

```csharp
internal abstract class AlpmFailureDetail
{
  internal sealed class MissingDependencies(IReadOnlyList<DepMissing> dependencies);
  internal sealed class ConflictingDependencies(IReadOnlyList<Conflict> conflicts);
  internal sealed class ConflictingFiles(IReadOnlyList<FileConflict> conflicts);

  /// The package names libalpm rejected. Code says why:
  /// invalid, checksum, signature or architecture.
  internal sealed class RejectedPackages(IReadOnlyList<string> packages);
}
```

Nesting mirrors the exception hierarchy, so the payload case and the exception case that carries it
read the same at the call site.

The elements are the same managed snapshots the public exceptions carry today, and they are public
types — `DepMissing`, `Conflict`, `FileConflict` already are. `AlpmFailure.Take` is the only
consumer of a native payload list, and it frees that list with the destructor the errno requires,
exactly as `AlpmTransactionException.TakeFailure` does now.

## Which operations get a `Try` twin

Only operations whose failure libalpm reports through a return value or through an errno an entry
point resets on entry (ADR 0012). Each twin sits directly above the public method it feeds.

| Type | `Try` twin |
|---|---|
| `Alpm` | `TryCreate(string root, string dbpath, out Alpm? session, out AlpmFailure? failure)` (static) |
| | `TryLoadPackage(string filename, bool full, SigLevel level, out LoadedPackage? package, out AlpmFailure? failure)` |
| | `TryBeginTransaction(TransactionFlags flags, out Transaction? transaction, out AlpmFailure? failure)` |
| | `TryRegisterSyncDatabase(string treename, SigLevel level, out Database? database, out AlpmFailure? failure)` |
| | `TryUnregisterAllSyncDatabases(out AlpmFailure? failure)` |
| `Database` | `TryGetPackageCache(out AlpmList<PackageView>? cache, out AlpmFailure? failure)` |
| | `TryGetGroupCache(out AlpmList<Group>? cache, out AlpmFailure? failure)` |
| | `TryUnregister(out AlpmFailure? failure)` |
| | `TryValidate(out AlpmFailure? failure)` |
| `Transaction` | `TryPrepare(out AlpmFailure? failure)` |
| | `TryCommit(out AlpmFailure? failure)` |
| | `TryAddPackage(PackageView pkg, out AlpmFailure? failure)` |
| | `TryAddPackage(LoadedPackage pkg, out PackageView? view, out AlpmFailure? failure)` |
| | `TryRemovePackage(PackageView pkg, out AlpmFailure? failure)` |
| | `TrySystemUpgrade(bool enableDowngrade, out AlpmFailure? failure)` |
| | `TryInterrupt(out AlpmFailure? failure)` |

`TryCreate` exists because a constructor cannot fail without throwing, and F# needs a session
without a `try`/`catch`. It does the native initialize and calls a private constructor; the public
constructor delegates to the same initialize and throws when it produced a failure.

No twin for `GetLocalDatabase`, `GetSyncDatabases`, `GetPackage`, `GetGroup`, `GetServers`,
`GetCacheServers`, `GetAddedPackages`, `GetRemovedPackages`, `GetFlags`, `IsValid`, and the option
getters: none of them has a failure signal the ADR 0012 discipline allows us to read. `GetPackage`
and `GetGroup` keep answering a query miss with `null`.

**No twin for teardown either.** `Dispose` and `Transaction.ReleaseFailure` keep the shape ADR 0008
gave them, and this seam does not touch it. F# has `use`, `using` and brackets of its own, so it
releases a transaction the same way C# does and reads `IsReleased` and `ReleaseFailure` in the
bracket's `finally`. There is nothing to gain from a `TryRelease`: the cleanup outcome is already
exposed rather than thrown, which is the only thing a `Try` twin would add. The cost is written down
below.

`TryAddPackage(LoadedPackage)` moves ownership only on success. On `false` the loaded package is
still the caller's to dispose, so the F# layer must not treat a failed add as a hand-over.

## What a `Try` twin still throws

The seam narrows one thing — the ALPM operation failure. Everything else keeps throwing, from both
facades, through the same guards the public methods run today:

- argument misuse (`ArgumentNullException`, `ArgumentException`);
- a stale lifetime stamp (`AlpmLifetimeException`);
- a disposed session, transaction or package (`ObjectDisposedException`);
- a request for transaction flags that differ from the active transaction
  (`InvalidOperationException`);
- `ALPM_ERR_MEMORY`, which stays an `OutOfMemoryException` (ADR 0002);
- a failure libalpm reported without setting an errno (`AlpmNativeFailureException`);
- any other runtime fault.

So a `Try` twin is not a `catch`-all. `NativeCall.Failure` returns an `AlpmFailure` and throws these
last two instead of answering with a value.

## Considered Options

**A result struct (`AlpmResult<T>`) instead of `bool` plus `out` parameters.** It composes well and
maps to F# `Result` with one adapter. Rejected: it is a third vocabulary beside the exception
hierarchy and the failure object, and it buys the F# layer nothing, because F# already tuplifies
`out` parameters — `let ok, failure = tx.TryPrepare()` needs no adapter type. The BCL `Try` shape is
also what a C# reader expects from the name.

**A public `Try` API.** Rejected by ADR 0002: it doubles the supported public surface before there
is evidence a C# consumer wants it. The seam is internal and the F# assembly reaches it through
`InternalsVisibleTo`, which ADR 0005 makes permanent.

**Making `AlpmFailure` an `Exception` subclass that is simply not thrown.** Rejected: exceptions
carry a stack trace and a `throw` identity they would never use, and F# would then pattern-match a
CLR type hierarchy instead of data. Keeping the failure as data and the exception as a projection
makes the direction of the dependency explicit: `ToException` is the only way one becomes the other.

**Mapping errnos in F#.** Rejected by ADR 0002 and ADR 0003: native semantics and payload
ownership stay in C#.

## Consequences

**One core, two facades.** Every public method that can fail is `if (!TryX(...)) throw
failure.ToException(context);`, so the exception path and the `Result` path cannot drift. The
performance claim behind ADR 0002 still has to be measured: add a BenchmarkDotNet benchmark over a
host-safe expected-failure workload — register a sync database and call `GetPackageCache`, which
libalpm fails while setting an errno — comparing `try`/`catch` against `TryGetPackageCache`.

**Payload ownership has one home.** `AlpmTransactionException.TakeFailure` becomes
`AlpmFailure.Take`. The four payload-carrying exception cases are still the public form of those
errnos (ADR 0007) but are now constructed in one place, `ToException`.

**Teardown stays outside the seam, and that has one price.** `Transaction.ReleaseFailure` remains
an `Exception?`, and the F# bracket classifies a cleanup failure by matching that exception's CLR
type instead of an `AlpmFailureCode`. ADR 0007 makes that matching stable — one errno has one public
exception type — but it is not the same contract as the rest of the seam, and for the errnos that
share the `AlpmException` base type the bracket has to read `Errno` to tell them apart. We accept it:
it is one place, on a path that runs once per transaction, and the alternative is a change to a
teardown design ADR 0008 already settled. Should it ever hurt, the narrow fix is an internal
read-only projection `AlpmFailure.From(AlpmException)` for the bracket to call, decided on its own.

**The GC-anchor rule gets simpler, and gains one invariant.** The sequence "read the errno, read the
payload, anchor, then build the failure" now happens once per operation inside the twin, and the
public wrapper throws a value that reads nothing native. `FindPointerCallsInsideThrowExpressions`
therefore has fewer sites to police. The new invariant: after the twin's `GC.KeepAlive` calls,
failure construction may read only `alpm_strerror`, which is a table lookup that never touches the
handle. Anything else there would invalidate the anchor.

**`Pacpar.Alpm.FSharp` must be added to `InternalsVisibleTo`.** It is not there today; only
`Pacpar.Alpm.Tests` and `Pacpar.Benchmarks` are. The two packages keep shipping together (ADR 0005).

**Tests to add.** `CodeOf` knows every errno the bindings know, replacing
`Categorize_ClassifiesEveryKnownErrno`; for every known errno, `ToException` built from
`CodeOf(errno)` is the type the old `Categorize` table named, which pins ADR 0007 through the new
path; every `Try` twin sets `failure` to `null` on `true` and the result to `null` on `false`; a
failed `TryPrepare` carries a `MissingDependencies` detail and frees the native list; a failed
`TryAddPackage(LoadedPackage)` leaves the package owned.

**Not decided here.** The F# unions themselves, and whether `AlpmFailureCode` ever becomes public.
It starts internal, as ADR 0006 asks.

## See also

- [ADR 0002: the failure boundary](./0002-fsharp-error-boundary.md)
- [ADR 0003: stable failure codes](./0003-stable-failure-codes-for-fsharp.md)
- [ADR 0007: one errno, one public exception type](./0007-one-errno-one-public-exception-type.md)
- [ADR 0008: cleanup failures are exposed](./0008-cleanup-failures-are-exposed-not-thrown.md)
- [ADR 0012: when errno may be read](./0012-errno-is-read-only-after-a-failing-return.md)
