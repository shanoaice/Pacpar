#pragma warning disable SYSLIB1054
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

/// <summary>
/// Expands the <c>(fmt, va_list)</c> payload libalpm hands to <c>alpm_cb_log</c>.
/// </summary>
/// <remarks>
/// A <c>va_list</c> cannot be declared in C# (there is no variadic P/Invoke, and no way to
/// materialise one), so the managed thunks receive it as an opaque <see cref="void"/> pointer and
/// pass it straight to libc's <c>vasprintf</c>. That mapping is the whole trick: a <c>va_list</c>
/// parameter is delivered as a pointer on every ABI .NET supports on Linux -- x86_64 through array
/// decay and AArch64 by reference, and likewise on riscv64, powerpc64le, s390x, armv7, i686 and
/// loongarch64 -- so the C# side never has to name the type.
/// </remarks>
internal static unsafe class LogMessageFormatter
{
  [DllImport("libc", EntryPoint = "vasprintf", CallingConvention = CallingConvention.Cdecl)]
  private static extern int Vasprintf(byte** buffer, byte* format, void* vaList);

  /// <summary>
  /// Formats one log message. Returns <see langword="null"/> when libalpm passed a null format
  /// string, when the format is invalid, or when the buffer could not be allocated.
  /// </summary>
  /// <param name="format">The format string libalpm passed to <c>alpm_cb_log</c>.</param>
  /// <param name="vaList">
  /// The raw <c>va_list</c> libalpm passed to <c>alpm_cb_log</c>, as an opaque pointer. It is
  /// consumed by this call, which is why there is exactly one <c>vasprintf</c> call here: a
  /// <c>va_list</c> is a one-shot cursor, so it cannot be measured first and then filled.
  /// </param>
  internal static string? Format(byte* format, void* vaList)
  {
    if (format == null) return null;

    byte* buffer = null;
    if (Vasprintf(&buffer, format, vaList) < 0) return null;

    try
    {
      return NativeString.FromNative((nint)buffer);
    }
    finally
    {
      // vasprintf allocates with malloc, and CFree is libc's free.
      MemoryManagement.CFree(buffer);
    }
  }
}
