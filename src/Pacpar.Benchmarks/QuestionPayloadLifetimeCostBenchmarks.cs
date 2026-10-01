using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pacpar.Alpm;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Cost model behind the question-payload lifetime question: question views are handed the handle's
/// root token today, and the two ways of making that granularity honest are (a) resolving each
/// package's owning database token, or (b) copying the package eagerly into a
/// <see cref="PackageSnapshot"/> that carries no native pointer at all.
/// </summary>
/// <remarks>
/// <para>
/// The library took (b) - question payloads are detached snapshots now, with
/// <see cref="AlpmBindingConfig.QuestionPayloadIncludeFiles"/> controlling whether file lists are copied - so this class stands as
/// the evidence for that decision and as the guard rail for the default: the file list rows are what
/// keeps file lists excluded by default.
/// </para>
/// <para>
/// The measured delta is per package, because both designs replace the same
/// <c>new PackageView(ptr, token)</c> that the current code already pays for:
/// <list type="bullet">
/// <item><description>
/// (a) adds one <c>alpm_pkg_get_db</c> call plus one dictionary hit in the root token's pointer
/// registry (<c>Lifetime.GetLifetimeTokenForHandle</c>), which is what the view then carries.
/// </description></item>
/// <item><description>
/// (b) adds a full metadata copy - every scalar property plus the licenses/groups/depends/
/// optional-depends/conflicts/provides lists - and allocates for all of it.
/// </description></item>
/// </list>
/// The workload is the local package cache, which is the conservative choice: local packages carry
/// install date, reason and validation, so their snapshot is the <i>most</i> expensive one libalpm
/// can hand to a question, while the <c>alpm_pkg_get_db</c> call costs the same for any db-owned
/// package. Every native lazy load is forced in <see cref="Setup"/>, so the numbers isolate the
/// copy itself; the <c>WithFiles</c> rows add the file list, which a question payload never needs,
/// and therefore also pay its one-time parse.
/// </para>
/// <para>
/// The benchmark project cannot reference <c>Pacpar.Alpm.Lifetime</c> or the bindings (internal;
/// only <c>Pacpar.Alpm.Tests</c> is a friend assembly), so the registry and the owner-token lookup
/// mirror <c>Lifetime.GetLifetimeTokenForHandle</c> exactly - same <c>nint</c>-keyed dictionary,
/// same hit path - and the native call is declared here. The package pointers come from
/// <c>PackageBase.BackingStruct</c> through reflection, once, at setup time.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class QuestionPayloadLifetimeCostBenchmarks
{
  /// <summary>Question payloads carry one package (replace, install-ignorepkg) or a handful
  /// (conflicts, providers, unresolvable targets); four is the working shape here.</summary>
  private const int PayloadSize = 4;

  private static readonly FieldInfo BackingStructField =
    typeof(PackageBase).GetField("BackingStruct", BindingFlags.Instance | BindingFlags.NonPublic)!;

  [DllImport("libalpm", EntryPoint = "alpm_pkg_get_db", CallingConvention = CallingConvention.Cdecl)]
  private static extern nint AlpmPkgGetDb(nint pkg);

  private AlpmHandle _alpm = null!;
  private PackageView[] _payload = null!;
  private PackageView _largest = null!;
  private nint[] _packagePointers = null!;
  private nint _largestPointer;
  private Token _rootToken = null!;
  private Dictionary<nint, Token> _registry = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    _payload = [.. _alpm.GetLocalDatabase().GetPackageCache().Take(PayloadSize)];
    _packagePointers = new nint[_payload.Length];
    _rootToken = new Token("the ALPM handle");
    _registry = [];

    // The worst case a question can hand out: the local package with the most installed bytes,
    // which is the best cheap proxy for the longest file list. Copying that list is what the
    // "with files" variant buys, and it is the one part of a snapshot no question handler needs.
    _largest = _payload[0];
    foreach (var pkg in _alpm.GetLocalDatabase().GetPackageCache())
    {
      if (pkg.InstalledSize > _largest.InstalledSize) _largest = pkg;
    }

    // Touch every native field once, as the warm groups do, so the snapshot rows measure the copy
    // rather than libalpm's lazy parse of /desc.
    foreach (var pkg in _payload)
    {
      _ = pkg.ToSnapshot();
    }

    _ = _largest.ToSnapshot(includeFiles: true);
    Console.WriteLine(
      $"[QuestionPayloadLifetimeCostBenchmarks] largest local package: {_largest.Name} " +
      $"({_largest.InstalledSize} B installed, {_largest.Files.Count} files)");

    for (var i = 0; i < _payload.Length; ++i)
    {
      _packagePointers[i] = PointerOf(_payload[i]);
    }
    _largestPointer = PointerOf(_largest);

    // The steady-state registry: the database already has a wrapper, so its token exists and the
    // lookup is a hit. Every local package points at the same database pointer.
    _registry[AlpmPkgGetDb(_packagePointers[0])] = _rootToken.CreateChild("the local database");
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  /// <summary>Resolution (a), one package: native owner lookup plus the registry hit.</summary>
  private Token ResolveOwnerToken(nint package) => _registry[AlpmPkgGetDb(package)];

  private static unsafe nint PointerOf(PackageBase package)
    => (nint)Pointer.Unbox(BackingStructField.GetValue(package)!)!;

  // ---- Per package -------------------------------------------------------------------------

  /// <summary>Yardstick: one native read of comparable shape (the name call the payload copies).</summary>
  [Benchmark(Baseline = true)]
  [BenchmarkCategory("PerPackage")]
  public string PerPackage_ReadName() => _payload[0].Name;

  /// <summary>The native half of (a), isolated: one <c>alpm_pkg_get_db</c> call.</summary>
  [Benchmark]
  [BenchmarkCategory("PerPackage")]
  public nint PerPackage_GetOwningDatabase() => AlpmPkgGetDb(_packagePointers[0]);

  /// <summary>All of (a): the extra native call plus the token the view would then carry.</summary>
  [Benchmark]
  [BenchmarkCategory("PerPackage")]
  public object PerPackage_ResolveOwnerToken() => ResolveOwnerToken(_packagePointers[0]);

  /// <summary>All of (b): the eager metadata copy a question view would keep instead.</summary>
  [Benchmark]
  [BenchmarkCategory("PerPackage")]
  public PackageSnapshot PerPackage_EagerSnapshot() => _payload[0].ToSnapshot();

  /// <summary>(b) with the file list, the variant a question never needs: the upper bound.</summary>
  [Benchmark]
  [BenchmarkCategory("PerPackage")]
  public PackageSnapshot PerPackage_EagerSnapshotWithFiles() => _payload[0].ToSnapshot(includeFiles: true);

  // ---- Whole payload -----------------------------------------------------------------------

  /// <summary>(a) across a four-package question payload.</summary>
  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Payload")]
  public int Payload_ResolveOwnerTokens()
  {
    var count = 0;
    foreach (var package in _packagePointers)
    {
      _ = ResolveOwnerToken(package);
      count++;
    }

    return count;
  }

  /// <summary>(b) across the same payload; the sum keeps the copies observable.</summary>
  [Benchmark]
  [BenchmarkCategory("Payload")]
  public int Payload_EagerSnapshots()
  {
    var characters = 0;
    foreach (var package in _payload)
    {
      characters += package.ToSnapshot().Name.Length;
    }

    return characters;
  }

  /// <summary>(b) with file lists, as an upper bound for the payload shape.</summary>
  [Benchmark]
  [BenchmarkCategory("Payload")]
  public int Payload_EagerSnapshotsWithFiles()
  {
    var characters = 0;
    foreach (var package in _payload)
    {
      characters += package.ToSnapshot(includeFiles: true).Name.Length;
    }

    return characters;
  }

  // ---- Worst case: the largest local package -----------------------------------------------

  /// <summary>(a) is flat: the owner lookup costs the same on the most expensive package.</summary>
  [Benchmark(Baseline = true)]
  [BenchmarkCategory("WorstCase")]
  public object WorstCase_ResolveOwnerToken() => ResolveOwnerToken(_largestPointer);

  /// <summary>(b) on the largest package, without the file list: the metadata copy scales too.</summary>
  [Benchmark]
  [BenchmarkCategory("WorstCase")]
  public PackageSnapshot WorstCase_EagerSnapshot() => _largest.ToSnapshot();

  /// <summary>(b) on the largest package with the file list: what "complete snapshot" costs.</summary>
  [Benchmark]
  [BenchmarkCategory("WorstCase")]
  public PackageSnapshot WorstCase_EagerSnapshotWithFiles() => _largest.ToSnapshot(includeFiles: true);
}
