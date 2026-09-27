# Architecture Reference: JIT Liveness, GC.KeepAlive, and Automated Static Analysis in Pacpar.Alpm

> **Document Status**: Approved Architecture Specification & Technical Design Reference  
> **Audience**: Library Maintainers, Application Developers, and Static Analysis Authors  
> **Related Components**: `Pacpar.Alpm.Lifetime`, `Pacpar.Alpm.Bindings.NativeMethods`, `Pacpar.Alpm.Tests.Unit.GcAnchor`

---

## 1. Executive Summary & Problem Definition

`Pacpar.Alpm` interlinks high-performance managed .NET code with the unmanaged Arch Linux `libalpm` C engine. While the **Root-Anchored Lifetime Token Tree** ([Architecture Reference: Lifetime Tokens](lifetime-tokens.md)) protects views against explicitly released contexts, it relies on the garbage collector (GC) and JIT compiler to keep root owners alive during active use.

A fundamental memory-safety hazard in managed/unmanaged interop is **premature garbage collection during unmanaged execution**:

```
[Managed Thread]                             [CLR Finalizer Thread]
ptr = this.BackingStruct;
// JIT marks 'this' dead (last use of 'this')
call NativeMethods.alpm_pkg_get_size(ptr) -> [Preemptive GC Mode]
                                             GC runs on another thread
                                             'this' has no live roots in stack map
                                             'this' queued for finalization
                                             ~LoadedPackage() runs alpm_pkg_free(ptr)
[Native call reading from freed memory] ----> Use-After-Free / SIGSEGV / Corrupted Heap!
```

### The Mechanism of the Hazard

1. **Preemptive P/Invoke Transitions**: When a managed thread transitions across the native boundary into unmanaged code (`NativeMethods.*`), the runtime places the thread into preemptive GC mode. In this mode, the GC can trigger at any moment without waiting for the native thread to reach a managed safepoint.
2. **JIT May-Liveness Optimization**: In optimized .NET execution (Tier 1 JIT in Release mode without a debugger attached), the JIT compiler computes the exact liveness of registers and stack slots. An object reference ceases to be reported as a GC root at its *last instruction of use*, not at the end of the enclosing C# method or lexical block.
3. **The Window of Exposure**: If a method loads an unmanaged pointer from a field (`this.BackingStruct`), passes that pointer to a native function or string allocation, and makes no subsequent references to `this`, `this` is completely unrooted during the execution of that unmanaged call.
4. **Finalizer Races**: If a collection occurs during this unrooted window, the root owner (`Alpm` or `LoadedPackage`) is enqueued for finalization. The CLR Finalizer thread concurrently executes `alpm_release(handle)` or `alpm_pkg_free(package)`, freeing the native memory that the caller is actively reading.

---

## 2. Liveness Classification Rules

Not every native call requires manual intervention. Under the CLR's static may-liveness rules, an anchor remains rooted if *any* subsequent code path in the method accesses it.

### Naturally Safe Forms (No Modification Required)

1. **Subsequent Field Assignment**:
   ```csharp
   // Safe: Storing into 'field' evaluates 'this' after the native call completes.
   return field ??= NativeString.FromNative((nint)NativeMethods.alpm_pkg_get_name(BackingStruct))!;
   ```
2. **Subsequent Token or Receiver Argument**:
   ```csharp
   // Safe: 'Lifetime' is an instance property of 'this', evaluated after the native call
   // before invoking the AlpmStringList constructor.
   return new AlpmStringList(NativeMethods.alpm_pkg_get_licenses(BackingStruct), Lifetime);
   ```
3. **SafeHandle Parameter, Anchored on the Error Path**:
   ```csharp
   // Safe: the context crosses as a SafeAlpmHandle, so the runtime marshaller refcounts it for the
   // whole call; the error path then reads _library.Errno, which keeps the owner live as well.
   var err = NativeMethods.alpm_add_pkg(_library.Handle, pkg.BackingStruct);
   if (err != 0) throw new AlpmPackageException(_library.Errno, ...);
   ```

### Vulnerable Forms (Requiring `GC.KeepAlive`)

1. **Bare Return Accessors**:
   ```csharp
   // Vulnerable: 'this' is loaded to fetch BackingStruct, then abandoned.
   public CLong Size => NativeMethods.alpm_pkg_get_size(BackingStruct);
   ```
