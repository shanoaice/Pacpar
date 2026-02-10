using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public enum AlpmStringListAllocPattern
{
  NoFree = 0,
  FFI = 1,
  DotNet = 2,
}

/// <summary>
/// Wraps an alpm_list_t from libalpm.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AlpmList<T> : IDisposable, IReadOnlyList<T>
{
  internal unsafe _alpm_list_t* _alpmListNative;
  private unsafe readonly delegate*<void*, T> _factory;
  protected bool _disposed = false;

  /// <summary>
  /// Wraps an existing alpm_list_t* owned by libalpm (accross FFI boundaries).
  /// *Note*: This is intended for internal wrapping only.
  /// </summary>
  /// <param name="alpmList">existing alpm_list_t*</param>
  /// <param name="factory">factory function to covert void* to T</param>

  internal unsafe AlpmList(_alpm_list_t* alpmList, delegate*<void*, T> factory)
  {
    _alpmListNative = alpmList;
    _factory = factory;
  }

  /// <summary>
  /// Creates and wraps a new alpm_list_t* list head, owned by dotnet.
  /// Note that subsequent list is probably owned across FFI boundraies
  /// when sent to libalpm for item dumping, thus we only own the head.
  /// The data is also probably owned by libalpm.
  /// </summary>
  /// <param name="factory">factory function to covert void* to T</param>
  public unsafe AlpmList(delegate*<void*, T> factory)
  {
    _factory = factory;
    _alpmListNative = (_alpm_list_t*)IntPtr.Zero;
  }

  public class AlpmListEnumerator<TEnum>(AlpmList<TEnum> _alpmList) : IEnumerator<TEnum>
  {
    private unsafe _alpm_list_t* _alpmList_native = _alpmList._alpmListNative;

    public unsafe TEnum Current
    {
      get
      {
        return _alpmList._factory(_alpmList_native->data);
      }
    }

    public unsafe bool MoveNext()
    {
      var next = NativeMethods.alpm_list_next(_alpmList_native);
      if ((IntPtr)next == IntPtr.Zero) return false;
      _alpmList_native = next;
      return true;
    }

    public unsafe void Reset()
    {
      _alpmList_native = _alpmList._alpmListNative;
    }

    public void Dispose()
    {
      GC.SuppressFinalize(this);
    }

    object IEnumerator.Current => Current!;
  }

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }

      NativeMethods.alpm_list_free(_alpmListNative);
      _alpmListNative = null;

      _disposed = true;
    }
  }

  ~AlpmList()
  {
    Dispose(disposing: false);
  }

  public IEnumerator<T> GetEnumerator() => new AlpmListEnumerator<T>(this);

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe int Count => (int)NativeMethods.alpm_list_count(_alpmListNative);

  public unsafe T this[int index] => _factory(NativeMethods.alpm_list_nth(_alpmListNative, (nuint)index));
}

public unsafe class AlpmStringList : AlpmList<string>
{
  private readonly AlpmStringListAllocPattern allocPattern;

  public AlpmStringList(_alpm_list_t* alpmList, AlpmStringListAllocPattern allocPattern = AlpmStringListAllocPattern.NoFree) : base(alpmList, &StringFactory)
  {
    this.allocPattern = allocPattern;
  }

  public AlpmStringList(AlpmStringListAllocPattern allocPattern = AlpmStringListAllocPattern.NoFree) : base(&StringFactory)
  {
    this.allocPattern = allocPattern;
  }

  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      switch (allocPattern)
      {
        case AlpmStringListAllocPattern.FFI:
          NativeMethods.alpm_list_free_inner(_alpmListNative, &MemoryManagement.CFreeExtern);
          break;
        case AlpmStringListAllocPattern.DotNet:
          NativeMethods.alpm_list_free_inner(_alpmListNative, &MemoryManagement.UnmanagedFreeExtern);
          break;
        default:
          // No-op for NoFree or any other case
          break;
      }
    }
    base.Dispose(disposing);
  }

  private static string StringFactory(void* data) => Marshal.PtrToStringAnsi((nint)data) ?? string.Empty;
}

public unsafe class AlpmUnmanagedStringList : AlpmStringList
{
  public AlpmUnmanagedStringList() : base()
  {
  }

  public AlpmUnmanagedStringList(_alpm_list_t* alpmList) : base(alpmList)
  {
  }

  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      NativeMethods.alpm_list_free_inner(_alpmListNative, &MemoryManagement.CFreeExtern);
    }
    base.Dispose(disposing);
  }
}

public class AlpmDisposableList<T> : AlpmList<T> where T : IDisposable
{
  public unsafe AlpmDisposableList(delegate*<void*, T> factory) : base(factory)
  {
  }

  internal unsafe AlpmDisposableList(_alpm_list_t* alpmList, delegate*<void*, T> factory) : base(alpmList, factory)
  {
  }

  protected override void Dispose(bool disposing)
  {
    if ((!_disposed) && disposing)
    {
      foreach (var item in this)
      {
        item.Dispose();
      }
    }
    base.Dispose(disposing);
  }
}
