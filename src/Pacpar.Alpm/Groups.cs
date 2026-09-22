using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A package group (<c>alpm_group_t</c>).
/// </summary>
/// <remarks>
/// A managed snapshot: the name and the member list are copied on construction, so a group obtained
/// from <see cref="Database.GetGroup"/> or <see cref="Database.GetGroupCache"/> stays readable after
/// libalpm's group cache moves on. Its members are <see cref="Package"/> views, because that is what
/// a package always is: libalpm owns it and this library reads through its accessors.
/// </remarks>
public class Group
{
  internal unsafe Group(_alpm_group_t* backingStruct)
  {
    Name = NativeString.FromNative((nint)backingStruct->name)!;
    Packages = [.. AlpmList<Package>.Borrow(backingStruct->packages, &Package.Factory)];
  }

  internal static unsafe Group Factory(void* ptr) => new((_alpm_group_t*)ptr);

  // ReSharper disable once MemberCanBePrivate.Global
  public string Name { get; }

  /// <summary>
  /// Packages that belong to this group, copied out of the group when this snapshot was made.
  /// </summary>
  public IReadOnlyList<Package> Packages { get; }

  /// <summary>
  /// Finds group members across <paramref name="dbs"/>.
  /// </summary>
  /// <remarks>
  /// libalpm allocates the returned list for the caller ("caller is responsible for
  /// <c>alpm_list_free</c>"), so it is copied into a managed collection and freed here. The
  /// packages themselves stay owned by the databases.
  /// </remarks>
  public unsafe IReadOnlyList<Package> FindGroupPackages(AlpmList<Database> dbs)
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
