# Rename the root type to `AlpmSession`

---
status: proposed
---

The root wrapper type is named `Alpm`, which stutters against its own namespace and assembly
(`Pacpar.Alpm.Alpm`) and leaves prose no clean way to distinguish "the ALPM library" from "the
object you hold a handle to". The concept is a session: a scoped, single-threaded connection that
owns the native handle and is the entry point for databases and transactions. We will rename the
public type to `AlpmSession` and reserve "handle" for the raw `alpm_handle_t` and the internal
`SafeAlpmHandle`.

The rename breaks the public C# API, so it must land before the v1 stability promise. It is
deliberately deferred until the current design audit finishes, because that audit may move the
type's ownership and lifetime boundaries enough to force a second rename.

**Consequences.** `GLOSSARY.md` already lists "Alpm" as an avoided word for this concept, so prose
and new code do not deepen the old name while the rename is pending.
