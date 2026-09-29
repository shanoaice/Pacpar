# Investigation Report: `Callback` Migration to `WeakGCHandle`

**Target Audience:** Engineering Agents / Maintainers  
**Context:** Migration of `Pacpar.Alpm.Events.Callback` from `Normal` `GCHandle<Callback>` to `WeakGCHandle<Callback>`.  
**Primary Symptom:** Failure of `Pacpar.Alpm.Tests.Unit.CallbackTests.AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased` and `AlpmFinalizer_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased`.

---

## 1. Executive Summary

1. **Test Failure Verdict:** Under the architectural premise that `Callback` must not self-root and `SafeAlpmHandle` must be decoupled from `Callback`, **the test is to blame**. The test asserts an implementation artifact of the previous architecture (`Assert.True(callback.IsAlive)` via a normal GCHandle GC root).
2. **Behavioral Invariant:** The original requirement behind the test (originating in commit `d7d1c22`) was **not** to keep `Callback` immortal, but to prevent `[UnmanagedCallersOnly]` thunks from throwing unhandled exceptions (and causing runtime `FailFast` aborts) when callbacks fire on a leaked native handle.
3. **Core Nuance of `WeakGCHandle`:** Using `WeakGCHandle` allows `Callback` to be collected normally, which correctly breaks the GC root. However, it requires deliberate management to avoid unmanaged handle table slot leaks and slot-reuse type confusion.

---

## 2. Root Cause Analysis

### 2.1 The Previous Architecture (`Normal` `GCHandle`)

Prior to commit `4af0932`:
1. `Callback` allocated `_ctxHandle = new GCHandle<Callback>(this)`. This placed an entry in the CLR GC Handle Table of type `GCHandleType.Normal`.
2. A normal `GCHandle` is an immortal GC root. To prevent this root from pinning `Alpm` forever via `GCHandle -> Callback -> Lifetime -> Alpm`, `Callback` held weak references:
   ```csharp
   private readonly WeakReference<SafeAlpmHandle> _handleRef;
   private readonly WeakReference<Lifetime> _lifetimeRef;
   ```
3. `SafeAlpmHandle` was coupled to `Callback`: `SafeAlpmHandle.ReleaseHandle()` retained a reference to `Callback` and invoked `_callback?.Dispose()` if and only if `alpm_release` succeeded (`err == 0`).
4. If `alpm_release` failed (e.g., an unreleased transaction), `Callback.Dispose()` was intentionally **not** called. The normal `GCHandle` remained allocated, keeping `Callback` rooted in memory.
5. The unit tests asserted `Assert.True(callback.IsAlive)` as an indirect proxy verifying that `_ctxHandle` had not been freed.

### 2.2 The New Architecture (`WeakGCHandle`)

In the new design:
1. `_ctxHandle` is a `WeakGCHandle<Callback>` (`GCHandleType.Weak`).
2. Because `_ctxHandle` is weak, it does **not** root `Callback`.
3. `Callback` is rooted solely by `Alpm.Callback`.
4. In `AlpmDispose_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased`:
   - `DisposeAlpmWithAnUnreleasableHandle` creates `Alpm`, begins an unreleased transaction, calls `alpm.Dispose()`, and drops `alpm` out of scope.
   - Even though `Callback.Dispose()` is skipped (`ReleaseSucceeded == false`), **nothing strongly roots `Callback`**.
   - `GC.Collect()` runs 10 times.
   - `Callback` is collected. `callback.IsAlive` evaluates to `false`.
   - The assertion `Assert.True(callback.IsAlive)` fails.

---

## 3. Technical Findings & Risk Assessment of `WeakGCHandle`

### 3.1 Handle Table Slot Leak on Finalization
* `WeakGCHandle<T>` is a value type (`readonly struct`) wrapping an index in the runtime's unmanaged handle table.
* When the target (`Callback`) is collected, the CLR sets the target pointer to `null`, but **does not free the slot index**.
* Without a finalizer on `Callback`, any `Alpm` instance dropped without calling `Dispose()` leaves its `WeakGCHandle` allocated in the handle table indefinitely.
* *Maintainer Verdict:* Considered benign under normal usage unless `Alpm` instances are repeatedly instantiated and dropped without disposal.

### 3.2 Freed Slot Reuse & Type Confusion Hazard
* In `Alpm.Dispose()`:
  ```csharp
  if (_handle.ReleaseSucceeded)
  {
      Callback.Dispose();
  }
  ```
* If `alpm_release` **succeeds**, `Callback.Dispose()` calls `_ctxHandle.Dispose()`, returning the slot to the runtime pool. This is safe because libalpm destroyed the native handle.
* If `alpm_release` **fails**, `Callback.Dispose()` is skipped. The slot remains allocated with a `null` target.
* **Critical invariant:** `Callback.Dispose()` **must never** be called when release fails. If the slot were returned to the pool while `alpm_handle_t` still pointed at `ctx`, another thread allocating a `GCHandle` could receive the same slot. In .NET, `WeakGCHandle<T>.TryGetTarget` performs an unchecked `Unsafe.As<object, T>`. Invoking a callback would treat the foreign object as a `Callback`, causing memory corruption.

### 3.3 Loss of Teardown Diagnostics
* When an un-disposed `Alpm` is finalized, the GC collects `Callback` before `SafeAlpmHandle.ReleaseHandle()` runs on the finalizer thread.
* If `alpm_release` emits native log messages, `WeakGCHandle.TryGetTarget` returns `false`, causing teardown log messages to be dropped silently.

