# Pacpar.Alpm.FSharp Design Document

This document outlines the architectural decisions, design choices, and proposed interface shapes for the functional-first F# binding around `Pacpar.Alpm`.

---

## 1. Core Philosophy & Architectural Goals

1. **Functional-First Architecture**:
   - Separate **specification (description)** from **execution (interpretation)**.
   - Represent processes (transactions, queries, configurations) as pure, immutable data structures where possible.
   - Use Discriminated Unions and `Result<'T, 'Error>` (Railway-Oriented Programming) for expected ALPM operation failures. Use `option` or empty collections for normal query misses, and exceptions for contract violations and runtime faults.

2. **Zero-Overhead & High Performance**:
   - `libalpm` queries (such as scanning package caches or file lists) can involve thousands of packages and hundreds of thousands of files.
   - Prefer zero-copy borrowed **views** (`PackageView`) over eager copies (`PackageSnapshot`) for query pipelines.
   - Preserve lazy streaming for expensive collections (e.g. file lists, changelogs).

3. **Safe Concurrency & Lifetime Encapsulation**:
   - Encapsulate native resource lifetimes (`alpm_handle_t`, `alpm_trans_t`, `db.lck`) within deterministic scopes (bracket patterns) so that consumers cannot accidentally leak locks or invoke operations on dead pointers.

4. **Safety in Interactivity**:
   - Critical operations (package replacement, conflict resolution, provider selection) must never be blindly defaulted or bypassed without explicit consumer intent.

---

## 2. Declarative Transactions & Phased Execution

Pacman executes transactions in three distinct, observable phases:

```
[TransactionPlan]
       │
       ▼
 1. Prepare Phase      (Resolve targets, add/remove, alpm_trans_prepare)
       │
       ▼
 2. Prepared State     (Holds db.lck; computes TransactionSummary)
       │
       ├─► [User / CLI Confirmation]  <--- Prompt "Proceed with installation? [Y/n]"
       │        │
       │        └─► Abort / Cancel ──► Cleanly release db.lck with zero disk mutations
       │
       ▼ Confirmed
 3. Commit Phase       (alpm_trans_commit, download, filesystem update, hooks)
       │
       ▼
 [TransactionReport]   (Final outcomes, installed packages, execution metrics)
```

### 2.1 The Transaction Plan Domain Model

A plan is pure, un-executed data describing what should be done:

```fsharp
namespace Pacpar.Alpm.FSharp

open Pacpar.Alpm

type PackageTarget =
    | Sync of name: string * repository: string option
    | LocalFile of path: string * signatureCheck: SigLevel option
    | View of PackageView

type PlanAction =
    | Install of PackageTarget
    | Remove of packageName: string
    | SysUpgrade of allowDowngrade: bool

type TransactionPlan = {
    Actions: PlanAction list
    Flags: TransactionFlags
    QuestionHandler: QuestionHandler
}
```

### 2.2 The Phased Execution API

```fsharp
/// Pure summary of an already-prepared transaction.
type TransactionSummary = {
    PackagesToInstall: PackageView list
    PackagesToRemove: PackageView list
    TotalDownloadSize: int64
    TotalInstallSizeChange: int64
}

/// An active transaction that has successfully finished 'prepare' and holds db.lck.
type PreparedTransaction =
    interface System.IDisposable
    abstract member Summary: TransactionSummary
    abstract member Commit: unit -> Result<TransactionReport, TransactionError>
    abstract member Abort: unit -> unit

module Transaction =
    /// Phase 1: Begins the native transaction, registers targets, and runs alpm_trans_prepare.
    val prepare: AlpmSession -> TransactionPlan -> Result<PreparedTransaction, PrepareError>

    /// Phase 2: Commits changes to disk.
    val commit: PreparedTransaction -> Result<TransactionReport, TransactionError>

    /// Higher-order bracket guaranteeing that the lock is released whether confirmed, aborted, or faulted.
    val withPrepared:
        AlpmSession
        -> TransactionPlan
        -> (PreparedTransaction -> Result<'a, TransactionError>)
        -> Result<'a, TransactionError>
```

