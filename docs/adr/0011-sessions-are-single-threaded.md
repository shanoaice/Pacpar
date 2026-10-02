# A session is single-threaded, and the contract is stated in one place

---
status: accepted
---

libalpm does not make a handle safe to use from several threads, and neither does this wrapper:
a session, the databases and transactions that hang off it, and every view they issue belong to one
thread at a time. Locking is not an option worth considering — it would be a fragile layer over a
library that was not written for it. The contract is therefore stated once, at the session, and
repeated only where libalpm crosses back into consumer code and the thread it runs on becomes
observable: the callbacks, and the questions raised during `prepare` and `commit`. Those handlers
run on libalpm's thread and block it for as long as they run, and a question is stricter than that -
it must be answered before its callback returns, because libalpm reads the answer the moment it
does, so an asynchronous answer is not an option but a bug. Everywhere else the library says nothing
about threads and assumes the contract holds; a consumer who breaks it receives no further
diagnosis, and that is deliberate.

## Considered Options

A debug-only thread-affinity assertion at every public boundary guard was considered and rejected.
It would have to be added to six separate guards, and it would still miss the option setters and the
callback-registration setters, which reach native code without consulting a guard at all — partial
coverage bought at the price of scattering thread machinery through code that is otherwise about
lifetimes. An assertion covering only the session's own entry points would catch the common mistake,
but not a view handed to another thread, so it would buy confidence it cannot back up.

## Consequences

Thread misuse is not diagnosed, only documented. The upside is that a reader meets the rule once, in
the place where they enter the library, instead of on every type they touch — and the rule is
impossible to enforce without the locking that was rejected here.
