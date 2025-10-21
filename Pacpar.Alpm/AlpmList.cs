using System.Collections;

namespace Pacpar.Alpm;

/// <summary>
/// Wraps an alpm_list_t from libalpm.
/// **Warning**: You will need to provide a factory method that 
/// constructs the type T from the native pointer.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AlpmList<T> : IDisposable, ICollection<T> where T : IDisposable
{
  private unsafe delegate T _factoryType(void* ptr);

  private unsafe readonly _alpm_list_t* _alpmListNative;
  private readonly _factoryType _factory;

  public class AlpmListEnumerator<TEnum>(AlpmList<TEnum> _alpmList) : IEnumerator<TEnum> where TEnum : IDisposable
  {
    private unsafe _alpm_list_t* _alpmListNative = _alpmList._alpmListNative;

    public unsafe TEnum Current
    {
      get
      {
        return _alpmList._factory(_alpmListNative->data);
      }
    }

    public unsafe bool MoveNext()
    {
      var next = NativeMethods.alpm_list_next(_alpmListNative);
      if ((IntPtr)next == IntPtr.Zero) return false;
      _alpmListNative = next;
      return true;
    }

    public unsafe void Reset()
    {
      _alpmListNative = _alpmList._alpmListNative;
    }

    public void Dispose()
    {
      GC.SuppressFinalize(this);
    }

    object IEnumerator.Current => Current;
  }

  public void Dispose()
  {

  }

  public IEnumerator<T> GetEnumerator() => new AlpmListEnumerator<T>(this);

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public string[] Items { get; set; }
}
