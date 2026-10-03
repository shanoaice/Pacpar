# Introduction to Pacpar.Alpm

`Pacpar.Alpm` provides high-performance, idiomatic .NET bindings for Arch Linux's `libalpm` package management C library.

## Key Design Goals

- **Zero-Allocation Bulk Performance**: Thin wrapper views (`PackageView`) directly read unmanaged package pointers without eagerly allocating managed metadata objects, delivering sub-microsecond scans across thousands of packages.
- **Deterministic Memory Safety**: A root-anchored lifetime token tree eliminates TOCTOU (time-of-check to time-of-use) finalizer race conditions and intercepts use-after-free access before invalid memory is touched.
- **Idiomatic .NET Experience**: Collections implement standard `IEnumerable<T>`, LINQ queries work seamlessly over database package caches, and snapshots can be created on demand via `.ToSnapshot()` for retention across threads or scopes.

## Documentation Overview

- [Getting Started](getting-started.md): Installation, initialization, querying packages, and managing transactions.
- [Lifetime and GC Safety](lifetime-and-gc-safety.md): Maintainer reference covering borrowed views, lifetime domains, guard enforcement, and unmanaged memory safety.
- [API Reference](xref:Pacpar.Alpm): Generated class- and member-level documentation for all public types.