---

## 3. Safe Interactive Question Handling

During `prepare` and `commit`, `libalpm` fires mid-computation questions. Carelessly defaulting these questions can lead to severe system corruption (e.g. accidentally agreeing to replace core system packages or picking incorrect virtual packages).

### 3.1 Domain-Specific Question Model

Each question carries rich context rather than raw strings:

```fsharp
type ConflictInfo = {
    Package1: PackageView
    Package2: PackageView
    Reason: Depend
}

type ProviderSelection = {
    Providers: PackageView list
    Dependency: Depend
}

type Question =
    | SelectProvider of ProviderSelection
    | ReplacePackage of oldPkg: PackageView * newDb: Database * newPkg: PackageView
    | ConflictPackage of ConflictInfo
    | InstallIgnored of package: PackageView
    | CorruptedPackage of filePath: string
    | RemovePackages of packages: PackageView list
    | ImportKey of keyUid: string * fingerprint: string
```

### 3.2 Question Handlers

Instead of a lossy bool policy, we offer typed responses or a handler interface:

```fsharp
type QuestionHandler =
    /// Full custom handling per question case.
    | Custom of (Question -> QuestionAnswer)
    /// Explicit interactive console/UI prompter.
    | Interactive of InteractivePrompter
    /// Strict non-interactive mode: fail transaction on ambiguous choices (e.g., CI/CD).
    | StrictNonInteractive
    /// Built-in defaults (mirrors pacman's defaults when --noconfirm is passed).
    | LibalpmDefault
```

---

## 4. Performance & Lifetime Management: Views vs. Snapshots

### 4.1 The Core Dilemma
- `Pacpar.Alpm.PackageView` is a direct view over `libalpm` memory. It is extremely fast (zero-copy), but invalid after transaction commits or handle disposal.
- `Pacpar.Alpm.PackageSnapshot` copies all fields into managed objects. While safe, copying large collections (especially file lists `pkg.Files`) incurs a ~27x performance overhead compared to lazy scans.

### 4.2 The F# Design Choice: "Views by Default, Snapshot on Demand"
1. **Views for Query and Processing Pipelines**:
   - F# functions operate directly on `PackageView`.
   - Collections like `Database.packages` return `seq<PackageView>`.
   - File lists are exposed as lazy `seq<PackageFile>` rather than eagerly pre-allocated string arrays.

2. **Lifetimes Anchored to Session Brackets**:
   - All `PackageView` instances are valid within the scope of an `Alpm.withSession` block:
     ```fsharp
     Alpm.withSession config (fun session ->
         let localDb = Database.local session
         let largePackages =
             Database.packages localDb
             |> Seq.filter (fun p -> p.InstalledSize > 100_000_000L)
             |> Seq.map (fun p -> p.Name, p.InstalledSize)
             |> Seq.toList
         // largePackages consists of plain strings and ints, safe to return outside the session
         largePackages
     )
     ```

3. **Explicit Snapshot Boundary**:
   - Provide `Package.snapshot: includeFiles: bool -> PackageView -> PackageSnapshot`.
   - If a caller specifically needs to retain package objects across a transaction commit (which invalidates local database caches), they explicitly take a snapshot.

---

## 5. Query Interface (`pacman -Q` / `-Ss` Ergonomics)

Query functions are modeled as composable pipeline operations over databases:

```fsharp
[<RequireQualifiedAccess>]
module Database =
    val local: AlpmSession -> Database
    val sync: name: string -> AlpmSession -> Database option
    val allSync: AlpmSession -> Database list

    val find: name: string -> Database -> PackageView option
    val packages: Database -> seq<PackageView>
    val search: terms: string list -> Database -> seq<PackageView>

[<RequireQualifiedAccess>]
module Package =
    val name: PackageBase -> string
    val version: PackageBase -> PackageVersion
    val description: PackageBase -> string option
    val size: PackageBase -> int64
    val files: PackageBase -> seq<PackageFile>
    val snapshot: includeFiles: bool -> PackageBase -> PackageSnapshot
```

