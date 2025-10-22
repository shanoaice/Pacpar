using System.Collections;
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

/// <summary>
/// Wraps an alpm_list_t from libalpm.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AlpmList<T> : IDisposable, IEnumerable<T> where T : IDisposable
{
  internal unsafe _alpm_list_t* _alpmListNative;
  private unsafe readonly delegate*<void*, T> _factory;

  private readonly bool _ownsPtr = false;
  private bool _disposed = false;

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
    var alpmListSize = Marshal.SizeOf<_alpm_list_t>();
    _alpmListNative = (_alpm_list_t*)Marshal.AllocHGlobal(alpmListSize);
    _ownsPtr = true;
  }

  public class AlpmListEnumerator<TEnum>(AlpmList<TEnum> _alpmList) : IEnumerator<TEnum> where TEnum : IDisposable
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

    object IEnumerator.Current => Current;
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
        foreach (var item in this)
        {
          item.Dispose();
        }
      }

      if (_ownsPtr)
      {
        var alpmListTemp = _alpmListNative->next;
        Marshal.FreeHGlobal((IntPtr)_alpmListNative);
        _alpmListNative = alpmListTemp;
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
}
