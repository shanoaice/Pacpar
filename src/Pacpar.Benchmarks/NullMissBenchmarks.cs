using BenchmarkDotNet.Attributes;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// The null-miss loop of field ??= versus the bool-flag fix (Recommendation 3). Each benchmark is
/// one 50,000-read loop over a single property.
/// </summary>
[MemoryDiagnoser]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(12)]
public class NullMissBenchmarks
{
  private const int Reads = 50_000;

  private AlpmHandle _alpm = null!;
  private Package _pkg = null!;
  private string? _cachedFilename;
  private bool _filenameLoaded;

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

  /// <summary>Current pattern on an absent property: field ??= re-runs the P/Invoke every read.</summary>
  [Benchmark(Baseline = true)]
  public int AbsentProperty_FieldCoalesce()
  {
    int n = 0;
    for (int i = 0; i < Reads; i++)
    {
      if (_pkg.Filename == null) n++;
    }
    return n;
  }

  /// <summary>Recommended fix: bool-flag cache, absent value crosses the interop boundary once.</summary>
  [Benchmark]
  public int AbsentProperty_BoolFlagCached()
  {
    int n = 0;
    for (int i = 0; i < Reads; i++)
    {
      if (FilenameFixed == null) n++;
    }
    return n;
  }

  /// <summary>field ??= cache hit on a non-null property: pure managed read.</summary>
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

  private string? FilenameFixed
  {
    get
    {
      if (!_filenameLoaded)
      {
        _cachedFilename = _pkg.Filename;
        _filenameLoaded = true;
      }
      return _cachedFilename;
    }
  }
}