### Example: Searching and Filtering in F#
```fsharp
let findInstalledOrphans (session: AlpmSession) =
    Database.local session
    |> Database.packages
    |> Seq.filter (fun p -> p.Reason = PackageReason.Depend)
    |> Seq.filter (fun p -> (Package.requiredBy p session).IsEmpty)
    |> Seq.map Package.name
    |> Seq.toList
```

---

## 6. Event Processing & Progress Reporting

`libalpm` emits several categories of notifications:
1. **Lifecycle Events** (`alpm_event_t`): Low frequency (e.g. hook start/done, transaction start/done, package install start/done).
2. **Progress Alerts** (`alpm_progress_t`): High frequency (percentage completion, items processed during download, conflicts check, integrity verify).
3. **Log Messages** (`alpm_cb_log`): Diagnostics filtered by `LogLevel` (Error, Warning, Debug, Function).
4. **Download Events** (`alpm_cb_download`): Internal libcurl transfer ticks.

### 6.1 Structural Choices & Trade-offs

| Pattern | Mental Model | Pros | Cons |
| :--- | :--- | :--- | :--- |
| **Choice A: Synchronous Callback Record (Direct Sink)** | Record of function handlers passed to the session or plan | - Zero allocation & zero latency<br>- Direct terminal progress updates without thread-switching<br>- Unused callbacks register null natively | - Runs synchronously on ALPM worker thread; slow handlers block libalpm<br>- Less composable with functional streams |
| **Choice B: Reactive / Observable Streams (`IObservable`)** | Stream of events via Rx or F# observables | - Highly composable (`filter`, `throttle`, `sample`)<br>- Clean separation of producer and consumer<br>- Great for GUIs (Avalonia/Elmish) | - High frequency progress ticks allocate event objects<br>- Thread marshaling needed to avoid flooding UI dispatchers |
| **Choice C: Async Message Passing (`Channel` / `MailboxProcessor`)** | Bounded/unbounded async channel of a unified `TransactionEvent` DU | - Fully isolates ALPM from consumer crashes/delays<br>- Natural async consumption (`channel.Reader.ReadAllAsync()`) | - Adds task scheduling latency<br>- Queue pressure on high-frequency progress ticks unless conflated |

### 6.2 Proposed Multi-Tiered Architecture

We propose a **multi-tiered model** where Choice A serves as the zero-cost foundation, with built-in adapters for Choice B and C:

```fsharp
type ProgressInfo = {
    Type: ProgressType
    PackageName: string
    Percent: int
    Current: int64
    Total: int64
}

type EventSink = {
    OnEvent: (AlpmEvent -> unit) option
    OnProgress: (ProgressInfo -> unit) option
    OnLog: (LogLevel -> string -> unit) option
    OnDownload: (DownloadProgress -> unit) option
}

module EventSink =
    /// Zero-cost default with no listeners (libalpm delegates unregistered).
    val empty: EventSink

    /// Built-in console progress adapter for CLI tools.
    val consoleProgress: EventSink

    /// Converts the sink into an asynchronous ChannelReader for async consumers.
    val toChannel: capacity: int option -> EventSink * System.Threading.Channels.ChannelReader<TransactionEvent>

    /// Converts the sink into an IObservable for reactive/GUI consumers.
    val toObservable: unit -> EventSink * System.IObservable<TransactionEvent>
```

### 6.3 High-Performance Gadgets for Observables and Channels

#### A. Taming `IObservable` GC Pressure
1. **Value Records (`[<Struct>]`)**:
   Define `ProgressInfo` and event payloads as F# `[<Struct>]` records or Struct DUs. Because `IObserver<T>.OnNext(T value)` is generic, passing a struct avoids heap boxing entirely.
2. **Source Conflation & Gating**:
   Filter high-frequency progress ticks *at the source* before publishing to the observable pipeline:
   - Percentage change gate: only fire if `percent != lastPercent`.
   - Time-delta throttle: only fire if elapsed time since last tick exceeds a threshold (e.g. 50ms via `Stopwatch.GetTimestamp()`).
   This eliminates ~95% of event dispatches with zero loss in visual smoothness.
