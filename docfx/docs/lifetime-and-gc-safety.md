# Lifetime and GC Safety

> **Document status**: Maintainer reference for the landed design.
> **Audience**: Library maintainers and reviewers.
> **Supersedes**: the earlier "Root-Anchored Lifetime Token Tree" specification and the separate
> GC.KeepAlive analysis. Both described a design that no longer exists in the code.

## 1. Two hazards, two answers

The wrapper borrows memory that `libalpm` owns. Two failures can make a borrow read freed memory.

| Hazard | Example | Answer |
|---|---|---|
| **Premature release**: managed code releases the native memory while a wrapper still reads it. | A caller keeps a package view, unregisters the database, then reads the view. | Lifetime domains and stamps (sections 3 and 4). |
| **Premature finalization**: the JIT stops reporting an owner as live during a native call, the GC finalizes it, and the finalizer frees the memory under the call. | A getter loads the pointer, calls `alpm_pkg_get_size`, and never touches `this` again. | GC anchoring (section 5) plus the IL audit (section 6). |

The first hazard is a contract problem. The library answers it with a mechanism that cannot be
bypassed. The second hazard is a JIT liveness problem. The library answers it with an anchor at the
call site, backed by a static audit.

## 2. Object model: owners and views

Only a few managed types own native memory. Everything else borrows from one of them.

| Type | Kind | What it holds | Released by |
|---|---|---|---|
| `Alpm` | owner | the `alpm_handle_t` inside a `SafeAlpmHandle` | `Dispose`, or the handle's finalizer |
| `Database` | owner | an `alpm_db_t*` owned by a session or by the database registry | `libalpm`, on unregister or handle release |
| `Transaction` | owner | the active `alpm_trans_t` on the handle | `Dispose` (`alpm_trans_release`) |
| `LoadedPackage` | owner | an `alpm_pkg_t*` loaded from a file | `Dispose`, the hand-over to a transaction, or the sweep in `Alpm.Dispose()` |
| `PackageView`, `FileList`, `Group`, `AlpmList<T>` | view | a native pointer plus a stamp | never; they only borrow |

A view never owns memory. A view must therefore know when its borrow has become invalid, and that is
what a stamp records.

## 3. Lifetime domains and stamps

A **domain** (`Lifetime`) is one object libalpm can release on its own. Each domain owns a
monotonically increasing generation counter and a strong reference to the managed owner, which is the
GC anchor.

- A **root** domain belongs to a session (`Alpm`) or to a file-loaded package.
- A **child** domain belongs to one database or to one transaction. `CreateChild` refuses to build a
  third level, because no stamp would cover it.

A **stamp** (`LifetimeStamp`) is a readonly struct a view holds. It records four values: the resource
domain, that domain's generation at capture time, the root domain, and the root's generation. A check
compares two integer pairs. There is no pointer chasing.

**Invalidating a domain means incrementing it.** One increment retires every stamp ever taken from
that domain, because each stamp compares against the current value when it is read. Nothing is pushed
into the views. That is what makes invalidation safe on a finalizer thread, where enumerating views
would not be.

### 3.1 Why two levels are enough

Every `libalpm` call that frees memory a view could borrow falls into one of two scopes.

| Scope | Events |
|---|---|
| **Session** | `alpm_release`; `alpm_unregister_all_syncdbs`; a commit, which rewrites the local database cache and can free individual local packages |
| **One resource** | `alpm_db_unregister`, `alpm_db_update`, the lazy cache rebuild, and the server-list setters (a database); `alpm_trans_release` (a transaction); `alpm_pkg_free` (a file-loaded package) |

Nothing below a package can be released on its own. The file list is an inline struct inside the
package. The dependency, backup, and xdata lists are freed only by `_alpm_pkg_free`. A lazy load
(`LAZY_LOAD`) only runs when the value is absent, and it never frees a previous value. A group dies
with the database that owns it. The header states the same rule: database packages are freed when the
database is unregistered, and file-loaded packages are freed manually or on transaction release.

### 3.2 Invalidation points

