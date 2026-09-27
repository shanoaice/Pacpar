using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Pacpar.Alpm;

/// <summary>
/// A NUL-terminated UTF-8 buffer for handing a managed string to libalpm for the duration of a
/// single call: it encodes into a caller-provided stack scratch buffer when the string fits and
/// falls back to <see cref="NativeMemory.Alloc"/> on the native heap otherwise, so short strings
/// — package names, versions, group names — never touch malloc at all.
/// </summary>
/// <remarks>
/// <para>
/// Use it as a <c>using</c> declaration inside the same frame that performs the native call:
/// </para>
/// <code>
/// Span&lt;byte&gt; scratch = stackalloc byte[64];
/// using var buffer = new Utf8Buffer(name, scratch);
/// NativeMethods.alpm_db_get_pkg(db, buffer.Ptr);
/// </code>
/// <para>
/// The buffer must not outlive the scratch memory it may point into; the ref-struct kind enforces
/// that at compile time (it cannot be boxed, stored on the heap, or escape the calling method).
/// When the string does not fit the scratch, the heap fallback is freed again by
/// <see cref="Dispose"/>, which makes the ownership contract identical to the stack case:
/// exactly one free per construction, never a double free and never a leak.
/// </para>
/// <para>
/// <see cref="NativeString.ToNative"/> remains for buffers that escape the current frame (they
/// are copied into native structures and freed later by a destructor thunk); everything that is
/// allocated and released around a single call site should prefer this type, because a native
/// malloc/free round trip costs tens of nanoseconds while a short stack buffer costs nothing.
/// </para>
/// </remarks>
internal unsafe ref struct Utf8Buffer
{
  private readonly byte* _heap;

  /// <summary>The NUL-terminated UTF-8 bytes, or null when the input string was null.</summary>
  internal readonly byte* Ptr;

  /// <summary>
  /// Encodes <paramref name="value"/> into <paramref name="scratch"/> when it fits (leaving room
  /// for the terminator), otherwise allocates with <see cref="NativeMemory.Alloc"/>.
  /// </summary>
  /// <param name="value">The string to marshal; null yields a null pointer, matching <see cref="NativeString.ToNative"/>.</param>
  /// <param name="scratch">Stack memory owned by the caller; 64 bytes suits names and versions, 256 suits paths and descriptions.</param>
  internal Utf8Buffer(string? value, scoped Span<byte> scratch)
  {
    _heap = null;
    if (value is null)
    {
      Ptr = null;
      return;
    }

    var byteCount = Encoding.UTF8.GetByteCount(value);
    if (byteCount + 1 <= scratch.Length)
    {
      Encoding.UTF8.GetBytes(value, scratch);
      scratch[byteCount] = 0;
      Ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(scratch));
    }
    else
    {
      var heap = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
      Encoding.UTF8.GetBytes(value, new Span<byte>(heap, byteCount));
      heap[byteCount] = 0;
      _heap = heap;
      Ptr = heap;
    }
  }

  /// <summary>Frees the heap fallback when one was taken; a stack-only buffer is a no-op.</summary>
  public void Dispose()
  {
    if (_heap != null) NativeMemory.Free(_heap);
  }
}