3. **ArrayPool / Buffer Pooling**:
   For log messages or custom download byte chunks, leverage `ArrayPool<byte>.Shared` to avoid allocating intermediate arrays across native boundaries.

#### B. Taming Async Channeling Downsides
1. **The Backpressure Dilemma**:
   - `libalpm` cannot be blocked on write: an asynchronous `WaitToWriteAsync()` would stall the native C thread.
   - Using an unbounded channel risks unbounded memory growth during heavy downloads.
   - Using a naive bounded channel with `DropOldest` risks dropping crucial lifecycle events (e.g., `HookDone` or `TransactionDone`).
2. **Channel Splitting / Conflation Architecture**:
   - **Critical Event Channel**: Unbounded channel dedicated strictly to low-frequency lifecycle events (`AlpmEvent`). Guaranteed delivery, near-zero memory footprint.
   - **Progress Telemetry**: Handled via a bounded channel with `Capacity = 1` and `BoundedChannelFullMode.DropOldest` (or an atomic `Volatile.Write` state cell). The reader only ever reads the latest progress state.
3. **Lock-Free Ring Buffers**:
   Configure channels with `SingleWriter = true` and `SingleReader = true`. This instructs `System.Threading.Channels` to bypass general concurrent synchronization and use high-speed lock-free circular buffers without internal node allocations.

---

## 7. Concurrency, Multi-Threaded Downloads & UI Architecture

Building a concurrent pacman alternative with rich visuals and multi-threaded downloads introduces specific constraints and interface requirements.

### 7.1 The Thread-Affinity of `libalpm`
- `libalpm` is **not thread-safe**. An `alpm_handle_t` and active transactions cannot be called concurrently from multiple OS threads.
- It holds an exclusive filesystem lock (`/var/lib/pacman/db.lck`).
- **Design Rule**: The F# library should treat `AlpmSession` and `PreparedTransaction` as thread-affined. A concurrent application should host the ALPM engine on a single dedicated worker thread / actor, while UI and networking run on independent threads.

### 7.2 Two Approaches to Concurrent Downloads

| Model | Mechanism | Interface Requirements in F# |
| :--- | :--- | :--- |
| **Model A: Native Parallel Downloads** | Configures `AlpmOptions.ParallelDownloads = N`. `libalpm` uses its internal `libcurl` multi-handle during `trans.Commit()`. | Must multiplex `DownloadEvent` by `filename: string` so the UI can track concurrent transfer bars. |
| **Model B: Custom Managed Multi-Threaded Downloader** | Bypasses `libcurl`. Downloads packages concurrently using .NET `HttpClient` (HTTP/2, HTTP/3, chunking, mirror racing) into pacman's cache directory before calling `trans.Commit()`. | Must expose URL resolution (`Database.servers`), target package filenames, and cache verification on `PreparedTransaction`. |

### 7.3 Supporting Custom Managed Downloader (Model B)
When pre-downloading packages into the cache directory, `libalpm` skips downloading during `trans.Commit()` if the package file and valid signature already exist in `/var/cache/pacman/pkg`.

To make this seamless, the F# interface exposes download targets on `PreparedTransaction`:

```fsharp
type DownloadTarget = {
    PackageName: string
    FileName: string
    Repository: string
    ServerUrls: string list
    ExpectedSize: int64
    Sha256Sum: string option
    CachePath: string
}

module Transaction =
    /// Evaluates which packages in the prepared transaction need downloading (omits already-cached, valid packages).
    val pendingDownloads: PreparedTransaction -> DownloadTarget list
```

### 7.4 Multi-Bar UI State Tracking
During parallel downloads (whether via Model A or Model B), multiple files transfer simultaneously. Pacman manages this via `struct pacman_multibar_ui`.

In F#, we can provide an immutable snapshot aggregator (`DownloadSessionState`) that folds download events into a clean state:

