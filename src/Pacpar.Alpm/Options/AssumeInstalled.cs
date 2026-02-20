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

  private unsafe AlpmList<Depend> BackingList => new(NativeMethods.alpm_option_get_assumeinstalled(_handle), &Depend.Factory, false);

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_assumeinstalled(_handle));

  public AlpmList<Depend>.Enumerator GetEnumerator() => BackingList.GetOwningEnumerator();

  IEnumerator<Depend> IEnumerable<Depend>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(Depend item)
  {
    var err = NativeMethods.alpm_option_add_assumeinstalled(_handle, item.BackingStruct);
    if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
  }

  public unsafe bool Contains(Depend item)
  {
    var itemPtr = item.BackingStruct;
    var optionsList = NativeMethods.alpm_option_get_assumeinstalled(_handle);
    return NativeMethods.alpm_list_find_ptr(optionsList, itemPtr) == itemPtr;
  }

  public unsafe bool Remove(Depend item)
  {
    var itemPtr = item.BackingStruct;
    var err = NativeMethods.alpm_option_remove_assumeinstalled(_handle, itemPtr);
    return err == 0;
  }

  public void Clear()
  {
    foreach (var item in this)
    {
      Remove(item);
    }
  }

  public void CopyTo(Depend[] array, int arrayIndex)
  {
    ArgumentNullException.ThrowIfNull(array);
    ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
    using var enumerator = GetEnumerator();
    for (var i = 0; i <= arrayIndex; ++i)
    {
      enumerator.MoveNext();
    }

    var idx = 0;
    do
    {
      array[idx] = enumerator.Current;
      ++idx;
    } while (enumerator.MoveNext());
  }
}
