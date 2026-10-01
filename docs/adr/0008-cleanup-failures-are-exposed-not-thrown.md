# Cleanup failures are exposed, never thrown from Dispose

---
status: accepted
---

`Transaction.Dispose` currently ignores a failed `alpm_trans_release`: no exception, no record,
`_released` stays false and `CurrentTransaction` stays set. A later `alpm_release` then fails with
`ALPM_ERR_TRANS_NOT_NULL`, so the native handle is never freed and the callback context's GCHandle slot
leaks for the process lifetime — while the caller is told the transaction succeeded. We keep `Dispose`
non-throwing, because throwing from `finally` and `using` turns a cleanup problem into an unrelated
crash, and record the outcome on the transaction instead (`IsReleased`, and the failure itself), for
the F# `withPrepared` bracket to inspect in its `finally` and report as a distinct cleanup failure.

These reports do not go through the consumer's log callback. That callback is an inbound channel from
libalpm, owned and registered by the consumer; synthesizing the wrapper's own diagnostics into it
conflates two sources, it may never have been registered, and teardown is when it is least safe —
`Callback.Dispose()` is already skipped once handle release has failed.

**Consequences.** `Dispose` stays silent by contract, so "did the lock actually get released?" becomes
an explicit question the caller (or bracket) has to ask rather than something inferred from the
absence of an exception.