Invalidate **before** the native call that frees, never after. An early increment only makes a
still-valid view report failure, which is the safe direction. A late increment leaves a window in
which a view still passes its check.

| Operation | What it invalidates |
|---|---|
| `Alpm.Dispose()` | the root domain, before the handle is released |
| `SafeAlpmHandle.ReleaseHandle` | the root domain, before `alpm_release` |
| `Transaction.Commit()` | the root domain, before `alpm_trans_commit` |
| `Transaction.Dispose()` | the transaction domain, before `alpm_trans_release` |
| `Database.Unregister()` | the database domain and the root domain, because removing a database frees its node in the handle's list |
| `Alpm.UnregisterAllSyncDatabases()` | the root domain, and it clears the per-pointer registry |
| `LoadedPackage.Dispose()` and the hand-over | that package's own domain |

Two consequences are deliberate. A commit invalidates every view in the session, not only local
database views. Unregistering one database also invalidates session-level list views. Both trade
precision for safety: an over-invalidated view throws, an under-invalidated view reads freed memory.

## 4. Guard enforcement

A wrapper validates its stamp before it reads the pointer. The pointer itself has no readable name.

```csharp
internal _alpm_pkg_t* BackingStruct
{
    get
    {
        ThrowIfDisposed();
        return field;            // C# 14: the backing field is compiler-generated and unnamed
    }
    private init => field = value;
}
```

Two properties follow.

- A sibling type cannot read the pointer without the guard. A private field inside a class is still
  readable by that class, which is why the raw pointer is not a named field at all.
- The pointer is assigned only in the constructor. A wrapper's pointer is fixed for its lifetime.

**The one exception is a cached member.** `field ??=` returns the cached value without evaluating its
right-hand side, so the guarded accessor never runs on a later read. Every caching member therefore
carries an explicit `ThrowIfDisposed()` before it returns. `PackageBase.Name` and ten other members
do this, and a test pins the behaviour.

## 5. GC anchoring

A stamp holds its domains, and each domain holds a strong reference to its owner. A live view
therefore keeps the session, the handle, and the native graph alive. This chain is the **primary**
guarantee: it makes it impossible for a finalizer to free memory under a view that is still reading.

Two rules keep the chain intact.

1. A domain stores its owner strongly. If it stored it weakly, a view could outlive its owner.
2. A view stores its stamp by value. A struct field is a real reference, so the chain survives.

**The finalizer hook is a second line of defence.** `SafeAlpmHandle.ReleaseHandle` retires the root
domain before it calls `alpm_release`. The handle holds that domain **weakly**: a strong reference
would root the session through the domain's owner, and with it the callback context that the
weak-GCHandle design deliberately keeps collectable. A weak reference is enough, because a live view
holds its domain strongly; when no view exists, there is nothing to invalidate.

## 6. The static audit

`GcAnchorAudit` inspects the IL of every method in the wrapper and reports each place that reads
native memory through a managed owner. A site is safe only when a validator proves a mechanism, and
the audit fails the build on any site that claims nothing.

| Mechanism | Meaning |
|---|---|
| `KeepAlive` | `GC.KeepAlive(owner)` follows the call on every path |
| `SafeHandleParameter` | the P/Invoke takes a `SafeHandle`, so the marshaller refcounts it for the call |
| `AddRefRelease` | the call sits inside a `DangerousAddRef` / `DangerousRelease` region |
| `ReceiverOnStack` | `this` is live after the call and transitively owns the memory |
| `SyncCallbackFrame` | the anchor is the managed caller of a synchronous libalpm callback |
| `DelegatedToCaller` | the pointer arrives as a parameter, so the caller owns the obligation |
| `CarriesOwnAnchor` | the constructed object receives the owner's stamp and anchors itself |

Verdicts are three-valued: `Safe`, `NeedsReview`, and `Unsafe`. A `NeedsReview` verdict means the
mechanism holds but its contract is a property of control flow or of the type graph, which IL
inspection cannot prove. `gc-anchor-allowlist.txt` records each such decision once, with its reason.
A stale entry fails the build, so the list cannot rot.

