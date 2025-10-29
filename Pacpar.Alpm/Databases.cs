using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public unsafe class Databases(byte* backing_struct)
{
  protected byte* _backing_struct = backing_struct;
  private string? _name;
  public string Name => _name ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_db_get_name(_backing_struct))!;

  public Package GetPackage(string name)
  {
    var name_cstr = Marshal.StringToHGlobalAnsi(name);
    var pkg = NativeMethods.alpm_db_get_pkg(_backing_struct, (byte*)name_cstr);
    Marshal.FreeHGlobal(name_cstr);
    return new Package(pkg, true);
  }

  public AlpmDisposableList<Package> GetPackageCache() => new(NativeMethods.alpm_db_get_pkgcache(_backing_struct), &Package.FactoryFromDatabase);

  public AlpmStringList GetServers() => new(NativeMethods.alpm_db_get_servers(_backing_struct));

  public AlpmStringList GetCacheServers() => new(NativeMethods.alpm_db_get_cache_servers(_backing_struct));

  // TODO: GROUP, GROUP_CACHE

  public nint Handle => (nint)NativeMethods.alpm_db_get_handle(_backing_struct);

  public SigLevel SigLevel => (SigLevel)NativeMethods.alpm_db_get_siglevel(_backing_struct);

  // TODO: USAGE

  public (bool, Exception?) Validate()
  {
    var valid = NativeMethods.alpm_db_get_valid(_backing_struct);
    if (valid != 0) return (false, ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle)));
    return (true, null);
  }
}