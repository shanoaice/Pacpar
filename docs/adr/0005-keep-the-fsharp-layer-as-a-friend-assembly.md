# Keep the F# layer as a friend assembly of the C# binding

---
status: accepted
---

`Pacpar.Alpm.FSharp` reaches the C# failure-returning core through `InternalsVisibleTo`
([ADR 0002](./0002-fsharp-error-boundary.md)). We considered promoting that bridge to public API and
splitting it into a separate contract assembly, and rejected both: the F# project already depends on
`Pacpar.Alpm`, so a third assembly would add build complexity without removing the coupling, and a
public bridge would freeze an internal shape before there is evidence that any C# consumer wants it.
The friend-assembly dependency is therefore permanent, not a pre-1.0 expedient.

**Consequences.** `Pacpar.Alpm.FSharp` can see every internal in `Pacpar.Alpm`, not only the bridge,
so that boundary is held by test and review rather than by the compiler. The two packages must always
be released together. If `Pacpar.Alpm` is ever strong-named, the `InternalsVisibleTo` entry needs the
matching public key.
