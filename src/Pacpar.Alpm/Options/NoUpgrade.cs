using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm.Options;

internal class NoUpgrade : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe NoUpgrade(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_noupgrades(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_noupgrades(_handle));

  // ReSharper disable once MemberCanBePrivate.Global
  public AlpmList<string>.Enumerator GetEnumerator() => BackingList.GetEnumerator();

  IEnumerator<string> IEnumerable<string>.GetEnumerator() => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_noupgrade(_handle, stringPtr);
      if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno(_handle));
    }
    finally
    {
      Marshal.FreeHGlobal((nint)stringPtr);
    }
  }

  public unsafe bool Contains(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var optionsList = NativeMethods.alpm_option_get_noupgrades(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_noupgrade(_handle, stringPtr);
    Marshal.FreeHGlobal((nint)stringPtr);
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

  public void CopyTo(string[] array, int arrayIndex)
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
