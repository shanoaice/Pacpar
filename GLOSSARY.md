# Pacpar

Pacpar wraps libalpm for .NET and adds a functional-first F# surface over the same native library.

## Language

**ALPM operation failure**:
An operation requested from libalpm was rejected because of package, database, dependency, retrieval, or system conditions.
_Avoid_: exception, errno

**Query miss**:
A lookup completed normally but found no matching package or group.
_Avoid_: not-found failure, lookup error

**Failure detail**:
Structured evidence attached to an ALPM operation failure, such as missing dependencies or conflicting files.
_Avoid_: raw error payload

**Session**:
One live use of libalpm: the object that owns the native handle and is the entry point for databases and transactions.
_Avoid_: handle, context, Alpm

**Package view**:
A package whose memory libalpm owns. It is a window onto the session, database, or transaction that produced it, so it is only readable while that scope is alive.
_Avoid_: package, package reference

**Loaded package**:
A package this library read from a package file and owns outright. It can hand that ownership to a transaction.
_Avoid_: local package, file package

**Package snapshot**:
A managed copy of a package's metadata that stays valid independently of libalpm's memory.
_Avoid_: package copy, clone, detached package

**Package base**:
The read-only surface every package kind shares; the parameter type for code that only reads.
_Avoid_: IPackage