2. **Unanchored Managed Allocations**:
   ```csharp
   // Vulnerable: PackageFile constructor allocates a managed UTF-8 string via Marshal.PtrToStringUTF8.
   // If string allocation trips GC, 'this' and the parent package are dead.
   return new PackageFile(&backingStruct->files[index]);
   ```
3. **Cross-Method Exception Payloads**:
   ```csharp
   // Vulnerable: Prepare() calls TakeFailure(_library.Errno, errData) and discards _library.
   // Conflict parsing in TakeFailure reads handle-owned package memory; an unrooted _library
   // allows ~Alpm() to release the database before Conflict reads package names.
   throw AlpmTransactionException.TakeFailure(_library.Errno, errData, "Failed to prepare transaction");
   ```

---

## 3. Automated IL Static Analysis Engine

To prevent regressions and avoid relying on fragile manual audits, `Pacpar.Alpm.Tests` includes an automated Intermediate Language (IL) verification scanner under `Unit/GcAnchor/`. It runs in unit tests in tens of milliseconds, inspecting every compiled method across the library without an explicit list of method names.

The engine has three parts:

- `IlDecoder.cs` decodes a method body into instructions whose operand sizes are exact. The opcode-to-operand-size table is derived from `System.Reflection.Emit.OpCodes` by reflection at startup, so it cannot drift from the runtime's own encoding.
- `IlControlFlowGraph.cs` builds basic blocks and post-dominators over that stream, adding exception-handler edges conservatively. Post-dominance is what lets the scanner say "this anchor is on every path after the hazard" rather than "an anchor appears somewhere later".
- `GcAnchorAudit.cs` holds hazard discovery, mechanism classification and the verdict.

### Why instruction-level decoding replaces the byte scan

A byte scan cannot tell an opcode from an operand byte. The constant `0x200` encodes as `20 00 02 00 00`, and the `0x02` in the middle is an operand byte of `ldc.i4`, not `ldarg.0`; a metadata token whose low byte is `0x02` or `0x7D` causes the same confusion. Decoding removes that entire class of misread, which is what let the previous scanner pass genuinely unanchored code.

### Hazard sites

A hazard site is a place that reads unmanaged memory through a managed owner:

| Kind | Trigger | Example |
|---|---|---|
| `NativeCall` | `call`/`callvirt` into `Pacpar.Alpm.Bindings.NativeMethods`, or a `calli` through a function pointer | `alpm_pkg_get_size(BackingStruct)` |
| `DangerousGetHandle` | `SafeHandle.DangerousGetHandle()` | `LoadedPackage`'s constructor: `base((_alpm_pkg_t*)handle.DangerousGetHandle())` |
| `ManagedAllocationFromPointer` | `newobj` whose constructor takes a pointer | `new PackageFile(&backing->files[i])` |

`ldftn` and `ldvirtftn` are not hazards: they load a method address without invoking it. The invocation they feed is the `calli`, which is detected in its own right — that is how `AlpmOptions.SetStringOption`, which reaches libalpm through `&NativeMethods.alpm_option_set_dbext`, is audited at all. Constructors are scanned too: `GetMethods` never returns them, and the snapshot types (`PackageFile`, `Conflict`, `ConflictPkg`, `Group`, …) do their native reads inside theirs.

Excluded from the scan: the generated `Bindings` namespace, static classes (there is no owner to anchor), `SafeHandle` derivatives (their `ReleaseHandle` *is* the finalization cleanup), and the non-public primitives of `AlpmOptionList<T>` — `GetList`, `AddNative`, `RemoveNative`, `Acquire`, `AcquireForAdd`, `Release`, `View` and `FindIn`. Those are reachable only from the base class itself, which anchors the whole family, so scanning just the option lists' public entry points keeps that single obligation audited once instead of re-reporting it as a defect on every concrete option list. This replaces the previous scanner's blanket "skip anything derived from `AlpmOptionList`".

### Mechanisms

Every hazard site is classified by the mechanism that is supposed to keep the owner alive. Mechanism identification is deliberately separate from the safety decision, because the mechanisms do not share a contract.

