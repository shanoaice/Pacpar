# Pacpar.Benchmarks

BenchmarkDotNet suite for `Pacpar.Alpm`: it measures the cost model behind lazy property access
versus eager managed snapshots, the null-miss caching loop, and `AlpmList<T>` traversal.

## Requirements

- .NET 10 SDK.
- A pacman database to measure against (any Arch-based host, or a copy of one). All benchmarks
  are **read-only**; they never modify the database.

By default the suite uses `/` and `/var/lib/pacman`. Point it elsewhere through the environment:

| Variable | Default | Meaning |
| :--- | :--- | :--- |
| `PACPAR_BENCH_ROOT` | `/` | Root directory handed to libalpm |
| `PACPAR_BENCH_DBPATH` | `/var/lib/pacman` | Pacman database path (`local/` + `sync/`) |

## Running

```sh
dotnet run -c Release --project src/Pacpar.Benchmarks -- --filter "*"
```

A single group, e.g. the cold-load ladder:

```sh
dotnet run -c Release --project src/Pacpar.Benchmarks -- --filter "*ColdLoad*"
```

Reports land in `BenchmarkDotNet.Artifacts/results/` (markdown, CSV, HTML).

## Benchmark groups

| Class | Question it answers |
| :--- | :--- |
| `WarmBulkBenchmarks` | What do lazy views vs eager snapshots cost purely in managed code during `pacman -Q`/`-Qs`/`-Ss`-style scans? |
| `ColdLoadBenchmarks` | What does libalpm's lazy loading actually cost on first access (`INFRQ_BASE` -> `INFRQ_DESC` -> `INFRQ_FILES`)? |
| `NullMissBenchmarks` | How expensive is re-entering the P/Invoke boundary for an absent property, and what does the bool-flag fix buy? |
| `ListTraversalBenchmarks` | What does one dependency-list pass cost: streaming the enumerator, re-reading the property, or materializing with `ToArray`? |
| `QuestionPayloadLifetimeCostBenchmarks` | What does fixing the question payload's lifetime granularity cost per package: resolving the owning-database token (nanoseconds, no garbage) or copying the package eagerly into a `PackageSnapshot` (microseconds to milliseconds, with the file list as the expensive part)? |

## Methodology

Two pitfalls dominate this benchmark space, and the suite is shaped around them:

1. **libalpm's lazy loads happen only once per package.** `LAZY_LOAD` parses `/desc`/`/files`
   when the field is first touched and never again, so a conventional warm-up silently measures
   the *cached* path. The warm group therefore forces every native load in `GlobalSetup` and
   reports strictly warm numbers, while the cold group hands each iteration a fresh libalpm handle
   (`IterationSetup` + `RunOncePerIteration`) to measure the first-touch parse ladder honestly.
2. **Single-shot timings are noise.** Everything runs under BenchmarkDotNet with warm-up and
   measured iterations, `MemoryDiagnoser` for allocations and GC counts, and ratios against a
   declared baseline.

Caveats: the cold group keeps the OS page cache warm, so its numbers isolate parse + allocation
cost rather than raw disk latency; absolute values are machine-dependent, so compare ratios rather
than milliseconds across machines.

## Notes

- `PackageSnapshot.cs` exposes the eager snapshot prototypes behind `PackageBase.ToSnapshot()`;
  the benchmarks here measure what they cost.
- `ListTraversalBenchmarks` picks whichever local package has the most dependencies, so the
  element count varies between machines. It also doubles as a regression check for the optimized
  `AlpmList<T>.ToArray` single-pass traversal.
