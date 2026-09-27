using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// One entry of a package's file list (<c>alpm_file_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: mode, size and name are copied out of the borrowed array when the entry is
/// read, so a <see cref="PackageFile"/> taken from <see cref="PackageView.Files"/> stays valid.
/// </remarks>
public readonly struct PackageFile
{
  internal unsafe PackageFile(_alpm_file_t* backingStruct)
  {
    Mode = backingStruct->mode;
    Size = backingStruct->size;
    Name = NativeString.FromNative((nint)backingStruct->name);
  }

  // The token parameter matches the element-factory delegate signature; a PackageFile is an eager
  // snapshot and needs no element token.
  internal static unsafe PackageFile Factory(void* ptr, Lifetime? lifetime) => new((_alpm_file_t*)ptr);

  public uint Mode { get; }

  public CLong Size { get; }

  public string? Name { get; }
}
