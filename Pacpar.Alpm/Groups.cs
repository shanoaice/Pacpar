namespace Pacpar.Alpm;

using System.Runtime.InteropServices;

public unsafe class Group(_alpm_group_t* _backing_struct)
{
  public static unsafe Group Factory(void* ptr) => new((_alpm_group_t*)ptr);

  private string? _name;
  public unsafe string Name => _name ??= Marshal.PtrToStringAnsi((nint)_backing_struct->name)!;

  public unsafe AlpmDisposableList<Package> Packages => new(_backing_struct->packages, &Package.FactoryFromDatabase);

  public unsafe AlpmDisposableList<Package> FindGroupPackages(AlpmList<Databases> dbs) => new(NativeMethods.alpm_find_group_pkgs(dbs._alpmListNative, (byte*)Marshal.StringToHGlobalAnsi(Name)), &Package.FactoryFromDatabase);
}