---

## 4. Comprehensive Fix Options

### Option 1: Retain `WeakGCHandle` and Align Tests to the New Contract (Current Path)

Keep the `WeakGCHandle` implementation, accept the finalizer slot leak as benign, and update the test suite to reflect the new lifecycle.

#### Action Items:
1. **Update `CallbackTests.cs`:**
   Invert the liveness check to assert that `Callback` is collected, and verify that subsequent callback invocations through the leaked handle safely no-op:
   ```csharp
   [Fact]
   public void AlpmDispose_DoesNotKeepCallbackRooted_WhenTheHandleCouldNotBeReleased()
   {
     var workspace = Path.Combine(Path.GetTempPath(), "pacpar-dispose-tests", Guid.NewGuid().ToString("n"));
     var root = Path.Combine(workspace, "root");
     var dbpath = Path.Combine(workspace, "var", "lib", "pacman");
     Directory.CreateDirectory(root);
     Directory.CreateDirectory(Path.Combine(dbpath, "local"));
     Directory.CreateDirectory(Path.Combine(root, "tmp"));

     var callback = DisposeAlpmWithAnUnreleasableHandle(root, dbpath);

     for (var i = 0; i < 10 && callback.IsAlive; ++i)
     {
       GC.Collect();
       GC.WaitForPendingFinalizers();
     }

     Assert.False(callback.IsAlive, "Callback should not be rooted when Alpm is out of scope.");

     if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
   }
   ```
   Apply the same update to `AlpmFinalizer_KeepsTheCallbackContext_WhenTheHandleCouldNotBeReleased`.

2. **Add Guard Clauses in Agent Thunks:**
   In `Callback.cs`, add explicit `if (ctx == null) return;` / `return -1;` guards before attempting `WeakGCHandle.FromIntPtr((nint)ctx)` to avoid unnecessary exception dispatch.

3. **Fix Unrelated Regressions in Commit `4af0932`:**
   - **`SafeBindingPairingTests` failure:** `alpm_option_set_logcb` signature mismatch in `NativeMethods.SafeHandle.cs`. Keep the hand-written P/Invoke private inside `Callback.cs` (as was done before) so reflection tests expecting an exact match with csbindgen output pass.
   - **`GcAnchorAuditTests` failure:** Add the constructor's new safe handle provenance to `gc-anchor-allowlist.txt`.

---

### Option 2: Monotonic ID / Global Registry Pattern (Cleanest Decoupling)

Eliminate `GCHandle` entirely from the FFI boundary.

#### Mechanics:
1. Maintain an internal thread-safe registry:
   ```csharp
   private static long s_nextId;
   private static readonly ConcurrentDictionary<nint, WeakReference<Callback>> s_registry = new();
   ```
2. In `Callback` constructor, assign an ID `(void*)Interlocked.Increment(ref s_nextId)` and pass that as `ctx`.
3. In `EventAgent(void* ctx, ...)`:
   ```csharp
   if (ctx != null && s_registry.TryGetValue((nint)ctx, out var weak) && weak.TryGetTarget(out var callback))
   {
       // Invoke handler safely
   }
   ```
4. In `Callback.Dispose()`: Remove ID from `s_registry`.

#### Trade-offs:
* **Pros:** Zero GC handle table leaks; IDs are monotonic and never recycled, completely eliminating type confusion; `SafeAlpmHandle` remains completely decoupled.
* **Cons:** Introduces a dictionary lookup on each native callback invocation (negligible for event/log frequencies).

---

### Option 3: Explicit Callback Unregistration

Unregister all native callbacks inside `Callback.Dispose()`.

#### Mechanics:
When `Alpm.Dispose()` runs, invoke `Callback.Dispose()`, which calls:
```csharp
NativeMethods.alpm_option_set_eventcb(_handle, null, null);
NativeMethods.alpm_option_set_fetchcb(_handle, null, null);
NativeMethods.alpm_option_set_questioncb(_handle, null, null);
NativeMethods.alpm_option_set_dlcb(_handle, null, null);
NativeMethods.alpm_option_set_progresscb(_handle, null, null);
NativeMethods.alpm_option_set_logcb(_handle, null, null);
```
Then free the context handle.

#### Trade-offs:
* **Pros:** Libalpm native handle has its callbacks cleared, so no callback can ever fire on a leaked handle. Context can be freed unconditionally without risk of UAF.
* **Cons:** Does not protect against un-disposed handles that are finalized (finalizer cannot safely unregister callbacks if `SafeAlpmHandle` is already dead).

---

### Option 4: Revert to Previous Architecture

Revert commit `4af0932` to restore `GCHandle<Callback>` (`Normal`) with `WeakReference<SafeAlpmHandle>` and `WeakReference<Lifetime>`.

#### Trade-offs:
* **Pros:** Re-aligns with existing test suite and DocFX documentation without code changes.
* **Cons:** Preserves the self-rooting pattern and coupling between `SafeAlpmHandle` and `Callback`.

---

## 5. Recommended Decision

If the project objective is to eliminate `Callback` self-rooting and keep `SafeAlpmHandle` clean:
* Proceed with **Option 1** (update tests and thunk guards), OR
* Adopt **Option 2** (Monotonic ID registry) if handle table slot leaks and slot-reuse safety need to be strictly eliminated without re-coupling `SafeAlpmHandle`.
