using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pacpar.Alpm;
using Pacpar.Alpm.Bindings;
using AlpmHandle = Pacpar.Alpm.Alpm;

namespace Pacpar.Benchmarks;

/// <summary>
/// Fortified experiment behind the NativeString short-string change (now implemented in
/// Pacpar.Alpm): native strings of a dozen bytes used to cost a native malloc/free round trip
/// each, and PackageVersion.CompareTo paid that twice per comparison. The implementation encodes
/// short strings into a caller-provided stack scratch (zero heap) and falls back to
/// NativeMemory.Alloc only when the string does not fit.
///
/// The Vercmp rows compare a single realistic pair of Arch version strings and differ only in
/// how the two operands are marshalled:
///   Heap_AllocHGlobal   — the pre-change path (historical baseline, kept for comparison);
///   Heap_NativeMemory   — the pure API-hygiene swap, ties the baseline on Linux because
///                         Marshal.AllocHGlobal already forwards to NativeMemory.Alloc;
///   Scratch_Utf8Buffer  — the implemented change (stack scratch, no heap for short strings);
///   RealApi_Anchor      — the real PackageVersion.CompareTo; post-change it must land on top
///                         of the Scratch row, proving the library really took the new path;
///   Floor_PreMarshalled — the same pair marshalled once in GlobalSetup: the alpm_pkg_vercmp
///                         call alone, the floor the marshalling rows cannot beat.
/// The Bulk256 rows run 256 comparisons per invocation (an upgrade-check shape) to see the same
/// delta at realistic amortization.
///
/// Reading the table: the interesting column is time (ns/op), not Allocated. Native malloc is
/// invisible to the GC allocator, so Allocated reads 0 B on every row — the win under test is
/// removing native malloc/free round trips, tens of ns per pair. These benchmarks are read-only
/// and need a pacman database (see BenchEnvironment).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[ProcessCount(1)]
[WarmupCount(3)]
[IterationCount(10)]
public unsafe class NativeStringScratchBenchmarks
{
  /// <summary>Scratch budget per operand. Arch version strings are far shorter than this.</summary>
  private const int ScratchSize = 64;

  /// <summary>Comparisons per Bulk256 invocation.</summary>
  private const int PairCount = 256;

  private AlpmHandle _alpm = null!;
  private string[] _left = null!;
  private string[] _right = null!;
  private PackageVersion[] _leftReal = null!;
  private PackageVersion[] _rightReal = null!;
  private int _pairs;
  private byte* _preLeft;
  private byte* _preRight;

  [GlobalSetup]
  public void Setup()
  {
    _alpm = BenchEnvironment.Open();
    var pkgs = _alpm.GetLocalDatabase().GetPackageCache();

    var real = new List<PackageVersion>(PairCount);
    foreach (var pkg in pkgs)
    {
      real.Add(pkg.Version);
      if (real.Count == PairCount) break;
    }
    if (real.Count < 2)
      throw new InvalidOperationException("Need at least two packages to compare versions.");

    _pairs = real.Count;
    _leftReal = new PackageVersion[_pairs];
    _rightReal = new PackageVersion[_pairs];
    _left = new string[_pairs];
    _right = new string[_pairs];
    for (var i = 0; i < _pairs; i++)
    {
      _leftReal[i] = real[i];
      _rightReal[i] = real[(i + 1) % _pairs];
      _left[i] = real[i].ToString();
      _right[i] = real[(i + 1) % _pairs].ToString();
    }

    // The floor pair, marshalled once and reused.
    _preLeft = ToNativeHeap(_left[0]);
    _preRight = ToNativeHeap(_right[0]);
  }

  [GlobalCleanup]
  public void Cleanup()
  {
    Marshal.FreeHGlobal((nint)_preLeft);
    Marshal.FreeHGlobal((nint)_preRight);
    _alpm.Dispose();
  }

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Vercmp")]
  public int Vercmp_Heap_AllocHGlobal() => CompareHeap(_left[0], _right[0]);

  [Benchmark]
  [BenchmarkCategory("Vercmp")]
  public int Vercmp_Heap_NativeMemory() => CompareNativeMemory(_left[0], _right[0]);

