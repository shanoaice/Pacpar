using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Database(byte* backingStruct)
{
  public static Database Factory(void* ptr) => new((byte*)ptr);

  public string Name => field ??= Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_db_get_name(backingStruct))!;

  public Package GetPackage(string name)
  {
    var nameCstr = Marshal.StringToHGlobalAnsi(name);
    var pkg = NativeMethods.alpm_db_get_pkg(backingStruct, (byte*)nameCstr);
    Marshal.FreeHGlobal(nameCstr);
    if ((nint)pkg == IntPtr.Zero)
    {
      throw ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle))!;
    }
    return new Package(pkg);
  }

  public AlpmDisposableList<Package> GetPackageCache()
  {
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(backingStruct);
    if ((nint)pkgCache == IntPtr.Zero)
    {
      throw ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle))!;
    }
    return new AlpmDisposableList<Package>(pkgCache, &Package.FactoryFromDatabase);
  }

  public AlpmStringList GetServers()
  {
    var servers = NativeMethods.alpm_db_get_servers(backingStruct);
    if (servers == null)
    {
      return new AlpmStringList();
    }
    return new AlpmStringList(servers);
  }

  public AlpmStringList GetCacheServers()
  {
    var servers = NativeMethods.alpm_db_get_cache_servers(backingStruct);
    if (servers == null)
    {
      return new AlpmStringList();
    }
    return new AlpmStringList(servers);
  }

  public Group GetGroup(string name)
  {
    var nameCstr = Marshal.StringToHGlobalAnsi(name);
    var group = NativeMethods.alpm_db_get_group(backingStruct, (byte*)nameCstr);
    Marshal.FreeHGlobal(nameCstr);
    if ((nint)group == IntPtr.Zero)
    {
      throw ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle))!;
    }
    return new Group(group);
  }

  public AlpmList<Group> GetGroupCache()
  {
    var groupCache = NativeMethods.alpm_db_get_groupcache(backingStruct);
    if ((nint)groupCache == IntPtr.Zero)
    {
      throw ErrorHandler.GetException(NativeMethods.alpm_errno((byte*)Handle))!;
    }
    return new AlpmList<Group>(groupCache, &Group.Factory);
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
