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
/// libalpm's group cache moves on. Its members are <see cref="PackageView"/> views, because that is
/// what a package always is: libalpm owns it and this library reads through its accessors. The
/// members inherit the issuing database's lifetime token, so reading one after that database was
/// unregistered throws <see cref="AlpmLifetimeException"/> even though the group itself is a copy.
/// </remarks>
public class Group
{
  /// <param name="backingStruct">The libalpm-owned group; dereferenced eagerly.</param>
  /// <param name="lifetime">
  /// Token of the database owning the group and its member packages; forwarded to the members.
  /// </param>
  internal unsafe Group(_alpm_group_t* backingStruct, Lifetime? lifetime)
  {
    Name = NativeString.FromNative((nint)backingStruct->name)!;
    Packages = [.. AlpmList<PackageView>.Borrow(backingStruct->packages, &PackageView.Factory, lifetime)];
  }

  internal static unsafe Group Factory(void* ptr, Lifetime? lifetime) => new((_alpm_group_t*)ptr, lifetime);

  // ReSharper disable once MemberCanBePrivate.Global
  public string Name { get; }

  /// <summary>
  /// Packages that belong to this group, copied out of the group when this snapshot was made.
  /// </summary>
  public IReadOnlyList<PackageView> Packages { get; }

  /// <summary>
  /// Finds group members across <paramref name="dbs"/>.
  /// </summary>
  /// <remarks>
  /// libalpm allocates the returned list for the caller ("caller is responsible for
  /// <c>alpm_list_free</c>"), so it is copied into a managed collection and freed here. The
  /// packages themselves stay owned by the databases, so the materialized members inherit the
  /// databases' lifetime token from <paramref name="dbs"/>.
  /// </remarks>
  public unsafe IReadOnlyList<PackageView> FindGroupPackages(AlpmList<Database> dbs)
  {
    var namePtr = NativeString.ToNative(Name);
    try
    {
      var result = NativeMethods.alpm_find_group_pkgs(dbs.Native, namePtr);
      return AlpmOwnedList<PackageView>.Take(result, &PackageView.Factory, null, dbs.Lifetime);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)namePtr);
    }
  }
}