```fsharp
type FileTransferStatus =
    | Connecting
    | Transferring of downloaded: int64 * total: int64 * rateBytesPerSec: int64
    | Retrying of resume: bool
    | Completed of success: bool

type DownloadSessionState = {
    ActiveTransfers: Map<string, FileTransferStatus>
    CompletedTransfers: int
    TotalTransfers: int
    TotalDownloadedBytes: int64
    TotalBytes: int64
}

module DownloadTracker =
    val init: targets: DownloadTarget list -> DownloadSessionState
    val update: DownloadEvent -> DownloadSessionState -> DownloadSessionState
```
This enables decoupled UI rendering loops (e.g., 30-60 FPS) to simply query `sessionState` without blocking or synchronizing with download worker threads.

---

## 8. Error Handling Boundary

The F# API separates three outcomes that must not be collapsed into one error channel:

- A completed lookup with no match is a normal result: `Database.find` returns `None`, and cache or search operations return an empty sequence.
- An expected ALPM operation failure is an error value: loading a missing package returns `Error PackageNotFound`, and preparing a transaction can return typed dependency, conflict, or validation failures.
- Caller misuse and runtime faults throw: invalid arguments, use after disposal, stale lifetime tokens, invalid transaction state, out-of-memory conditions, a native failure without an error code, and unexpected managed exceptions are not business outcomes.

`Pacpar.Alpm` provides an internal, failure-returning core to this project through `InternalsVisibleTo`. That core preserves the native contract at one place: it reads the handle error immediately after the call, distinguishes empty values from failures, and consumes payload-bearing transaction lists with the correct element destructor. It returns typed managed failure values to F#; F# maps them to operation-specific public discriminated unions. The public F# surface therefore exposes neither raw pointers nor generated `_alpm_errno_t` values.

The existing C# facade remains exception-based and delegates to the same core. It is not necessary for F# to catch those exceptions on ordinary expected failure paths. Unknown but valid libalpm errnos map to a generic ALPM operation failure so a newer libalpm remains representable; `ALPM_ERR_MEMORY` remains a thrown out-of-memory condition. See [ADR 0001](../../docs/adr/0001-fsharp-error-boundary.md) and [ADR 0002](../../docs/adr/0002-stable-failure-codes-for-fsharp.md) for the trade-offs.

## 9. C# Support for the F# Boundary

### 9.1 Ownership of the bridge

The C# project owns everything that depends on native return conventions and native memory. The F# project owns only the public language model and the translation from the internal bridge values to its discriminated unions. `Pacpar.Alpm` must not reference `FSharp.Core`, return `FSharpResult`, or make F# types part of the C# API.

The bridge is a narrow internal surface, not a second public wrapper. `Pacpar.Alpm.FSharp` receives access through `InternalsVisibleTo`, and the F# project references `Pacpar.Alpm` directly. No F# method should call `NativeMethods`, inspect a pointer, or decide which native destructor is safe.

### 9.2 Internal result and failure values

The bridge uses the classic `TryX` shape. The Boolean means that the ALPM operation completed without an expected ALPM failure; it does not mean that an optional lookup produced a non-null value. On success `failure` is `null`; on expected failure `value` is left at its default and `failure` is populated.

```csharp
namespace Pacpar.Alpm.Internal;

internal static class SessionCore
{
  internal static bool TryCreate(
    string root,
    string dbpath,
    out Alpm value,
    out AlpmFailure? failure);
}

internal static class PackageCore
{
  internal static bool TryLoad(
    Alpm session,
    string filename,
    bool full,
    SigLevel level,
    out LoadedPackage value,
    out AlpmFailure? failure);
}

internal abstract record AlpmFailure
{
  public AlpmFailureCode Code { get; init; }
  public uint NativeCode { get; init; }
  public string Description { get; init; }
  public string? Context { get; init; }
}
```

Void operations use `bool TryX(..., out AlpmFailure? failure)`. Lookup operations that can legitimately find nothing use the same Boolean-success contract and a nullable `out` value: `true` plus `value == null` is a completed query miss, while `false` is an ALPM operation failure. Removal operations additionally return `out bool removed`, because "not present" is a successful negative answer rather than an error.

