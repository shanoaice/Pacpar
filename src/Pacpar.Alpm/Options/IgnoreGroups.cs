using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

internal class IgnoreGroups : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe IgnoreGroups(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_ignoregroups(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_ignoregroups(_handle));

  // ReSharper disable once MemberCanBePrivate.Global
  public AlpmList<string>.Enumerator GetEnumerator() => BackingList.GetOwningEnumerator();

  IEnumerator<string> IEnumerable<string>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_ignoregroup(_handle, stringPtr);
      if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
    }
    finally
    {
      Marshal.FreeHGlobal((nint)stringPtr);
    }
  }

  public unsafe bool Contains(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var optionsList = NativeMethods.alpm_option_get_ignoregroups(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_ignoregroup(_handle, stringPtr);
    Marshal.FreeHGlobal((nint)stringPtr);
    return err == 0;
  }

  public void Clear()
  {
    foreach (var item in this)
    {
      Remove(item);
    }
  }

  public void CopyTo(string[] array, int arrayIndex)
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
