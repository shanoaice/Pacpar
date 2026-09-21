#pragma warning disable SYSLIB1054
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

internal static class MemoryManagement
{
  [DllImport("libc", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
  internal extern unsafe static void CFree(void* ptr);

  /// <summary>
  /// libc <c>free</c> as an element destructor for <c>alpm_list_free_inner</c>.
  /// </summary>
  /// <remarks>
  /// A method cannot carry both <see cref="DllImportAttribute"/> and
  /// <see cref="UnmanagedCallersOnlyAttribute"/>, so this thunk forwards to <see cref="CFree"/>.
  /// </remarks>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  internal static unsafe void CFreeExtern(void* ptr)
  {
    CFree(ptr);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  internal unsafe static void UnmanagedFreeExtern(void* ptr)
  {
    Marshal.FreeHGlobal((nint)ptr);
  }

  /// <summary>
  /// Element destructor for lists of <c>alpm_depmissing_t</c> that the caller owns.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  internal static unsafe void DepMissingFreeExtern(void* ptr)
  {
    NativeMethods.alpm_depmissing_free((_alpm_depmissing_t*)ptr);
  }
}
