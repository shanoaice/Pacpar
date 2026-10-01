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
