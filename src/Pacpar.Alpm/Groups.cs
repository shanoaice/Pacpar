using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public unsafe class Group
{
  private readonly _alpm_group_t* backingStruct;

  internal Group(_alpm_group_t* backingStruct)
  {
    this.backingStruct = backingStruct;
  }

  internal static Group Factory(void* ptr) => new((_alpm_group_t*)ptr);

  // ReSharper disable once MemberCanBePrivate.Global
  public string Name => field ??= NativeString.FromNative((nint)backingStruct->name)!;

  /// <summary>
  /// Packages that belong to this group. The list is owned by the group, so the view never frees it.
  /// </summary>
  public AlpmList<Package> Packages => AlpmList<Package>.Borrow(backingStruct->packages, &Package.Factory);

  /// <summary>
  /// Finds group members across <paramref name="dbs"/>.
  /// </summary>
  /// <remarks>
  /// libalpm allocates the returned list for the caller ("caller is responsible for
  /// <c>alpm_list_free</c>"), so it is copied into a managed collection and freed here. The
  /// packages themselves stay owned by the databases.
  /// </remarks>
  public IReadOnlyList<Package> FindGroupPackages(AlpmList<Database> dbs)
  {
    var namePtr = NativeString.ToNative(Name);
    try
    {
      var result = NativeMethods.alpm_find_group_pkgs(dbs.Native, namePtr);
      try
      {
        var packages = new List<Package>();
        for (var node = result; node != null; node = NativeMethods.alpm_list_next(node))
        {
          packages.Add(Package.Factory(node->data));
        }

        return packages;
      }
      finally
      {
        AlpmNativeList.Free(result, null);
      }
    }
    finally
    {
      Marshal.FreeHGlobal((nint)namePtr);
    }
  }
}
