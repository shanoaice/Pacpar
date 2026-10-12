# Enums sit with their primary consumer, and value-bound enums keep libalpm's numbering

---
status: proposed
---

Two decisions about enums, both recorded because neither is derivable from the Framework Design
Guidelines alone.

1. **Placement.** An enum lives in the file of its primary consumer. An enum that two or more
   feature areas use gets its own file. We do not collect enums into a shared drawer.
2. **Values.** An enum whose members mirror libalpm constants keeps libalpm's numbering. We do not
   add a zero member to such an enum, and we do not renumber one.

## Placement: the file is not in scope for the FDG, so we fix it ourselves

The FDG governs the public API surface: namespaces, type granularity, nesting, enum design. It says
nothing about which file a type belongs in. That is a repository convention, and an unwritten
convention loses to the next urge to tidy. ADR 0013 records what happens then: a mechanical
"turn the getters into properties" pass moved `Database.PackageCache` to the wrong shape and broke
the benchmarks build. We write this one down for the same reason.

The rule follows from the one FDG rule that does apply — *organize types into related feature
areas*:

- **One consumer** — put the enum in that consumer's file. `TransactionFlags` lives in
  `Transaction.cs` because `Transaction` is the only type that uses it.
- **Two or more feature areas** — give the enum its own file, named after the enum.

The companion rule is negative: **do not build `XxxEnums.cs` as a central drawer.** A drawer
inverts the grouping. It puts types from different feature areas in one file on the strength of
them all being enums, which is a property of the C# type system and not of the domain.

### Resulting layout

| Enum | Home | Why |
|---|---|---|
| `TransactionFlags` | `Transaction.cs` | One consumer |
| `ProgressType`, `LogLevel`, `FetchResult` | `Callback.cs` | Callback payloads only |
| `DepMod` | `Dependencies.cs` | Ships with `Depend` |
| `PackageOrigin`, `PackageReason`, `PackageValidation` | `Package/PackageEnums.cs` | Package-only |
| `HookWhen`, `PackageOperation` | `Events/AlpmEvent.cs` | Event payloads only |
| `SandboxState` | `Options.cs` | One consumer, `AlpmOptions.Sandbox` |
| `Capability` | `AlpmSession.cs` | One consumer, `AlpmSession.Capabilities` |
| `DatabaseUsage` | `Database.cs` | One consumer, `Database.Usage` |
| `SigLevel` | `SigLevel.cs` | Shared by session options, database registration and package loading |

The rule does not stop at public enums. `AlpmFailureCode` and `AlpmErrorCategory` sit in
`AlpmFailure.cs` because `AlpmFailure` is their only consumer, and `Transaction.CompletionState` is
a private nested enum because nothing outside `Transaction` names it.

`Capability` and `DatabaseUsage` used to sit in `Package/PackageEnums.cs` while belonging to the
session and the database. `SigLevel` used to sit in a file called `Signatures.cs`, whose name
promised a feature that does not exist.

## Values: a bound enum is a projection, not a design of its own

FDG 4.8 asks for a zero member on every enum and asks that flag enums name it `None`. That rule
assumes the enum is a vocabulary we design. Most enums here are not. `SigLevel`, `DepMod`,
`PackageOrigin`, `PackageOperation`, `HookWhen`, `PackageValidation`, `TransactionFlags`,
`Capability` and `DatabaseUsage` project a libalpm constant directly:

```csharp
public enum PackageOrigin : uint
{
  File = 1,          // ALPM_PKG_FROM_FILE
  LocalDatabase = 2, // ALPM_PKG_FROM_LOCALDB
  SyncDatabase = 3   // ALPM_PKG_FROM_SYNCDB
}
```

The member values cross the FFI boundary as arguments and return values. They must equal what
libalpm defines, or the call is wrong. Adding a zero member to such an enum buys a nicer
`default(T)` and costs three things:

1. **It invents a value libalpm does not have.** Either we pass an undefined constant to native
   code, or every boundary translates zero into something real. Both are worse than having no zero
   member.
2. **It makes the mapping a second source of truth.** Every libalpm upgrade needs a recheck, and a
   missed recheck is a silent argument mismatch rather than a compile error.
3. **It works against ADR 0009.** That ADR already established the separation: libalpm's numbering
   stays in the binding layer, and consumers that need stability match on a semantic layer of our
   own — `AlpmFailureCode`. Stability belongs in that layer, not in the projection.

So: **whether a bound enum has a zero member is decided by libalpm, not by us.** The enums that do
have one are the ones where libalpm defines it — `PackageReason.Explicit = 0`, `ProgressType.AddStart
= 0`, `FetchResult.Success = 0`. The ones that start at 1 describe native sets that start at 1, and
their `default(T)` is an undefined value on purpose.

Two enums are not bound and do follow the FDG: `SandboxState` and `AlpmFailureCode` are ours, and
both carry a zero member.

### One deliberate deviation, kept

`TransactionFlags` has no `None = 0`. Its zero means libalpm's fully-checked default mode, not
"nothing set". Naming it `None` would tell a caller that all checks are off when all checks are on,
which is the most dangerous reading available. The class remarks on `AlpmSession.BeginTransaction`
state what zero means. Do not add `None` to satisfy the FDG.

## Member names: FDG casing, no repeated prefix, native name in the doc

Member names follow the FDG — PascalCase, and no prefix that repeats the type. The native constant
moves into the member's `<summary>`, so the projection stays traceable without carrying the prefix
in the identifier:

```csharp
// before
AlpmTransFlagNodeps = 1,
ALPM_PKG_VALIDATION_MD5SUM = 2,

// after
/// <summary>Ignore dependency checks (<c>ALPM_TRANS_FLAG_NODEPS</c>).</summary>
NoDeps = 1,
/// <summary>The package is validated with md5 (<c>ALPM_PKG_VALIDATION_MD5SUM</c>).</summary>
Md5Sum = 2,
```

Renaming a member does not touch its value, so this is independent of the decision above and is
safe to do on a bound enum.

**Type names do not take the FDG plural rule.** `SigLevel`, `Capability`, `DatabaseUsage` and
`PackageValidation` stay singular and close to the native typedef. Pluralizing them would buy FDG
compliance and cost the one-to-one correspondence with `alpm.h`, which is the header we document
against and the one a reader has open. Where the FDG and the native name disagree, the native name
wins on the type and the FDG wins on the members.

One member keeps a name the FDG would object to: `PackageValidation.None` is `1`, not `0`, because
libalpm puts it there — `Unknown` is the zero member. The remarks on the enum say so, so a caller
does not read `None` as an empty flag set.

## Consequences

- Moving an enum means checking its consumers first. One consumer: move it in. Two or more: give it
  a file.
- A new libalpm enum arrives with libalpm's numbering, including the absence of a zero member.
  Record the native name in a comment on each member.
- Where consumers need a stable vocabulary, add a semantic enum of our own and map to it — the ADR
  0009 pattern — rather than editing the projection.
- Member names take the FDG casing and drop the repeated type prefix; the native constant goes into
  the `<summary>`. Type names keep the native singular form. See *Member names* above.
