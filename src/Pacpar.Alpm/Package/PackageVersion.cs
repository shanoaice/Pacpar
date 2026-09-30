using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package version (<c>alpm_pkg_get_version</c>), ordered by libalpm's own
/// <c>alpm_pkg_vercmp</c>.
/// </summary>
/// <remarks>
/// A managed snapshot: the version text is copied on construction, so a value taken from
/// <see cref="PackageBase.Version"/> stays valid after the package that produced it is disposed.
/// </remarks>
public class PackageVersion : IComparable<PackageVersion>
{
  private readonly string _value;

  internal unsafe PackageVersion(byte* version)
  {
    _value = NativeString.FromNative((nint)version) ?? string.Empty;
  }

  public unsafe int CompareTo(PackageVersion? other)
  {
    if (other == null) return 1;

    // Versions are short (64 bytes covers realistic epoch/pkgver/pkgrel strings), so both
    // operands marshal onto stack scratch and the comparison never touches the native heap.
    Span<byte> leftScratch = stackalloc byte[64];
    Span<byte> rightScratch = stackalloc byte[64];
    using var left = new Utf8Buffer(_value, leftScratch);
    using var right = new Utf8Buffer(other._value, rightScratch);
    return NativeMethods.alpm_pkg_vercmp(left.Ptr, right.Ptr);
  }

  public override string ToString() => _value;
}
