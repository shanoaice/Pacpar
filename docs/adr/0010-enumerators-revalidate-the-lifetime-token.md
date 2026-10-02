# `AlpmList` enumerators re-validate the lifetime token on every step

---
status: accepted
---

`AlpmList<T>.Enumerator` validated the lifetime token in `Current` but not in `MoveNext`, and
`MoveNext` dereferences the previously visited node (`_current = _current->next`). A `foreach` whose
body disposed the session therefore read freed memory on the next step before any check could run —
either crashing on an unmapped page, or, worse, reading a `next` of zero and ending the loop
silently with a truncated result instead of the `AlpmLifetimeException` the type documents.
`MoveNext` now re-validates the token before touching the node.

The cost was measured with BenchmarkDotNet against a 2473-package local database, largest dependency
list 103 entries: ~8.5ns per element, which is +15% on a loop that materializes every element and
2.89× on a bare advance loop, with a control benchmark that never calls `MoveNext` unchanged. We pay
it: a use-after-free read that fails silently is worth nanoseconds, and the alternative is to
document a contract the type's own doc comment denies.

## Considered Options

Making the check O(1) — a single shared flag per token rather than a walk of the parent chain — would
bring the price down to a field read, and was rejected. The parent-chain walk is what lets a token be
retired by flipping one bool **top-down**: the root's finalizer can invalidate every database,
transaction and view below it without touching a registry. Moving the work from the check to the
invalidation would put it on the finalizer path, which is the one place this design is deliberately
kept trivial.

## Consequences

The check is a same-thread guard, not a synchronisation primitive: a concurrent disposer can still
win the race between validation and dereference. That residual is governed by the documented
single-thread affinity of `alpm_handle_t` and is not something the wrapper can fix without locking.
