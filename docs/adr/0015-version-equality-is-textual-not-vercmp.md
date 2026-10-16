# PackageVersion equality is textual; CompareTo stays an ordering

---
status: proposed
---

`PackageVersion` has value semantics. `Equals`, `GetHashCode` and the `==` operator compare the
copied version text ordinally. `CompareTo` keeps answering with `alpm_pkg_vercmp` and is **not**
equality.

1. **Equality is the text.** Two instances are equal when their version strings are ordinally equal.
2. **Ordering is libalpm's, and is not equality.** `CompareTo` returns whatever `alpm_pkg_vercmp`
   returns; its 0 means "libalpm reports these as the same version", which is a looser relation than
   equality and must never be substituted for it.
3. **The type is sealed** so that equality cannot be broken by a subtype.

## Why equality has to exist at all

`PackageBase.Version` builds a new `PackageVersion` on every read. Reference equality therefore
answers nothing useful: two reads of the same package's version were never equal, and
`pkgA.Version == pkgB.Version` compared object identity where the reader meant version text. A type
that is an immutable snapshot of a string is a value; it gets value semantics, and that includes the
`==` operator, which is what a reader writes first.

## Why equality is not `CompareTo == 0`

The Framework Design Guidelines ask that `CompareTo` returning 0 agree with `Equals`. Here they
cannot, because libalpm's comparison is not an ordering whose zero is transitive. Probed against
libalpm 16.0.1:

| Left | Right | `alpm_pkg_vercmp` |
|---|---|---|
| `1.0` | `1.0-1` | 0 |
| `1.0` | `1.0-2` | 0 |
| `1.0-1` | `1.0-2` | -1 |
| `1.0-1` | `1.0-01` | 0 |
| `0:1.0-1` | `1.0-1` | 0 |

The first three rows are the argument. A missing package release is not compared, numeric segments
compare numerically, and an omitted epoch is 0 - so `"1.0"` is "the same version" as both
`"1.0-1"` and `"1.0-2"`, which are not the same version as each other. That relation is not
transitive, so it is not an equivalence relation, so it cannot back `Equals`.

It also cannot back a hash code. Every member of such a class would have to share one hash value,
and the only value guaranteed to do that is a constant - which turns every `HashSet<PackageVersion>`
and `Dictionary<PackageVersion, _>` into a linear scan over native `vercmp` calls. Hashing a
projection of the string (the part before the release dash, say) is not safe either: it hands
different hashes to pairs the table reports as equal, such as `0:1.0-1` and `1.0-1`.

## Consequences

- A `HashSet<PackageVersion>` groups by text; a `SortedSet<PackageVersion>` groups by
  `CompareTo`. Both are correct for what they do, and the type remarks say so.
- Code that wants "libalpm considers these the same version" writes `CompareTo(other) == 0`. We do
  not add a second method that says the same thing.
- `PackageVersion` is sealed. Its constructor is internal, so no consumer could have subclassed it;
  sealing stops a future in-assembly subtype from making `Equals` asymmetric.

## Rejected alternatives

- **`Equals` = `CompareTo == 0`, `GetHashCode` = constant.** Contract-legal, and useless in a hash
  container.
- **Canonicalise the text before hashing** (strip a `0:` epoch, normalise the release). This
  reimplements `alpm_pkg_vercmp` and breaks the moment libalpm's comparison changes. It also cannot
  work in principle: hash plus equality encodes an equivalence relation, and the table's first three
  rows are not one.
- **Leave equality out.** The review item this ADR closes: `CompareTo` existed, equality did not,
  and `List.Contains` on versions silently compared references.
