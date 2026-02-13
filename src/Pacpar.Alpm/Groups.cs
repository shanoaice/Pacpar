using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Group(_alpm_group_t* backingStruct)
{
  public static Group Factory(void* ptr) => new((_alpm_group_t*)ptr);

  private string? _name;
  // ReSharper disable once MemberCanBePrivate.Global
  public string Name => _name ??= Marshal.PtrToStringAnsi((nint)backingStruct->name)!;

  public AlpmDisposableList<Package> Packages => new(backingStruct->packages, &Package.FactoryFromDatabase);

  public AlpmDisposableList<Package> FindGroupPackages(AlpmList<Databases> dbs) => new(NativeMethods.alpm_find_group_pkgs(dbs.AlpmListNative, (byte*)Marshal.StringToHGlobalAnsi(Name)), &Package.FactoryFromDatabase);
}
