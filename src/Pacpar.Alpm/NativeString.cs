using System.Runtime.InteropServices;
using System.Text;

namespace Pacpar.Alpm;

/// <summary>
/// Marshalling helpers for the NUL-terminated C strings libalpm exchanges with this library.
/// </summary>
/// <remarks>
/// libalpm's string contract is UTF-8, not the platform ANSI code page: package descriptions, file
/// names and database names routinely contain non-ASCII text. The conversions are therefore
/// explicit here instead of going through the platform-ANSI marshal helpers, which encode and
/// decode with the host's ANSI code page and thus silently mojibake libalpm's UTF-8 on Windows.
/// </remarks>
internal static unsafe class NativeString
{
  /// <summary>Decodes a libalpm string; a null pointer yields <c>null</c>.</summary>
  internal static string? FromNative(nint value) => Marshal.PtrToStringUTF8(value);

  /// <summary>
  /// Copies <paramref name="value"/> into a NUL-terminated UTF-8 buffer allocated with
  /// <see cref="Marshal.AllocHGlobal"/>; a null input yields a null pointer.
  /// </summary>
  /// <remarks>The caller owns the result and frees it with <see cref="Marshal.FreeHGlobal"/>.</remarks>
  internal static byte* ToNative(string? value)
  {
    if (value is null) return null;

    var byteCount = Encoding.UTF8.GetByteCount(value);
    var buffer = (byte*)Marshal.AllocHGlobal(byteCount + 1);
    Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, byteCount));
    buffer[byteCount] = 0;
    return buffer;
  }
}
