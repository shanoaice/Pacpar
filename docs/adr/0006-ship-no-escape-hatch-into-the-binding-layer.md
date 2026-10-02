# Ship no escape hatch into the binding layer

---
status: accepted
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

## Implementation notes (2026-10-02)

Landed. What the change actually turned out to require:

- **One line at the generator**, as predicted: `.csharp_class_accessibility("internal")` in
  `libalpm-sys-cs/build.rs`. `csbindgen` applies that accessibility to every emitted item, so all 17
  enums and every struct, fixed buffer and the `NativeMethods` class itself became internal together.
  The hand-written `bindings/NativeMethods.SafeHandle.cs` partial had to match, since partial
  declarations may not disagree about accessibility.
- **The public error surface moved to `int`**, following [ADR 0009](./0009-public-errors-expose-a-numeric-native-code.md)
  rather than introducing `AlpmFailureCode` here: `Alpm.Errno`, `AlpmException.Errno` and the public
  exception constructors now take and return the raw `int`. The semantic-code half of ADR 0003 stays
  with the F# bridge, which does not exist yet; inventing the enum now would freeze a shape with no
  consumer. Messages keep the readable `ALPM_ERR_*` name through a new `ErrorHandler.NameOf(int)`.
- **`AlpmQuestion` needed a real edit, not just a type swap.** Its primary constructor was
  `protected` (the compiler makes an abstract type's primary constructor protected), and a protected
  member of a public class is public API, so it could not keep naming an internal type. It is now an
  explicit `private protected` constructor plus a `private protected` field - constructible by this
  assembly's subclasses, which is all of them.
- **In-repo tooling kept friend access.** `Pacpar.Alpm.Tests` already had it; `Pacpar.Benchmarks` was
  added, because its marshalling benchmarks call `alpm_pkg_vercmp` directly on purpose. The ADR's
  "consumers declare their own P/Invoke" reasoning is about consumers; the benchmarks are measuring
  this wrapper's own binding.
- **The guard test became real.** `PublicApiSurfaceTests` no longer skips the binding namespace, and a
  new test pins `Pacpar.Alpm.Bindings` as unexported so a libalpm upgrade plus regeneration cannot
  quietly widen the surface again.

Verified: the whole solution builds, and the host-side suite (integration filtered out) passes with
168 tests.
