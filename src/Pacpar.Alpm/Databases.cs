using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Databases(byte* backingStruct)
{
  private string? _name;
  public string Name => _name ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_db_get_name(backingStruct))!;

  public Package GetPackage(string name)
  {
    var nameCstr = Marshal.StringToHGlobalAnsi(name);
    var pkg = NativeMethods.alpm_db_get_pkg(backingStruct, (byte*)nameCstr);
    Marshal.FreeHGlobal(nameCstr);
    return new Package(pkg);
  }

  public AlpmDisposableList<Package> GetPackageCache() => new(NativeMethods.alpm_db_get_pkgcache(backingStruct), &Package.FactoryFromDatabase);

  public AlpmStringList GetServers() => new(NativeMethods.alpm_db_get_servers(backingStruct));

  public AlpmStringList GetCacheServers() => new(NativeMethods.alpm_db_get_cache_servers(backingStruct));

  public Group GetGroup(string name) => new(NativeMethods.alpm_db_get_group(backingStruct, (byte*)Marshal.StringToHGlobalAnsi(name)));

  public AlpmList<Group> GetGroupCache()
  {
    var groupCache = NativeMethods.alpm_db_get_groupcache(backingStruct);
    if ((nint)groupCache == IntPtr.Zero)
    {
      throw ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle))!;
    }
    return new(groupCache, &Group.Factory);
  }

  public nint Handle => (nint)NativeMethods.alpm_db_get_handle(backingStruct);

  public SigLevel SigLevel => (SigLevel)NativeMethods.alpm_db_get_siglevel(backingStruct);

  // TODO: USAGE

  public (bool, Exception?) Validate()
  {
    var valid = NativeMethods.alpm_db_get_valid(backingStruct);
    if (valid != 0) return (false, ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle)));
    return (true, null);
  }
}
