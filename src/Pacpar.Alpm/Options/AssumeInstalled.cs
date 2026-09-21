using System.Collections;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

internal class AssumeInstalled : ICollection<Depend>
{
  private readonly unsafe byte* _handle;

  internal unsafe AssumeInstalled(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmList<Depend> BackingList => Depend.ListFactory(NativeMethods.alpm_option_get_assumeinstalled(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_assumeinstalled(_handle));

  public AlpmList<Depend>.Enumerator GetEnumerator() => BackingList.GetEnumerator();

  IEnumerator<Depend> IEnumerable<Depend>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(Depend item)
  {
    var err = NativeMethods.alpm_option_add_assumeinstalled(_handle, item.NativePtrOrThrow(nameof(item)));
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
  }

  public unsafe bool Contains(Depend item)
  {
    var itemPtr = item.BackingStruct;
    if (itemPtr == null) return false;

    var optionsList = NativeMethods.alpm_option_get_assumeinstalled(_handle);
    return NativeMethods.alpm_list_find_ptr(optionsList, itemPtr) == itemPtr;
  }

  public unsafe bool Remove(Depend item)
  {
    if (item.BackingStruct == null) return false;

    var itemPtr = item.BackingStruct;
    var err = NativeMethods.alpm_option_remove_assumeinstalled(_handle, itemPtr);
    return err == 0;
  }

  public void Clear()
  {
    // Snapshot first: the enumerator caches the current native node, so removing while
    // enumerating leaves it pointing at a freed node on the next MoveNext().
    foreach (var item in BackingList.ToArray())
    {
      Remove(item);
    }
  }

  public void CopyTo(Depend[] array, int arrayIndex)
  {
    ArgumentNullException.ThrowIfNull(array);
    ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

    var source = BackingList.ToArray();
    if (array.Length - arrayIndex < source.Length)
    {
      throw new ArgumentException("The destination array is not long enough to hold all items.", nameof(array));
    }

    for (var i = 0; i < source.Length; ++i)
    {
      array[arrayIndex + i] = source[i];
    }
  }
}
