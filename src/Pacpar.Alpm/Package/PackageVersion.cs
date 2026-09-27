using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package version (<c>alpm_pkg_get_version</c>), ordered by libalpm's own
/// <c>alpm_pkg_vercmp</c>.
/// </summary>
/// <remarks>
/// A managed snapshot: the version text is copied on construction, so a value taken from
/// <see cref="PackageView.Version"/> stays readable after the package that produced it is gone.
/// libalpm can only compare native strings, so <see cref="CompareTo"/> marshals both operands for
/// the duration of the call instead of holding a pointer to package-owned memory.
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

    var left = NativeString.ToNative(_value);
    try
    {
      var right = NativeString.ToNative(other._value);
      try
      {
        return NativeMethods.alpm_pkg_vercmp(left, right);
      }
      finally
      {
        Marshal.FreeHGlobal((nint)right);
      }
    }
    finally
    {
      Marshal.FreeHGlobal((nint)left);
    }
  }

  public override string ToString() => _value;
}
