using BenchmarkDotNet.Attributes;
using Pacpar.Alpm;
using Pacpar.Alpm.List;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Dependency-list access costs on the local package with the most dependencies. AlpmList&lt;T&gt;
/// is a forward-only <see cref="IEnumerable{T}"/> view (its O(N^2) indexer was removed in commit
/// 06b14e6), so one pass over a list can be streamed through the enumerator, materialized with
/// <c>ToArray</c>, or preceded by a fresh property read that allocates a new view wrapper.
/// </summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(12)]
public class ListTraversalBenchmarks
{
  private AlpmHandle _alpm = null!;
  private PackageView _pkg = null!;
  private AlpmList<Depend> _depends = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    int best = -1;
    PackageView? bestPkg = null;
    foreach (var pkg in _alpm.GetLocalDatabase().GetPackageCache())
    {
      int c = 0;
      foreach (var _ in pkg.Depends) c++;
      if (c > best)
      {
        best = c;
        bestPkg = pkg;
      }
    }
    _pkg = bestPkg!;
    _depends = _pkg.Depends;
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  /// <summary>Streams a cached view through the enumerator.</summary>
  [Benchmark(Baseline = true)]
  public int Depends_Enumerate_CachedView()
  {
    int total = 0;
    foreach (var d in _depends)
    {
      total += d.Name?.Length ?? 0;
    }
    return total;
  }

  /// <summary>Reads the list property before streaming it: a new view wrapper per read.</summary>
  [Benchmark]
  public int Depends_Enumerate_PropertyRead()
  {
    int total = 0;
    foreach (var d in _pkg.Depends)
    {
      total += d.Name?.Length ?? 0;
    }
    return total;
  }

  /// <summary>Materializes the view with ToArray, then loops over the managed copy.</summary>
  [Benchmark]
  public int Depends_ToArray_CachedView()
  {
    var arr = _depends.ToArray();
    int total = 0;
    for (int i = 0; i < arr.Length; i++)
    {
      total += arr[i].Name?.Length ?? 0;
    }
    return total;
  }
}