`AlpmFailure` is supplemented by typed payload records for the transaction cases that already have managed snapshots: missing dependencies, conflicting dependencies, file conflicts, and invalid package names. Other operations can use a generic native failure record. `AlpmFailureCode` is a stable semantic enum; `NativeCode` preserves the numeric libalpm value for diagnostics and forward compatibility. `ALPM_ERR_MEMORY` is not returned as an ordinary failure from the bridge: it is surfaced as `OutOfMemoryException` at the boundary.

The C# exception factory consumes the same `AlpmFailure` values to construct the existing public `AlpmException` hierarchy. This is important for compatibility: `AlpmTransactionException.TakeFailure` must become a thin adapter over a failure-payload builder, not a second interpretation of the output list. The builder remains unsafe and private to C# because it must select the correct element destructor for the errno.

### 9.3 Native signal map

The bridge should make each native convention explicit. The following are all of the ROP-eligible groups currently exposed by C#; normal getters and read-only package properties do not need a result wrapper.

| C# operation group | Native signal | F# meaning | Bridge shape |
| --- | --- | --- | --- |
| create `Alpm` | initialization out-errno and null handle | `Result<AlpmSession, SessionError>` | `TryCreate(..., out value, out failure)` |
| `LoadPackage`, `GetSignature` | return code plus handle errno | `Result<_, PackageError>` | `TryX(..., out value, out failure)` |
| sync database registration/unregistration | pointer/return code plus handle errno | `Result<_, DatabaseError>` | `TryX(..., out value, out failure)` |
| `GetPackage` / `GetGroup` | null pointer as a lookup miss | `PackageView option` / `Group option` | no failure channel |
| package/group caches and server lists | null plus ok errno means empty; null plus errno means failure | `Result<seq<_>, QueryError>` | `TryX(..., out value, out failure)` |
| `Database.Validate` | validity answer plus errno when invalid | `Result<unit, DatabaseError>` | `TryX(..., out failure)` |
| scalar option setters and option-list add/remove | return code plus handle errno; remove also has a not-present answer | `Result<unit, ConfigurationError>` | `TryX(..., out failure)` and `TryRemove(..., out removed, out failure)` |
| callback registration/unregistration | return code plus handle errno | `Result<unit, CallbackError>` | `TrySet...(..., out failure)` |
| transaction begin/add/remove/system-upgrade/interrupt/prepare/commit | return code plus handle errno; prepare/commit also own a failure list | `Result<_, TransactionError>` | `TryX(..., out value, out failure)` with typed payload |

Disposed objects, stale views, invalid transaction state, argument validation, a native failure that reports no errno, and cleanup/release failures remain exceptions. They are programming, runtime, or unrecoverable resource conditions, not part of the F# railway.

### 9.4 C# refactor sequence

1. Add the F# project reference and `InternalsVisibleTo` entry. Keep the public C# API surface unchanged.
2. Add `AlpmFailureCode`, `AlpmFailure` payload records, the `TryX` bridge contract, and the failure builder. Add a mapping test for every generated errno, including the unknown-code fallback.
3. Split every fallible C# operation in the table above into a failure-returning core and a throwing facade. The facade must call the core and translate `AlpmFailure` through one exception factory; no facade should independently re-read or re-classify errno.
4. Move transaction payload consumption out of `AlpmTransactionException` into the core failure builder. Preserve the existing managed payload properties and destructor regression tests through the facade.
5. Add the F# interop translation for all ROP-eligible groups in one module, then expose operation-specific error unions above it. Keep query misses in `option`/empty collections and preserve `removed = false` as a successful answer.
6. Add F# tests for `Result`/`option` boundaries and C# tests proving that every facade and core pair reports the same failure. Include option-list removal semantics and callback registration failures in the contract tests. Run host tests with `dotnet test --filter "FullyQualifiedName!~Integration"`.

Callbacks themselves remain an exception-safe execution boundary: managed handler exceptions are already caught before they can cross the FFI boundary. Only callback registration and unregistration are ROP-eligible because those are normal configuration operations that libalpm can reject.
