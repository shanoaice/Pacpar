using BenchmarkDotNet.Attributes;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// The null-miss loop of <c>field ??=</c> versus the bool-flag fix (Recommendation 3 of the audit
/// report). Each benchmark is one 50,000-read loop over a single property.
/// </summary>
/// <remarks>
/// The library now caches with a flag on every property that can be absent, so the pair below is the
/// fixed path on an absent property (<see cref="PackageBase.Filename"/>) against the coalescing path on
/// one libalpm always sets (<see cref="PackageBase.Name"/>). The pre-fix comparison - the same loop over
/// the old coalescing implementation, 91.67 us against 10.04 us - is recorded in section 4.3 of the
/// report; this class is kept as the regression check for the fix.
/// </remarks>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(12)]
public class NullMissBenchmarks
{
  private const int Reads = 50_000;

  private AlpmHandle _alpm = null!;
  private PackageView _pkg = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    foreach (var pkg in _alpm.GetLocalDatabase().GetPackageCache())
    {
      _pkg = pkg;
      break;
    }
    _ = _pkg.Name; // warm the cached-hit path
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  /// <summary>Absent property behind a bool flag: the miss crosses the interop boundary exactly once.</summary>
  [Benchmark(Baseline = true)]
  public int AbsentProperty_BoolFlagCached()
  {
    int n = 0;
    for (int i = 0; i < Reads; i++)
    {
      if (_pkg.Filename == null) n++;
    }
    return n;
  }

  /// <summary>Cache hit on a property libalpm always sets, where coalescing has no miss to repeat.</summary>
  [Benchmark]
  public int CachedProperty_Hit()
  {
    int n = 0;
    for (int i = 0; i < Reads; i++)
    {
      if (_pkg.Name != null) n++;
    }
    return n;
  }
}