  [Benchmark]
  [BenchmarkCategory("Vercmp")]
  public int Vercmp_Scratch_Utf8Buffer() => CompareScratch(_left[0], _right[0]);

  [Benchmark]
  [BenchmarkCategory("Vercmp")]
  public int Vercmp_RealApi_Anchor() => _leftReal[0].CompareTo(_rightReal[0]);

  [Benchmark]
  [BenchmarkCategory("Vercmp")]
  public int Vercmp_Floor_PreMarshalled() => NativeMethods.alpm_pkg_vercmp(_preLeft, _preRight);

  [Benchmark(Baseline = true)]
  [BenchmarkCategory("Bulk256")]
  public long Bulk_Heap_AllocHGlobal()
  {
    long sum = 0;
    for (var i = 0; i < _pairs; i++) sum += CompareHeap(_left[i], _right[i]);
    return sum;
  }

  [Benchmark]
  [BenchmarkCategory("Bulk256")]
  public long Bulk_Scratch_Utf8Buffer()
  {
    long sum = 0;
    for (var i = 0; i < _pairs; i++) sum += CompareScratch(_left[i], _right[i]);
    return sum;
  }

  /// <summary>Replica of the pre-change NativeString.ToNative (the Marshal.AllocHGlobal path).</summary>
  private static byte* ToNativeHeap(string value)
  {
    var byteCount = Encoding.UTF8.GetByteCount(value);
    var buffer = (byte*)Marshal.AllocHGlobal(byteCount + 1);
    Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, byteCount));
    buffer[byteCount] = 0;
    return buffer;
  }

  /// <summary>The same allocation through NativeMemory (the API-hygiene swap under test).</summary>
  private static byte* ToNativeMemory(string value)
  {
    var byteCount = Encoding.UTF8.GetByteCount(value);
    var buffer = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
    Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, byteCount));
    buffer[byteCount] = 0;
    return buffer;
  }

  private static int CompareHeap(string a, string b)
  {
    var left = ToNativeHeap(a);
    try
    {
      var right = ToNativeHeap(b);
      try { return NativeMethods.alpm_pkg_vercmp(left, right); }
      finally { Marshal.FreeHGlobal((nint)right); }
    }
    finally { Marshal.FreeHGlobal((nint)left); }
  }

  private static int CompareNativeMemory(string a, string b)
  {
    var left = ToNativeMemory(a);
    try
    {
      var right = ToNativeMemory(b);
      try { return NativeMethods.alpm_pkg_vercmp(left, right); }
      finally { NativeMemory.Free(right); }
    }
    finally { NativeMemory.Free(left); }
  }

  private static int CompareScratch(string a, string b)
  {
    Span<byte> leftScratch = stackalloc byte[ScratchSize];
    Span<byte> rightScratch = stackalloc byte[ScratchSize];
    using var left = new Utf8Buffer(a, leftScratch);
    using var right = new Utf8Buffer(b, rightScratch);
    return NativeMethods.alpm_pkg_vercmp(left.Ptr, right.Ptr);
  }

  /// <summary>
  /// The proposed NativeString shape, copied here because the Benchmarks project is not a friend
  /// of Pacpar.Alpm: encode into the caller's stack scratch when the string (plus NUL) fits,
  /// otherwise fall back to NativeMemory.Alloc and free it on Dispose. A ref struct, so the
  /// buffer can never escape the frame that owns the scratch.
  /// </summary>
  private ref struct Utf8Buffer
  {
    private readonly byte* _heap;
    internal readonly byte* Ptr;

    internal Utf8Buffer(string value, scoped Span<byte> scratch)
    {
      _heap = null;
      var byteCount = Encoding.UTF8.GetByteCount(value);
      if (byteCount + 1 <= scratch.Length)
      {
        Encoding.UTF8.GetBytes(value, scratch);
        scratch[byteCount] = 0;
        Ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(scratch));
      }
      else
      {
        var heap = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        Encoding.UTF8.GetBytes(value, new Span<byte>(heap, byteCount));
        heap[byteCount] = 0;
        _heap = heap;
        Ptr = heap;
      }
    }

    public void Dispose()
    {
      if (_heap != null) NativeMemory.Free(_heap);
    }
  }
}
