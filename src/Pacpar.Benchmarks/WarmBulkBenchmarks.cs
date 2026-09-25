using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pacpar.Alpm;
using Pacpar.Alpm.List;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Warm-cache bulk workloads (pacman -Q / -Qs / -Ss equivalents). The GlobalSetup forces every
/// native lazy load (INFRQ_DESC, INFRQ_FILES) up front, so the measured runs isolate the managed
/// cost of "lazy view vs eager snapshot" without any disk I/O. The native lazy-load parse cost is
/// measured separately in <see cref="ColdLoadBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class WarmBulkBenchmarks
{
  private const string LocalQuery = "system";
  private const string SyncQuery = "python";

  private AlpmHandle _alpm = null!;
  private AlpmList<Package> _localPkgs = null!;
  private AlpmList<Package> _syncPkgs = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    var localDb = _alpm.GetLocalDatabase();
    _localPkgs = localDb.GetPackageCache();
    _syncPkgs = BenchEnvironment.RegisterSyncDatabase(_alpm).GetPackageCache();

    // Touch everything once so every native lazy load has already happened.
    foreach (var pkg in _localPkgs)
    {
      _ = pkg.ToSnapshot();
      _ = pkg.Files.Count;
    }
    foreach (var pkg in _syncPkgs)
    {
      _ = pkg.Name;
      _ = pkg.Description;
    }
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Bulk")]
  public int Bulk_Lazy_NameVersion()
  {
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      var name = pkg.Name;
      var ver = pkg.Version.ToString();
      if (name.Length > 0 && ver.Length > 0) count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Bulk")]
  public int Bulk_Eager_MetadataSnapshot()
  {
    var snapshots = new List<PackageSnapshot>();
    foreach (var pkg in _localPkgs) snapshots.Add(pkg.ToSnapshot());
    return snapshots.Count;
  }

  [Benchmark]
  [BenchmarkCategory("Bulk")]
  public int Bulk_Eager_FullSnapshot_InclFiles()
  {
    var snapshots = new List<PackageSnapshot>();
    foreach (var pkg in _localPkgs) snapshots.Add(pkg.ToSnapshot(includeFiles: true));
    return snapshots.Count;
  }

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Search-Local")]
  public int Search_Local_Lazy_NameDesc()
  {
    int matches = 0;
    foreach (var pkg in _localPkgs)
    {
      if (pkg.Name.Contains(LocalQuery, StringComparison.OrdinalIgnoreCase) ||
          (pkg.Description != null && pkg.Description.Contains(LocalQuery, StringComparison.OrdinalIgnoreCase)))
      {
        matches++;
      }
    }
    return matches;
  }

  [Benchmark]
  [BenchmarkCategory("Search-Local")]
  public int Search_Local_Eager_SnapshotFirst()
  {
    var snapshots = new List<PackageSnapshot>();
    foreach (var pkg in _localPkgs) snapshots.Add(pkg.ToSnapshot());
    int matches = 0;
    foreach (var s in snapshots)
    {
      if (s.Name.Contains(LocalQuery, StringComparison.OrdinalIgnoreCase) ||
          (s.Description != null && s.Description.Contains(LocalQuery, StringComparison.OrdinalIgnoreCase)))
      {
        matches++;
      }
    }
    return matches;
  }

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Search-Sync")]
  public int Search_Sync_Lazy_NameDesc()
  {
    int matches = 0;
    foreach (var pkg in _syncPkgs)
    {
      if (pkg.Name.Contains(SyncQuery, StringComparison.OrdinalIgnoreCase) ||
          (pkg.Description != null && pkg.Description.Contains(SyncQuery, StringComparison.OrdinalIgnoreCase)))
      {
        matches++;
      }
    }
    return matches;
  }

  [Benchmark]
  [BenchmarkCategory("Search-Sync")]
  public int Search_Sync_Eager_SnapshotFirst()
  {
    var snapshots = new List<PackageSnapshot>();
    foreach (var pkg in _syncPkgs) snapshots.Add(pkg.ToSnapshot());
    int matches = 0;
    foreach (var s in snapshots)
    {
      if (s.Name.Contains(SyncQuery, StringComparison.OrdinalIgnoreCase) ||
          (s.Description != null && s.Description.Contains(SyncQuery, StringComparison.OrdinalIgnoreCase)))
      {
        matches++;
      }
    }
    return matches;
  }
}
