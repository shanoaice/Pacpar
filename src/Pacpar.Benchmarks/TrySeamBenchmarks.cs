using BenchmarkDotNet.Attributes;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Measures the cost of the internal Try* seam against the throwing public wrapper (ADR 0002, ADR 0013).
/// The measured workload is an expected failure: reading the package cache of an unregistered/unpopulated
/// sync database, where libalpm fails and sets pm_errno (ALPM_ERR_DB_NOT_FOUND).
/// </summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class TrySeamBenchmarks
{
  private string _tempDir = null!;
  private AlpmHandle _alpm = null!;
  private Database _syncDb = null!;

  [GlobalSetup]
  public void Setup()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "pacpar-bench-tryseam-" + Guid.NewGuid().ToString("n"));
    var root = Path.Combine(_tempDir, "root");
    var dbpath = Path.Combine(_tempDir, "db");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(dbpath, "sync"));

    _alpm = new AlpmHandle(root, dbpath);
    _syncDb = _alpm.RegisterSyncDatabase("nonexistent", 0);
  }

  [GlobalCleanup]
  public void Cleanup()
  {
    _alpm.Dispose();
    if (Directory.Exists(_tempDir))
    {
      Directory.Delete(_tempDir, recursive: true);
    }
  }

  /// <summary>
  /// The throwing public wrapper: allocates an exception, captures a stack trace, and unwinds.
  /// </summary>
  [Benchmark(Baseline = true)]
  public bool Throwing_GetPackageCache()
  {
    try
    {
      _ = _syncDb.GetPackageCache();
      return true;
    }
    catch (AlpmDatabaseException)
    {
      return false;
    }
  }

  /// <summary>
  /// The internal Try* seam: returns false and an AlpmFailure object without throwing.
  /// </summary>
  [Benchmark]
  public bool TrySeam_TryGetPackageCache()
  {
    return _syncDb.TryGetPackageCache(out _, out _);
  }
}
