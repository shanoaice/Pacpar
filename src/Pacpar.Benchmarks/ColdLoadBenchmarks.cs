using BenchmarkDotNet.Attributes;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Cold native lazy-load ladder. Each iteration gets a freshly created libalpm handle whose
/// package cache has NOT been populated yet (IterationSetup only registers the database), so the
/// measured action is the first, uncached read path: INFRQ_BASE population (readdir) and the
/// LAZY_LOAD parses of /desc and /files. This is what a naive warmup hides. OS page cache stays
/// warm across iterations, so the numbers isolate parse + allocation cost, not raw disk latency.
/// </summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class ColdLoadBenchmarks
{
  private AlpmHandle _alpm = null!;
  private Database _localDb = null!;

  [IterationSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    _localDb = _alpm.GetLocalDatabase();
  }

  [IterationCleanup]
  public void Cleanup() => _alpm.Dispose();

  /// <summary>Package cache population (readdir + name/version from directory names) plus one enumeration pass.</summary>
  [Benchmark]
  [RunOncePerIteration]
  public int Cold_PopulateAndEnumerateCache() => _localDb.GetPackageCache().ToArray().Length;

  /// <summary>Population + name and version reads. Still no file on disk is opened.</summary>
  [Benchmark]
  [RunOncePerIteration]
  public int Cold_PopulateAndReadNameVersion()
  {
    var count = 0;
    foreach (var pkg in _localDb.GetPackageCache())
    {
      _ = pkg.Name;
      _ = pkg.Version.ToString();
      count++;
    }
    return count;
  }

  /// <summary>Triggers LAZY_LOAD(INFRQ_DESC) for every package: opens + parses every /desc.</summary>
  [Benchmark]
  [RunOncePerIteration]
  public int Cold_PopulateAndReadAllMetadata()
  {
    int n = 0;
    foreach (var pkg in _localDb.GetPackageCache())
    {
      _ = pkg.ToSnapshot();
      n++;
    }
    return n;
  }

  /// <summary>Triggers LAZY_LOAD(INFRQ_FILES) for every package: opens + parses every /files.</summary>
  [Benchmark]
  [RunOncePerIteration]
  public long Cold_PopulateAndReadAllFileLists()
  {
    long entries = 0;
    foreach (var pkg in _localDb.GetPackageCache()) entries += pkg.Files.Count;
    return entries;
  }
}
