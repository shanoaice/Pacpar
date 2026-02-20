using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.List;

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
