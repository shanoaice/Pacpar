using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// One entry of a package's file list (<c>alpm_file_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: mode, size and name are copied out of the package's file list when the entry is
/// read, so a <see cref="PackageFile"/> taken from <see cref="PackageBase.Files"/> remains valid.
/// </remarks>
public readonly struct PackageFile
{
  internal unsafe PackageFile(_alpm_file_t* backingStruct)
  {
    Mode = backingStruct->mode;
    Size = backingStruct->size.Value;
    Name = NativeString.FromNative((nint)backingStruct->name);
  }

  internal static unsafe PackageFile Factory(void* ptr, Lifetime? lifetime) => new((_alpm_file_t*)ptr);

  /// <summary>
  /// The POSIX file mode and permissions.
  /// </summary>
  public uint Mode { get; }

  /// <summary>
  /// The size of the file in bytes.
  /// </summary>
  public long Size { get; }

  /// <summary>
  /// The relative path of the file within the package.
  /// </summary>
  public string? Name { get; }
}
