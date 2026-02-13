using System.Collections;
using System.Runtime.InteropServices;
using Pacpar.Alpm.list;

namespace Pacpar.Alpm.options;

internal class ArchitectureOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe ArchitectureOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_architectures(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_architectures(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_architecture(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_architectures(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_architecture(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class AssumeInstalledOptionCollection : ICollection<Depend>
{
  private readonly unsafe byte* _handle;

  internal unsafe AssumeInstalledOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmList<Depend> BackingList => new(NativeMethods.alpm_option_get_assumeinstalled(_handle), &Depend.Factory, false);

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_assumeinstalled(_handle));

  public IEnumerator<Depend> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(Depend item)
  {
    var err = NativeMethods.alpm_option_add_assumeinstalled(_handle, item._backing_struct);
    if (err != 0) throw ErrorHandler.GetException(NativeMethods.alpm_errno(_handle))!;
  }

  public unsafe bool Contains(Depend item)
  {
    var itemPtr = item._backing_struct;
    var optionsList = NativeMethods.alpm_option_get_assumeinstalled(_handle);
    return NativeMethods.alpm_list_find_ptr(optionsList, itemPtr) == itemPtr;
  }

  public unsafe bool Remove(Depend item)
  {
    var itemPtr = item._backing_struct;
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class HookDirectoriesOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe HookDirectoriesOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_hookdirs(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_hookdirs(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_hookdir(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_hookdirs(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_hookdir(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class IgnoreGroupsOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe IgnoreGroupsOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_ignoregroups(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_ignoregroups(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class IgnorePackagesOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe IgnorePackagesOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_ignorepkgs(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_ignorepkgs(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_ignorepkg(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_ignorepkgs(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_ignorepkg(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class NoExtractOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe NoExtractOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_noextracts(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_noextracts(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_noextract(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_noextracts(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_noextract(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class NoUpgradeOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe NoUpgradeOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_noupgrades(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_noupgrades(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_noupgrade(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class CacheDirectoriesOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe CacheDirectoriesOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_cachedirs(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_cachedirs(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_cachedir(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_cachedirs(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_cachedir(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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

internal class OverwritableFilesOptionCollection : ICollection<string>
{
  private readonly unsafe byte* _handle;

  internal unsafe OverwritableFilesOptionCollection(byte* handle)
  {
    _handle = handle;
  }

  private unsafe AlpmStringList BackingList => new(NativeMethods.alpm_option_get_overwrite_files(_handle));

  public bool IsReadOnly => false;

  public unsafe int Count => (int)NativeMethods.alpm_list_count(NativeMethods.alpm_option_get_overwrite_files(_handle));

  public IEnumerator<string> GetEnumerator() => BackingList.GetOwningEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public unsafe void Add(string arch)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(arch);
    try
    {
      var err = NativeMethods.alpm_option_add_overwrite_file(_handle, stringPtr);
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
    var optionsList = NativeMethods.alpm_option_get_overwrite_files(_handle);
    var found = NativeMethods.alpm_list_find_str(optionsList, stringPtr) == stringPtr;
    Marshal.FreeHGlobal((nint)stringPtr);
    return found;
  }

  public unsafe bool Remove(string item)
  {
    var stringPtr = (byte*)Marshal.StringToHGlobalAnsi(item);
    var err = NativeMethods.alpm_option_remove_overwrite_file(_handle, stringPtr);
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
    for (var i = 0; i < arrayIndex; ++i)
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
