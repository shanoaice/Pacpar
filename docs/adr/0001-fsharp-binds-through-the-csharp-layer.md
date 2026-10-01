# F# binds through the C# layer, not through its own interop

`Pacpar.Alpm.FSharp` is built on top of the `Pacpar.Alpm` C# binding rather than declaring a
parallel P/Invoke layer of its own. F# lacks the plumbing needed to express a clean and performant
native binding, and concentrating the native ABI in one assembly keeps ownership, lifetime, and
marshalling rules single-sourced. The F# layer owns ergonomics and error presentation only —
including re-expressing errno-style failures as `Result` — and does not own the native boundary.

**Consequences.** The F# package consumes both the C# public object model and — through the
friend assembly decided in [ADR 0002](./0002-fsharp-error-boundary.md) — the C# internal failure
bridge. So an "internal" C# refactor is not invisible: it is observable by F#. The two packages
must be released together, and the C# public API is a compatibility surface for both audiences.

## See also

- [ADR 0002: failure boundary](./0002-fsharp-error-boundary.md)
