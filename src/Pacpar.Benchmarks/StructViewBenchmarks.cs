using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pacpar.Alpm;
using Pacpar.Alpm.List;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Variant B-2 (lifetime-token-preview.md, appendix B.3): a readonly struct view whose lazy cache
/// and lifetime token live in a shared per-package entry instead of in the view itself. The
/// question is allocation. Today every element of every enumeration is a new <see cref="PackageView"/>,
/// and every lazy metadata read on that fresh object allocates again, so one pass over the local
/// database allocates ~585 KB (WarmBulkBenchmarks.Bulk_Lazy_NameVersion). A struct view over a
/// shared entry would allocate nothing on a warm pass, but it needs a per-element entry lookup and
/// it still has to run the lifetime check. This benchmark measures both sides, plus the first
/// (cold) pass a one-shot CLI actually pays for.
/// </summary>
/// <remarks>
/// The prototypes are local to this project, exactly like the PackageSnapshot prototype the earlier
/// suite used, because the types under test do not exist in Pacpar.Alpm yet. How to read the table:
/// <list type="bullet">
/// <item><c>Scan_Class_LibraryPath</c> is today's code and is the baseline. It doubles as the COLD
/// class path: each BenchmarkDotNet invocation walks the same <see cref="AlpmList{T}"/>, but the
/// library constructs a new <see cref="PackageView"/> per element per pass, so no cache survives a pass
/// and every property read goes back to libalpm and allocates a fresh string. Note that
/// <see cref="PackageBase.Version"/> is not cached even on one instance, so it allocates a Version
/// plus a string per read.</item>
/// <item><c>Scan_Class_SharedEntry</c> keeps the class representation but moves the cache into the
/// entry: one object per element per pass, no strings. It isolates "sharing the cache" from "being
/// a struct" - if the win is really the sharing, this column shows it without any type change.</item>
/// <item><c>Scan_Struct_SharedEntry</c> is B-2 as sketched: zero managed allocation per pass, one
/// dictionary lookup per element.</item>
/// <item><c>Scan_Struct_EntryArray</c> is the best case for B-2: the entries were resolved once,
/// so a pass is a plain array walk with no lookup at all.</item>
/// <item><c>Cold_Struct_FreshCache</c> is B-2 on a first pass (allocation of the entry table is
/// inside the measured method) against <c>Cold_Class_LibraryPath</c>, the real one-shot CLI
/// case.</item>
/// </list>
/// The keys are pointer-shaped <see cref="nint"/> values (8-byte aligned, one contiguous range)
/// rather than the real <c>_alpm_pkg_t*</c>: <c>PackageBase.BackingStruct</c> is internal to
/// Pacpar.Alpm and this project is not a friend assembly, and a dictionary lookup over an nint
/// costs the same either way. The entries' cached strings are read from the real packages during
/// setup, so the columns compare representation and lookup cost, not string allocation - both
/// designs pay for the strings once.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public class StructViewBenchmarks
{
  private const string Query = "system";

  private AlpmHandle _alpm = null!;
  private AlpmList<PackageView> _localPkgs = null!;

  /// <summary>The handle's root lifetime token, shared by every entry of that handle.</summary>
  private Token _ownerToken = null!;

  private nint[] _keys = null!;
  private string[] _names = null!;
  private string[] _versions = null!;
  private string?[] _descriptions = null!;
  private Dictionary<nint, PackageEntry> _cache = null!;
  private PackageEntry[] _entryArray = null!;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    _localPkgs = _alpm.GetLocalDatabase().GetPackageCache();
    _ownerToken = new Token("the alpm handle");

    // Setup only: materialize the real views once and copy what the entries would cache. The
    // measured passes must not see this work, except in the Cold_Struct_FreshCache variant.
    var pkgs = _localPkgs.ToArray();
    _keys = new nint[pkgs.Length];
    _names = new string[pkgs.Length];
    _versions = new string[pkgs.Length];
    _descriptions = new string?[pkgs.Length];
    _cache = new Dictionary<nint, PackageEntry>(pkgs.Length);

    for (int i = 0; i < pkgs.Length; i++)
    {
      // 8-byte aligned, in one range, the shape malloc hands out - see the remarks.
      _keys[i] = unchecked((nint)(0x7f2a_0000_0000L + i * 0x38L));
      _names[i] = pkgs[i].Name;
      _versions[i] = pkgs[i].Version.ToString();
      _descriptions[i] = pkgs[i].Description;
      _cache[_keys[i]] = new PackageEntry(_ownerToken, _names[i], _versions[i], _descriptions[i]);
    }

    _entryArray = new PackageEntry[pkgs.Length];
    for (int i = 0; i < pkgs.Length; i++)
      _entryArray[i] = _cache[_keys[i]];
  }

  [GlobalCleanup]
  public void Cleanup() => _alpm.Dispose();

  // ---- Scan: name + version, the "pacman -Q" shape, once over the local cache ----

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Scan")]
  public int Scan_Class_LibraryPath()
  {
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      var name = pkg.Name;
      var version = pkg.Version.ToString();
      if (name.Length > 0 && version.Length > 0) count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_Class_SharedEntry()
  {
    var cache = _cache;
    int count = 0;
    for (int i = 0; i < _keys.Length; i++)
    {
      var view = new PackageViewClass(cache[_keys[i]]);
      if (view.Name.Length > 0 && view.VersionText.Length > 0) count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_Struct_SharedEntry()
  {
    var cache = _cache;
    int count = 0;
    for (int i = 0; i < _keys.Length; i++)
    {
      var view = new PackageViewStruct(cache[_keys[i]]);
      if (view.Name.Length > 0 && view.VersionText.Length > 0) count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Scan")]
  public int Scan_Struct_EntryArray()
  {
    var entries = _entryArray;
    int count = 0;
    for (int i = 0; i < entries.Length; i++)
    {
      var view = new PackageViewStruct(entries[i]);
      if (view.Name.Length > 0 && view.VersionText.Length > 0) count++;
    }
    return count;
  }

  // ---- Cold: a first pass, the one-shot CLI case ----

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Cold")]
  public int Cold_Class_LibraryPath()
  {
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      var name = pkg.Name;
      var version = pkg.Version.ToString();
      if (name.Length > 0 && version.Length > 0) count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Cold")]
  public int Cold_Struct_FreshCache()
  {
    var keys = _keys;
    var cache = new Dictionary<nint, PackageEntry>(keys.Length);
    for (int i = 0; i < keys.Length; i++)
      cache[keys[i]] = new PackageEntry(_ownerToken, _names[i], _versions[i], _descriptions[i]);

    int count = 0;
    for (int i = 0; i < keys.Length; i++)
    {
      var view = new PackageViewStruct(cache[keys[i]]);
      if (view.Name.Length > 0 && view.VersionText.Length > 0) count++;
    }
    return count;
  }

  // ---- Search: name + description, the "pacman -Qs" shape ----

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Search")]
  public int Search_Class_LibraryPath()
  {
    int count = 0;
    foreach (var pkg in _localPkgs)
    {
      var description = pkg.Description;
      if (pkg.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
          (description is not null && description.Contains(Query, StringComparison.OrdinalIgnoreCase)))
        count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Search")]
  public int Search_Class_SharedEntry()
  {
    var cache = _cache;
    int count = 0;
    for (int i = 0; i < _keys.Length; i++)
    {
      var view = new PackageViewClass(cache[_keys[i]]);
      var description = view.Description;
      if (view.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
          (description is not null && description.Contains(Query, StringComparison.OrdinalIgnoreCase)))
        count++;
    }
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Search")]
  public int Search_Struct_SharedEntry()
  {
    var cache = _cache;
    int count = 0;
    for (int i = 0; i < _keys.Length; i++)
    {
      var view = new PackageViewStruct(cache[_keys[i]]);
      var description = view.Description;
      if (view.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
          (description is not null && description.Contains(Query, StringComparison.OrdinalIgnoreCase)))
        count++;
    }
    return count;
  }

  // ---- Materialize: keep the views (a UI sorts/filters, a wrapper stores them) ----

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Materialize")]
  public int ToList_Class_LibraryPath()
  {
    var list = new List<PackageView>();
    foreach (var pkg in _localPkgs)
      list.Add(pkg);

    int count = 0;
    for (int i = 0; i < list.Count; i++)
      count += list[i].Name.Length;
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Materialize")]
  public int ToList_Class_SharedEntry()
  {
    var cache = _cache;
    var list = new List<PackageViewClass>(_keys.Length);
    for (int i = 0; i < _keys.Length; i++)
      list.Add(new PackageViewClass(cache[_keys[i]]));

    int count = 0;
    for (int i = 0; i < list.Count; i++)
      count += list[i].Name.Length;
    return count;
  }

  [Benchmark]
  [BenchmarkCategory("Materialize")]
  public int ToArray_Struct_SharedEntry()
  {
    var cache = _cache;
    var views = new PackageViewStruct[_keys.Length];
    for (int i = 0; i < _keys.Length; i++)
      views[i] = new PackageViewStruct(cache[_keys[i]]);

    int count = 0;
    for (int i = 0; i < views.Length; i++)
      count += views[i].Name.Length;
    return count;
  }

  // ---- Entry: the per-element costs B-2 adds ----

  [Benchmark]
  [BenchmarkCategory("Entry")]
  public long Entry_Resolve()
  {
    var cache = _cache;
    long sum = 0;
    for (int i = 0; i < _keys.Length; i++)
      sum += cache[_keys[i]].Name.Length;
    return sum;
  }

  [Benchmark]
  [BenchmarkCategory("Entry")]
  public int Entry_CreateAndInsert()
  {
    var keys = _keys;
    var cache = new Dictionary<nint, PackageEntry>(keys.Length);
    for (int i = 0; i < keys.Length; i++)
      cache[keys[i]] = new PackageEntry(_ownerToken, _names[i], _versions[i], _descriptions[i]);
    return cache.Count;
  }
}

/// <summary>
/// The shared, pointer-keyed entry B-2 proposes: it owns the lifetime token and the lazy metadata
/// cache that today live inside <see cref="PackageBase"/> - which is exactly why a per-pass view
/// cannot reuse them and calls back into libalpm on every element.
/// </summary>
internal sealed class PackageEntry
{
  internal PackageEntry(Token owner, string name, string versionText, string? description)
  {
    Owner = owner;
    Name = name;
    VersionText = versionText;
    Description = description;
  }

  internal readonly Token Owner;
  internal readonly string Name;
  internal readonly string VersionText;
  internal readonly string? Description;
}

/// <summary>
/// B-2: the value-type view. One reference to its entry, no cache of its own, and the lifetime
/// check the user requires on every read (the strict tier measured by LifetimeCheckBenchmarks).
/// </summary>
internal readonly struct PackageViewStruct
{
  private readonly PackageEntry _entry;

  internal PackageViewStruct(PackageEntry entry) => _entry = entry;

  internal string Name
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.Name;
    }
  }

  internal string VersionText
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.VersionText;
    }
  }

  internal string? Description
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.Description;
    }
  }
}

/// <summary>
/// Control for the struct: the same shared entry behind a class reference, so the one object per
/// element per pass is still allocated. This is the column that separates "shared cache" from
/// "value type".
/// </summary>
internal sealed class PackageViewClass
{
  private readonly PackageEntry _entry;

  internal PackageViewClass(PackageEntry entry) => _entry = entry;

  internal string Name
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.Name;
    }
  }

  internal string VersionText
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.VersionText;
    }
  }

  internal string? Description
  {
    get
    {
      _entry.Owner.ThrowIfStale();
      return _entry.Description;
    }
  }
}
