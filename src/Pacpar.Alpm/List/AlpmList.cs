using System.Collections;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.List;

/// <summary>
/// Wraps an alpm_list_t from libalpm.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AlpmList<T> : IDisposable, IReadOnlyList<T>
{
  internal unsafe _alpm_list_t* AlpmListNative;

  // ReSharper disable once MemberCanBePrivate.Global
  internal readonly unsafe delegate*<void*, T> Factory;
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
    Factory = factory;
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
    Factory = factory;
    AlpmListNative = (_alpm_list_t*)IntPtr.Zero;
    _ownsList = true;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(Disposed, this);
  }

  public unsafe struct Enumerator(AlpmList<T> alpmList, bool disposeParent = false)
    : IEnumerator<T>
  {
    private _alpm_list_t* _current = null;
    private bool _started = false;
    private bool _disposed;

    public T Current
    {
      get
      {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        if (!_started || _current == null) throw new InvalidOperationException();
        return alpmList.Factory(_current->data);
      }
    }

    public bool MoveNext()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      if (!_started)
      {
        _current = alpmList.AlpmListNative;
        _started = true;
      }
      else if (_current != null)
      {
        _current = NativeMethods.alpm_list_next(_current);
      }

      return _current != null;
    }

    public void Reset()
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      _current = null;
      _started = false;
    }

    public void Dispose()
    {
      if (_disposed) return;
      if (disposeParent) alpmList.Dispose();

      _disposed = true;
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

  public Enumerator GetEnumerator()
  {
    ThrowIfDisposed();
    return new Enumerator(this);
  }

  public Enumerator GetOwningEnumerator()
  {
    ThrowIfDisposed();
    return new Enumerator(this, true);
  }

  IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe int Count
  {
    get
    {
      ThrowIfDisposed();
      return (int)NativeMethods.alpm_list_count(AlpmListNative);
    }
  }

  public unsafe T this[int index]
  {
    get
    {
      ThrowIfDisposed();
      return Factory(NativeMethods.alpm_list_nth(AlpmListNative, (nuint)index));
    }
  }
}
