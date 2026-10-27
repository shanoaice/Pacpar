# A child lifetime domain owns facets, and a stamp stays two levels deep

---
status: accepted
---

A database has parts that are released independently of it. `alpm_db_set_servers` frees the list
it replaces, `alpm_db_add_server` assigns a new list head through `alpm_list_add`, and
`alpm_db_remove_server` frees the matching node through `alpm_list_remove_str`, which may also
replace the head. A view taken before one of those calls walks a list that libalpm has already
freed or re-linked. The wrapper handled that by invalidating the whole database domain, which also
retired every package view issued from it, although nothing about the packages had changed.

The decision is that such a part is a **facet**: a child domain in its own right, owned by the
domain of the resource it belongs to, and retired by the operation that releases it. The database's
server list and its cache-server list are the first two facets. A facet is issued by
`ChildLifetime.Facet`, retired by `InvalidateFacet`, and cascaded into when its owner is
invalidated.

The stamp stays two levels deep. A facet's stamp records the facet and the session root, exactly as
a child domain's stamp records the domain and the root. It does not record the owner, because the
owner's invalidation cascades into the facet and the root comparison already covers the session.
Invalidating the server list therefore costs one increment, and checking a server-list view still
costs two integer comparisons. When the database itself goes away, `ChildLifetime.Invalidate`
retires every facet before the caller frees the native memory, so no facet outlives the memory it
describes. The facet is drawn below its owner because that is where its memory comes from, not
because a check walks through the owner.

Facets are stored in a lazily allocated array indexed by `LifetimeFacet`. The enum's values are
array indexes, not identities: they stay dense and zero-based, and `Count` stays last. Reordering is
safe because every call site names its member; a value at or beyond `Count` raises
`IndexOutOfRangeException` instead of silently addressing another facet. Adding a facet therefore
costs an enum member, the facet passed at the read site, and `InvalidateFacet` at the write site. It
adds no field to the type, does not touch the release path, and does not affect the handle registry.

## Considered Options

**A third stamp level.** Give every child domain a fixed slot for its owner, so a stamp compares the
root, the owner, and the resource. That is 48 bytes instead of 32 for *every* view, and a third
comparison on *every* check, including the package views that have no facet beneath them. It also
makes the depth a choice this library makes rather than a fact it reports, and libalpm has exactly
one such owner-of-an-owner relationship: the server lists are fields of the database struct.

**A token chain.** Each stamp holds its ancestor chain and walks it. This was the previous design,
and it was replaced deliberately: it allocates once per view, and `GetPackageCache` issues one view
per package.

**A registry keyed by (handle, facet).** The root would hold one entry per facet. Releasing a
database would then have to invalidate a group of entries, which is a second invalidation path that
is easy to forget and hard to test. The array hangs the facets off the domain they belong to, so the
release path is the same code that already retires the domain.

**A keyed collection inside the child domain.** A dictionary costs an allocation and a hash lookup
per read, for at most a handful of entries. A separate `FacetSet` class adds a layer of indirection
and an allocation for the same reason.

## Consequences

- A read path calls `Lifetime.Facet`; a write path calls `Lifetime.InvalidateFacet`. A view taken
  before the write reports the write through `AlpmLifetimeException`, and a database that is only
  having its server list rewritten keeps its package views.
- Each child domain costs one reference (null until a facet is issued), one array per domain that
  issues facets, and one small object per facet. A facet call is one array index; an invalidation of
  a domain without facets is one null check.
- Facets are created and retired on the session thread. The only cross-thread invalidation is the
  root's, which does not cascade: a dead root retires facet stamps through the root comparison, and a
  dead child domain refuses to issue a new facet.
- A facet that is never requested is never allocated, so a domain that does not need one pays only
  the reference.
