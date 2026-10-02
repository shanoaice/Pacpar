# Read errno only after a call whose return value says it failed

---
status: accepted
---

libalpm keeps one `pm_errno` per handle and does not clear it when a call succeeds. Whether a given
entry point resets it on entry is an implementation detail: measured against libalpm 16.0.1, 139
exported functions clear it via `CHECK_HANDLE` or an explicit assignment, 53 never touch it at all,
and the rest set it only on failure. `alpm.h` documents none of this. Reading errno after a
successful call therefore reports whatever the previous failure left on the handle, and reading it
after another native call reports that call's state instead.

There is no way to clear the handle errno from outside, so "reset first, then look" is not available.
The rule is therefore a reading discipline:

> errno is read immediately after a call that signalled failure through its **return value**, and
> nothing native may run in between.

Two shapes are legal, and they are the only two:

1. **Return value is the signal.** The call site checks its result and hands a handle - captured
   *before* that call - to `NativeCall.Failure`, which reads errno as its first statement. A failure
   that set no errno becomes `AlpmNativeFailureException` rather than an `ArgumentOutOfRangeException`
   from the errno-to-exception factory.
2. **The entry point resets errno on entry.** Only then may a null/empty result be interpreted with
   the handle's errno. `Database.GetPackageCache` and `Database.GetGroupCache` are the two places that
   do this, through `ThrowIfTheLastCallFailed`; both underlying functions clear `pm_errno` in their
   first statement.

`Database.GetServers` and `GetCacheServers` deliberately do **not** consult errno: their underlying
functions never touch it, so a stale value from an earlier failure used to be reported as a failure
of a successful call. This was measurable: after a package query miss,
`GetServers()` threw `AlpmPackageException(ALPM_ERR_PKG_NOT_FOUND)`, and the same call succeeded again
after any unrelated call that happened to clear the errno.

**Considered options.** Probing errno after every call was the status quo; it is order-dependent and
was rejected. Never probing was also rejected: `alpm_db_get_pkgcache` reports a missing or corrupt
database file through errno, and dropping the check would turn that into a silently empty cache.
`Alpm.Errno` and `Alpm.GetCurrentError` remain public for diagnostics, but no control flow in the
library depends on them.

**Consequences.** "Which libalpm functions reset errno" stops being knowledge the wrapper needs: the
rule only asks whether a return value signalled failure. A future libalpm release that changes when a
function clears errno cannot make these paths report the wrong error, because the successful paths no
longer read it. The residual is the two reset-dependent probes, which are covered by tests pinning
both directions (a missing database still throws; a stale errno is not reported).
