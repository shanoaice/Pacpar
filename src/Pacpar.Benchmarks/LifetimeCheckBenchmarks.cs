using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pacpar.Alpm;
using Pacpar.Alpm.List;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Cost of the optional strict granularity in lifetime-token checking: a check inside
/// <c>Enumerator.Current</c> (once per element) against the default check at the enumeration
/// entry only. The measured workload is the local package cache - the largest list the wrapper
/// hands out and the list every "pacman -Q"-shaped consumer walks.
/// </summary>
/// <remarks>
/// The benchmark project cannot reference <c>Pacpar.Alpm.Lifetime</c> (internal; only
/// <c>Pacpar.Alpm.Tests</c> is a friend assembly), so <see cref="Token"/> mirrors its shape
/// exactly: same volatile flag, same parent chain, same out-of-line loop-and-throw check. The
/// checks are placed where the wired-up design places them - one call per <c>Current</c>, one
/// call per property read - and the token is read from a field every time, as the enumerator
/// would read it off the list it wraps.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class LifetimeCheckBenchmarks
{
  private AlpmHandle _alpm = null!;
  private AlpmList<PackageView> _localPkgs = null!;
  private Token _handleToken = null!;
  private Token _localDbToken = null!;

  /// <summary>The <c>Lifetime</c> a list view would hold and read on every element.</summary>
  private Token _viewToken = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    _localPkgs = _alpm.GetLocalDatabase().GetPackageCache();

    // Warm every native lazy load up front, as WarmBulkBenchmarks does: the measured runs then
    // isolate managed cost (and the check) from disk I/O.
    foreach (var pkg in _localPkgs)
    {
      _ = pkg.Name;
      _ = pkg.Version.ToString();
    }

    // Mirrors the chain the wrapper builds: handle token -> local-database token.
    _handleToken = new Token("the alpm handle");
    _localDbToken = _handleToken.CreateChild("the local database");
    _viewToken = _localDbToken;
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  /// <summary>One check against a root token (no parent link): the floor. Per call.</summary>
  [Benchmark]
  [BenchmarkCategory("Check")]
  public void Check_RootToken() => _handleToken.ThrowIfStale();

  /// <summary>One check against a child token (one parent link): the shape used here. Per call.</summary>
  [Benchmark]
  [BenchmarkCategory("Check")]
  public void Check_ChildToken() => _localDbToken.ThrowIfStale();

  /// <summary>Pure traversal, entry check only: today's design (preview section 6).</summary>
  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Enumerate")]
  public int Enumerate_EntryCheckOnly()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      if (pkg is not null) count++;
    }
    return count;
  }

  /// <summary>Pure traversal, strict tier: one additional check per element.</summary>
  [Benchmark]
  [BenchmarkCategory("Enumerate")]
  public int Enumerate_CheckPerCurrent()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      _viewToken.ThrowIfStale();
      if (pkg is not null) count++;
    }
    return count;
  }

  /// <summary>Realistic scan (name + version), entry check only - the baseline to compare against.</summary>
  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Scan")]
  public int Scan_EntryCheckOnly()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      var name = pkg.Name;
      var ver = pkg.Version.ToString();
      if (name.Length > 0 && ver.Length > 0) count++;
    }
    return count;
  }

  /// <summary>Realistic scan with the strict per-element check added.</summary>
  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_CheckPerCurrent()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      _viewToken.ThrowIfStale();
      var name = pkg.Name;
      var ver = pkg.Version.ToString();
      if (name.Length > 0 && ver.Length > 0) count++;
    }
    return count;
  }

  /// <summary>Realistic scan with the wired-up per-getter check (one per property read).</summary>
  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_CheckPerGetter()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      _viewToken.ThrowIfStale();
      var name = pkg.Name;
      _viewToken.ThrowIfStale();
      var ver = pkg.Version.ToString();
      if (name.Length > 0 && ver.Length > 0) count++;
    }
    return count;
  }

  /// <summary>Both tiers together: what a fully wired strict build pays per element.</summary>
  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_CheckPerCurrentAndGetter()
  {
    _viewToken.ThrowIfStale();
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      _viewToken.ThrowIfStale();
      _viewToken.ThrowIfStale();
      var name = pkg.Name;
      _viewToken.ThrowIfStale();
      var ver = pkg.Version.ToString();
      if (name.Length > 0 && ver.Length > 0) count++;
    }
    return count;
  }
}

/// <summary>
/// A byte-for-byte mirror of <c>Pacpar.Alpm.Lifetime</c> (which is internal, so this project
/// cannot reference it): same volatile flag, same parent chain, same check. Keeping the copy
/// here is deliberate - the measured quantity is the cost of one <c>ThrowIfStale()</c> call and
/// of the extra field read that precedes it, and both live in this type's shape.
/// </summary>
internal sealed class Token
{
  private volatile bool _alive = true;

  internal Token(string target, Token? parent = null)
  {
    Target = target;
    Parent = parent;
  }

  internal string Target { get; }

  internal string? InvalidatedBy { get; private set; }

  internal Token? Parent { get; }

  internal bool IsAlive => _alive && (Parent is null || Parent.IsAlive);

  internal Token CreateChild(string target) => new(target, this);

  internal void Invalidate(string invalidatedBy)
  {
    if (!_alive) return;
    InvalidatedBy = invalidatedBy;
    _alive = false;
  }

  internal void ThrowIfStale()
  {
    for (var token = this; token is not null; token = token.Parent)
      if (!token._alive)
        throw new InvalidOperationException(
          token.Target + " was invalidated by " + (token.InvalidatedBy ?? "[unknown]"));
  }
}
