# Ship no escape hatch into the binding layer

---
status: proposed
---

The generated bindings and the hand-written `SafeHandle` overloads in `Pacpar.Alpm.Bindings` are to
become internal, rather than staying public as a namespace documented "unsafe". An escape hatch is
not a neutral convenience: using it mutates native state that the managed layer cannot observe,
which breaks the lifetime and ownership invariants the rest of the wrapper is built to maintain, and
supporting it honestly would mean publishing a cascade of unstable types — `SafeAlpmHandle`,
`Lifetime`, the marshalling helpers — under a promise the project has no intention of keeping.
Consumers who genuinely need raw access can declare their own P/Invoke.

Mechanically this is one line. `csbindgen`'s class accessibility is configured in
`libalpm-sys-cs/build.rs` (`.csharp_class_accessibility("public")`), so the generated class changes at
the generator instead of by editing generated output — which is also the one file in that helper
project that may be edited at all.

**Consequences.** Every public member that currently names a binding type has to change: `Alpm.Errno`,
`AlpmException.Errno`, and the public exception constructors that take `_alpm_errno_t`. The public
error surface should adopt the stable `AlpmFailureCode` plus numeric native code vocabulary that
[ADR 0003](./0003-stable-failure-codes-for-fsharp.md) already defines. `PublicApiSurfaceTests` can
then stop skipping the `Pacpar.Alpm.Bindings` namespace and become a real guard.