| | Mechanism | What it keeps alive | Contract |
|---|---|---|---|
| M1 | `GC.KeepAlive(this)` | a managed object's reachability | the call post-dominates the hazard |
| M2 | `SafeHandle` parameter | one SafeHandle | the CLR P/Invoke stub refcounts it |
| M3 | `DangerousAddRef` / `DangerousRelease` | one SafeHandle's refcount | a *pair* bracketing the call, released in a `finally`, guarded by the `ok` flag |
| M4 | receiver liveness | `this` | `this` is used after the hazard, or is still on the evaluation stack when it fires |
| M5 | synchronous callback frame | the owner of the libalpm call | the anchor sits in the managed caller, outside this method body |
| M6 | delegated to caller | — | the pointer arrives as a parameter, so the call site must anchor it |
| R2 | token-carrying construction | the constructed object | the constructor receives the owner's token as a live argument |

M3 is never auto-accepted. `DangerousRelease` alone proves nothing: it is the *end* of a region, and calling it without a matching `DangerousAddRef` underflows the refcount and can release the handle early. Pairing is a property of control flow, so an M3 site stays `NeedsReview` until a human confirms it.

### Ownership

A mechanism that keeps `this` alive implies memory safety only when `this` transitively owns the memory:

| Class | Condition | Consequence |
|---|---|---|
| `Strong` | the type holds a `SafeHandle` field, or a non-nullable `Lifetime` field | M1 and M4 suffice |
| `Token` | the type holds only a `Lifetime?` field | the token may be null, so the site needs review |
| `None` | neither | anchoring `this` protects nothing |

A non-nullable `Lifetime` counts as `Strong` because the token tree's own invariant makes the hop unconditional: `Lifetime._root` is a strong reference assigned in both constructors, so any live token keeps the owner reachable in one step. Nullability is read from `NullabilityInfoContext`, since `Lifetime?` and `Lifetime` are the same `Type` to reflection.

### Verdicts

The audit is three-valued, and the third value is what keeps it usable:

- **`Safe`** — a validator proved the mechanism.
- **`NeedsReview`** — a mechanism is claimed, but its contract is a property of control flow, of the callers, or of a token that may be null, so IL alone cannot prove it.
- **`Unsafe`** — nothing was claimed at all, or a claim was disproved.

A site whose pointer can be attributed to neither `this` nor a parameter is reported as `NeedsReview`, never as `Unsafe`: ambiguity must not be reported as a confirmed defect.

### Baseline

`NeedsReview` sites are resolved against `Unit/GcAnchor/gc-anchor-allowlist.txt`, which records the review decision and its reason exactly once:

```
<Type>[.<Member>] | <mechanism> | <why the site is safe>
```

A type-level entry records a type-wide invariant and excuses `NeedsReview` for that type; it never excuses `Unsafe`, so a newly added unanchored member still fails the build instead of hiding behind its type. A stale entry — one that no longer matches any hazard site — also fails the build.

### Tests

| Test | Asserts |
|---|---|
| `HazardSites_AreEnumerated` | discovery is non-empty and has not shrunk below the recorded count |
| `NoHazardSiteIsUnanchored` | no site is `Unsafe`, i.e. no mechanism is missing outright |
| `EveryClaimedMechanismIsProvenOrAllowlisted` | every `NeedsReview` site is proven or reviewed, and no allowlist entry is stale |

### The binding layer underneath the audit

The audit reasons about the wrapper's own IL; the layer underneath it is guarded separately. Every libalpm entry point that takes the ALPM context has a hand-written `[LibraryImport]` overload taking `SafeAlpmHandle` in place of the raw `_alpm_handle_t*` (`src/Pacpar.Alpm/Interop/NativeMethods.SafeHandle.cs`), so the context is refcounted by the runtime marshaller on every such call. That is why M2 is the dominant mechanism above: 115 of the generated declarations take the context, 114 of them have a safe overload, and `alpm_release` is the one deliberate exception — it is the release routine itself, which `SafeAlpmHandle.ReleaseHandle()` must call with the raw value, because a `SafeHandle` cannot be marshalled from inside its own cleanup.

`Unit/Interop/SafeBindingPairingTests.cs` keeps that layer honest. It pairs each hand-written overload with its csbindgen-generated `[DllImport]` declaration by `EntryPoint` — `DllImportAttribute.EntryPoint` is populated on the generated declarations and `LibraryImportAttribute.EntryPoint` survives on the hand-written ones, and both live on the same partial `NativeMethods` class, so one reflection pass sees them together — then requires equal arity, equal parameter types with `SafeAlpmHandle` standing in for `_alpm_handle_t*`, and an equal return type. A libalpm signature change therefore fails the build instead of corrupting the stack at the call boundary, and an entry point that loses its safe overload fails too. The suite needs no native library: reflection never invokes the methods.

