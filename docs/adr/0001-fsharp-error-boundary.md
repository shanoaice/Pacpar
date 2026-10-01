# Keep native failure interpretation inside C#

---
status: accepted
---

The F# wrapper represents expected ALPM operation failures as operation-specific `Result` error values, while normal query misses remain `option` values or empty collections. Parameter misuse, invalid lifetime or transaction state, disposed objects, out-of-memory conditions, missing native error state, and unexpected runtime faults continue to throw. The failure-returning core remains internal to `Pacpar.Alpm` and is available to `Pacpar.Alpm.FSharp` through `InternalsVisibleTo`; it returns typed, managed failure values rather than exposing raw errno handling to F#. The existing public C# API remains exception-based and is layered over the same core.

## Considered Options

Catching the existing C# exceptions in F# is initially simpler, but retains exception construction and unwinding on expected failures and makes the F# contract depend on exception classification. Calling native bindings directly from F# would duplicate return-code, lifetime, and native payload ownership rules. A public C# `Try*` API would make the low-level contract available to more consumers, but would double the supported public surface before there is evidence that C# users need it.

## Consequences

The C# core owns errno interpretation and consumes native failure payloads into safe managed snapshots before crossing the assembly boundary. F# maps those values into public, operation-specific discriminated unions and does not expose generated binding types. Both the throwing C# facade and the F# facade must delegate to one core so their error semantics cannot drift. The core is failure-returning rather than absolutely non-throwing: contract violations and runtime faults still escape as exceptions. An unrecognized but valid libalpm errno maps to a generic ALPM operation failure for forward compatibility; `ALPM_ERR_MEMORY` is still surfaced as an out-of-memory condition. The two assemblies must be versioned together because the friend-assembly dependency is intentionally not part of either package's public API. Any performance claim about avoiding exceptions should be measured with BenchmarkDotNet on a representative expected-failure workload.
