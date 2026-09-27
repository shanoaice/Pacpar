using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Locks in the <c>va_list</c> forwarding contract behind <see cref="LogMessageFormatter"/>:
/// libalpm calls <c>alpm_cb_log</c> with a <c>(fmt, va_list)</c> pair, the managed thunk receives
/// that argument as an opaque pointer, and libc expands it.
/// </summary>
/// <remarks>
/// The harness in <c>native/pacpar_log_shim_test.c</c> builds a genuine <c>va_list</c> from a real
/// variadic call site, which is the only way to exercise this path (C# cannot materialise one).
/// That makes the test architecture-sensitive on purpose: it is what pins down the assumption that
/// a <c>va_list</c> parameter travels as a pointer. If the managed signature ever stops agreeing
/// with the C one, it fails here instead of at runtime, and running this suite on AArch64 checks
/// the second ABI rather than just the x86_64 one.
/// </remarks>
public sealed unsafe class LogMessageFormatterTests
{
  private const string HarnessLibrary = "pacpar_log_shim_test";

  [DllImport(HarnessLibrary, EntryPoint = "pacpar_shim_test_invoke", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
  private static extern void Invoke(
    delegate* unmanaged[Cdecl]<void*, int, byte*, void*, void> callback,
    void* context,
    int level,
    byte* format,
    byte* text,
    int number,
    double fraction);

  [Fact]
  public void Format_ExpandsTheVaListLibalpmPassesToTheLogCallback()
  {
    var received = Capture("log %s #%d %.0f", "中文 / ünïcødé", 42, 1000.0);

    Assert.Equal(new string?[] { "log 中文 / ünïcødé #42 1000" }, received);
  }

  [Fact]
  public void Format_ReturnsNull_WhenTheFormatStringIsNull()
  {
    var received = Capture(null, "ignored", 0, 0);

    Assert.Equal(new string?[] { null }, received);
  }

  /// <summary>
  /// Formats through <see cref="LogMessageFormatter"/> from a thunk that was entered the way
  /// libalpm enters it. <paramref name="fraction"/> is exercised so the floating-point part of the
  /// register save area is read back too, not just the general-purpose part.
  /// </summary>
  private static List<string?> Capture(string? format, string text, int number, double fraction)
  {
    var received = new List<string?>();
    var handle = new GCHandle<List<string?>>(received);
    var formatPointer = Utf8(format);
    var textPointer = Utf8(text);

    try
    {
      Invoke(&LogThunk, (void*)GCHandle<List<string?>>.ToIntPtr(handle), 2, formatPointer, textPointer, number, fraction);
    }
    finally
    {
      Free(formatPointer);
      Free(textPointer);
      handle.Dispose();
    }

    return received;
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static void LogThunk(void* context, int level, byte* format, void* vaList)
  {
    var received = GCHandle<List<string?>>.FromIntPtr((nint)context).Target;
    received.Add(LogMessageFormatter.Format(format, vaList));
  }

  private static byte* Utf8(string? value)
    => value is null ? null : (byte*)Marshal.StringToCoTaskMemUTF8(value);

  private static void Free(void* pointer)
  {
    if (pointer != null) Marshal.FreeCoTaskMem((nint)pointer);
  }
}
