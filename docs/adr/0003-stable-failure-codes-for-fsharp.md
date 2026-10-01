# Give F# a stable semantic failure contract

---
status: accepted
---

The F# public error unions are a language-facing contract, while the generated `_alpm_errno_t` enum is a native binding contract that can change when libalpm changes. The internal C# bridge therefore reports a stable `AlpmFailureCode` alongside the numeric native code and a typed, managed failure payload. F# maps that value to operation-specific discriminated unions; an unrecognized but valid native code becomes a generic unknown-native failure instead of becoming an unmatchable exception.

## Considered Options

Exposing `_alpm_errno_t` directly would make F# code compile against generated binding names and couple every public error case to a native header change. Keeping only broad categories would be easier to maintain but would lose useful cases such as `PackageNotFound`, `MissingDependencies`, and `FileConflicts`. Mapping raw codes in F# would put native semantics and payload interpretation back in the language wrapper, contrary to the boundary in ADR 0002.

## Consequences

The C# failure model keeps the exact native numeric code for diagnostics and forward compatibility, while the semantic code is exhaustively tested against every errno known to the bindings. New native codes fall back to `UnknownNative` until the semantic mapping is intentionally extended. F# public unions can evolve by adding cases without promising the generated C enum as their ABI; the C# and F# assemblies must still be released together while the internal bridge is in use.