### 6.1 The throw-expression rule

`GC.KeepAlive(x)` keeps `x` alive only up to its own instruction. An anchor written before an
exception-construction expression therefore does not cover a native read inside that expression, and
the per-call-site audit cannot see it: the read sits in another method, recorded as delegated to the
caller.

`FindPointerCallsInsideThrowExpressions` closes that gap. It walks the IL back over the expression a
`throw` consumes, and it reports any call that is a binding entry point or takes a pointer parameter.
The second half matters, because the trap that motivated the rule arrived through a helper that took
the handle, not through a direct binding call. A second test feeds the rule a deliberate violation,
so the rule cannot pass merely because the library contains no offending `throw`.

The corresponding production rule is simple: read the errno at the failure site, then anchor, then
throw a value that reads nothing. `NativeCall` offers only two overloads, and neither reads native
memory during construction.

## 7. Invariants for maintainers

1. Invalidate before you free. Never after.
2. A commit and an unregister invalidate more than they strictly need to. Do not narrow that without
   replacing the safety argument.
3. A view's stamp is captured from the resource that owns the memory. Do not accept a stamp from the
   caller; do not build a third level.
4. Every read of a native pointer goes through a guarding accessor. A caching member guards itself.
5. Any native read inside an exception expression is a defect. Read the value first.
6. A session belongs to one thread. The domain registry relies on that, and adding a lock to it would
   protect the registry while leaving `libalpm`'s own state unprotected.

## 8. Known gaps and accepted trades

One gap is open. One trade was accepted, and it is written down here so that it is not re-litigated
from memory.

- **Open.** Callback payloads carry the session stamp rather than the stamp of the resource that owns
  each package. That is safe because a commit retires the session, but it is coarse.

### 8.1 A dropped `LoadedPackage` is an accepted leak

A `LoadedPackage` that the garbage collector collects without `Dispose` keeps its native package
until the session ends. Three paths reclaim it, in the order a caller should prefer them.

1. `LoadedPackage.Dispose()`, normally through `using`. This is the only path that retires the
   package at a point the caller chose.
2. The hand-over to a transaction. `alpm_trans_release` frees what it was given.
3. `Alpm.Dispose()`, which sweeps the session's registry of file-loaded packages **before** it
   releases the handle.

The leak is therefore bounded by the session, not by the process, and it costs one package per
dropped wrapper. Loading a package from a file is a file read, an archive parse and a `.PKGINFO`
parse; sessions are short and the operation is rare, so the size of the leak does not justify the
cost of removing it. A batch release method was considered and declined: it trades "leaks until the
session ends" for "leaks until you remember to call it", which is the same class of mistake as
forgetting `Dispose`.

**Why there is no finalizer.** Three reasons, any one of which is enough.

1. **The Framework Design Guidelines forbid a public type with a finalizer**, and `LoadedPackage` is
   `public sealed`. The finalizer would have to sit on a non-public `SafeHandle` that the public
   type holds, which is the shape the guideline prescribes - and the next two reasons are why that
   shape is not free here.
2. **The anchoring chain does not cover it.** `CreateChild` hands the root's owner to every child
   domain, so a loaded package's domain anchors the **session**, not the package. A borrow taken
   from the package - `Depends`, `Files`, `Licenses`, and the eight other members that pass
   `Lifetime?.Domain` - therefore keeps the session alive and nothing else. Give the package its own
   finalizer and that borrow can be freed underneath it: the leak becomes a use-after-free. Closing
   that gap means giving the package domain an owner of its own, which changes the invariant §5 is
   built on.
3. **Ordering.** `Alpm.Dispose()` frees loaded packages before `alpm_release`, on the session's
   thread. A finalizer gives up both properties: the free happens whenever the GC reaches it,
   possibly after `alpm_release`, and `alpm_pkg_free` documents that it sets `pm_errno` on failure,
   which suggests it touches the handle. A session that is itself dropped to the GC has no ordering
   guarantee at all, which is why the contract stays "dispose the session".

The F# facade is a stub, so the error-bridge decisions recorded in the ADRs are not implemented yet.
