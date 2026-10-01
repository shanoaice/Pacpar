# One errno maps to one public exception type

---
status: accepted
---

The same libalpm errno currently surfaces as two different public exception types depending on which
entry point observes it. `ErrorHandler.Categorize` classifies `ALPM_ERR_PKG_INVALID`,
`ALPM_ERR_PKG_INVALID_CHECKSUM`, `ALPM_ERR_PKG_INVALID_SIG` and `ALPM_ERR_PKG_INVALID_ARCH` as package
errors, so the generic path raises `AlpmPackageException`; `AlpmTransactionException.TakeFailure`
raises the payload-carrying subclasses `InvalidPackage`, `InvalidPackageChecksum`,
`InvalidPackageSignature` and `InvalidPackageArchitecture` for those same four errnos. The
payload-carrying subclasses are canonical and classification moves to match them, so one errno has
exactly one public exception type whichever entry point reports it.

The cost is explicit and accepted now, before the v1 stability promise: a caller doing
`catch (AlpmPackageException)` around `Alpm.GetCurrentError()` stops matching for these four. The
alternative is a failure bridge ([ADR 0002](./0002-fsharp-error-boundary.md)) that cannot express one
failure code per errno at all.

**Consequences.** The exception type answers "which operation failed"; what is wrong travels in the
typed payload, and `Context` stays the human-readable description. A test must pin the mapping from
both entry points, because today only the `TakeFailure` side is covered and the classification test
asserts nothing stronger than "not unknown".
