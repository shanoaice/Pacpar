# Public errors expose a numeric native code, not a generated type

---
status: accepted
---

Once the binding layer is internal ([ADR 0006](./0006-ship-no-escape-hatch-into-the-binding-layer.md)),
the generated `_alpm_errno_t` can no longer appear in a public signature. The public error contract
keeps a plain numeric native code — the value of libalpm's `pm_errno` — and exposes it as an `int`
documented as "consult `alpm.h`", with no stability promise about its names or values across libalpm
versions. The exception hierarchy and its typed payload remain the contract a consumer matches on;
the number exists so that a failure can be distinguished or reported to libalpm when the hierarchy
deliberately collapses two cases.

This was chosen over a hand-written `AlpmErrno` enum mirroring the generated one. A mirror would read
more naturally but would be a second copy of libalpm's taxonomy that the project would then have to
keep in sync, which is the maintenance burden the internal binding layer exists to avoid.

**Consequences.** An `int` promises nothing, which is the honest position given that libalpm's errno
set is whatever Arch rolling ships. Consumers who want a typed failure must use the F# layer, whose
typed DTOs are built from the internal bridge rather than from these numbers.