---

## 4. Testing & Runtime Realities

### Why Stochastic Unit Tests Do Not Fail

Running standard unit tests—even under loops of 2,000 iterations with continuous background `GC.Collect()` calls—consistently passes without throwing exceptions. Three structural factors explain why stochastic testing cannot catch this design flaw:

1. **Nanosecond Execution Window**: A bare struct getter (`alpm_pkg_get_size`) executes in ~10–30 nanoseconds. The chance of a GC preemption landing precisely in that window is less than 0.01%.
2. **Asynchronous Finalizer Scheduling**: In .NET, unrooted objects with finalizers are placed into the finalization queue. The CLR Finalizer thread must be scheduled by the OS to execute `alpm_pkg_free`. OS context switching and thread scheduling take 10–50 microseconds—thousands of times longer than the native call. The calling thread returns before the finalizer can run.
3. **Accidental Stack Rooting in Test Harnesses**:
   - `using var x`: C# generates a hidden `try ... finally { x.Dispose(); }`, keeping `x` rooted on the stack for the entire test method.
   - `Assert.Throws<T>(() => x.Method())`: Lambda closures allocate a compiler-generated `DisplayClass` that retains a strong reference to `x` across the assertion.
   - Tiered Compilation (Quick JIT): By default, modern .NET runs newly invoked test methods in Tier 0 (unoptimized), where local variable lifetimes are artificially extended to the end of the method, matching Debug behavior.

### Enforcing Strict Liveness in Testing

To eliminate artificial lifetime extensions during testing:
- **Command Line**: Run tests with optimization and without Quick JIT:
  ```bash
  DOTNET_TieredCompilation=0 DOTNET_TC_QuickJit=0 dotnet test --filter "FullyQualifiedName!~Integration" -c Release
  ```
- **Code Attributes**: Apply `[MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]` to helper methods to bypass Tier 0 directly.

`GcKeepAliveAuditTests.cs` keeps a small suite that exercises the real paths under forced liveness for exactly this reason, and it is worth running headlessly for a smoke signal. It cannot be the regression gate: a passing stress loop only says the window was not hit this time. The static audit in §3 is the gate, because it reasons about the emitted IL instead of racing the finalizer.

---

## 5. Future Reference: Compile-Time Roslyn Analyzer (`PACPAR001`)

While the IL inspection test provides high-speed automated regression protection in the test suite, compile-time detection in the IDE is the ideal long-term enhancement.

### Analyzer Specification

| Property | Value |
|---|---|
| **Diagnostic ID** | `PACPAR001` |
| **Category** | `Reliability` / `MemorySafety` |
| **Default Severity** | `Warning` (escalated to `Error` on CI) |
| **Title** | Unanchored P/Invoke call on unmanaged wrapper instance |
| **Message Format** | `Method '{0}' invokes native method '{1}' without anchoring 'this' via GC.KeepAlive.` |

### Control Flow Graph (CFG) Algorithm

Using `Microsoft.CodeAnalysis.Diagnostics.AnalysisContext`:
1. Register on `OperationKind.MethodBody` or `OperationKind.Block`.
2. Locate any `IInvocationOperation` targeting methods in the `Pacpar.Alpm.Bindings.NativeMethods` type.
3. Check the Control Flow Graph (CFG) from the native call operation to all `return` exit points:
   - Does any successor basic block contain an `IInvocationOperation` calling `GC.KeepAlive(this)`?
   - Does any successor operation access an instance member on `this`?
4. If a CFG path reaches a return statement without accessing `this` or calling `GC.KeepAlive(this)`, report diagnostic `PACPAR001`.

### Automated Code Fix (`CodeFixProvider`)

A companion Roslyn code fix can automatically rewrite bare expression-bodied getters:

```csharp
// Before:
public CLong Size => NativeMethods.alpm_pkg_get_size(BackingStruct);

// After automated code fix:
public CLong Size
{
  get
  {
    var result = NativeMethods.alpm_pkg_get_size(BackingStruct);
    GC.KeepAlive(this);
    return result;
  }
}
```
