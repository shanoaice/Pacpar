using System.Collections;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm.list;

/// <summary>
/// Indicates the allocation pattern of strings wrapped by AlpmStringList
/// </summary>
public enum AlpmStringListAllocPattern
{
  /// <summary>
  /// Indicates that we do not free any of the content,
  /// not even the list itself. usually happens when the caller
  /// manages the list. This is also the default.
  /// </summary>
  NO_FREE = 0,

  /// <summary>
  /// Indicate that we will free the list itself,
  /// but not the inner string content, usually happens
  /// when the content should be freed by the caller
  /// who passes in the list.
  /// </summary>
  NO_FREE_INNER = 1,

  /// <summary>
  /// Indicates that we should free the content
  /// by the allocator across FFI boundary, i.e. the C free().
  /// Usually happens when callee is responsible for freeing the content.
  /// </summary>
  FFI = 2,

  /// <summary>
  /// Indicates that we should free the content
  /// by the .NET CLR Unmanaged Allocator.
  /// Usually happens when we created the list ourselves.
  /// </summary>
  DOT_NET = 3,
}

/// <summary>
/// Wraps an alpm_list_t from libalpm.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AlpmList<T> : IDisposable, IReadOnlyList<T>
{
  internal unsafe _alpm_list_t* AlpmListNative;
  private readonly unsafe delegate*<void*, T> _factory;
  protected bool Disposed;
  private readonly bool _ownsList;

  /// <summary>
  /// Wraps an existing alpm_list_t* owned by libalpm (across FFI boundaries).
  /// *Note*: This is intended for internal wrapping only.
  /// </summary>
  /// <param name="alpmList">existing alpm_list_t*</param>
  /// <param name="factory">factory function to covert void* to T</param>
  /// <param name="ownsList">whether .NET owns / frees the list</param>
  internal unsafe AlpmList(_alpm_list_t* alpmList, delegate*<void*, T> factory, bool ownsList = true)
  {
    AlpmListNative = alpmList;
    _factory = factory;
    _ownsList = ownsList;
  }

  /// <summary>
  /// Creates and wraps a new alpm_list_t* list head, owned by dotnet.
  /// Note that subsequent list is probably owned across FFI boundaries
  /// when sent to libalpm for item dumping, thus we only own the head.
  /// The data is also probably owned by libalpm.
  /// </summary>
  /// <param name="factory">factory function to covert void* to T</param>
  public unsafe AlpmList(delegate*<void*, T> factory)
  {
    _factory = factory;
    AlpmListNative = (_alpm_list_t*)IntPtr.Zero;
    _ownsList = true;
  }

  private sealed class AlpmListEnumerator<TEnum>(AlpmList<TEnum> alpmList, bool disposeParent = false)
    : IEnumerator<TEnum>
  {
    private unsafe _alpm_list_t* _alpmListNative = alpmList.AlpmListNative;
    private bool _disposed;

    public unsafe TEnum Current => alpmList._factory(_alpmListNative->data);

    public unsafe bool MoveNext()
    {
      var next = NativeMethods.alpm_list_next(_alpmListNative);
      if ((IntPtr)next == IntPtr.Zero) return false;
      _alpmListNative = next;
      return true;
    }

    public unsafe void Reset()
    {
      _alpmListNative = alpmList.AlpmListNative;
    }

    public void Dispose()
    {
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
      if (_disposed) return;
      if (disposing)
      {
        if (disposeParent) alpmList.Dispose();
      }

      _disposed = true;
    }

    object IEnumerator.Current => Current!;

    ~AlpmListEnumerator()
    {
      Dispose(disposing: false);
    }
  }

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (Disposed) return;
    if (disposing)
    {
      // dispose managed state (managed objects)
    }

    if (_ownsList) NativeMethods.alpm_list_free(AlpmListNative);
    AlpmListNative = null;

    Disposed = true;
  }

  ~AlpmList()
  {
    Dispose(disposing: false);
  }

  public IEnumerator<T> GetEnumerator() => new AlpmListEnumerator<T>(this);

  public IEnumerator<T> GetOwningEnumerator() => new AlpmListEnumerator<T>(this, true);

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe int Count => (int)NativeMethods.alpm_list_count(AlpmListNative);

  public unsafe T this[int index] => _factory(NativeMethods.alpm_list_nth(AlpmListNative, (nuint)index));
}

public unsafe class AlpmStringList : AlpmList<string>
{
  private readonly AlpmStringListAllocPattern _allocPattern;

  public AlpmStringList(_alpm_list_t* alpmList,
    AlpmStringListAllocPattern allocPattern = AlpmStringListAllocPattern.NO_FREE) : base(alpmList, &StringFactory,
    allocPattern != AlpmStringListAllocPattern.NO_FREE)
  {
    this._allocPattern = allocPattern;
  }

  public AlpmStringList(AlpmStringListAllocPattern allocPattern = AlpmStringListAllocPattern.NO_FREE) : base(
    &StringFactory)
  {
    this._allocPattern = allocPattern;
  }

  protected override void Dispose(bool disposing)
  {
    if (!Disposed)
    {
      switch (_allocPattern)
      {
        case AlpmStringListAllocPattern.FFI:
          NativeMethods.alpm_list_free_inner(AlpmListNative, &MemoryManagement.CFreeExtern);
          break;
        case AlpmStringListAllocPattern.DOT_NET:
          NativeMethods.alpm_list_free_inner(AlpmListNative, &MemoryManagement.UnmanagedFreeExtern);
          break;
        // No-op for NoFree or any other case
      }
    }

    base.Dispose(disposing);
  }

  private static string StringFactory(void* data) => Marshal.PtrToStringAnsi((nint)data) ?? string.Empty;
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
    if ((!Disposed) && disposing)
    {
      foreach (var item in this)
      {
        item.Dispose();
      }
    }

    base.Dispose(disposing);
  }
}
