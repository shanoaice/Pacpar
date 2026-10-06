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
/// members belong to the database that issued them, so reading one after that database was
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
  /// packages themselves stay owned by their databases, and each member stays tied to the database
  /// that owns it. Unregistering one database therefore retires its own members, but not the members
  /// of a sibling database.
  /// </remarks>
  public unsafe IReadOnlyList<PackageView> FindGroupPackages(AlpmList<Database> dbs)
  {
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(Name, scratch);
    var result = NativeMethods.alpm_find_group_pkgs(dbs.ValidatedNative(), nameBuf.Ptr);
    var list = AlpmOwnedList<PackageView>.Take(result, &GroupPackageFactory, null, dbs.Lifetime?.Domain);
    GC.KeepAlive(this);
    return list;
  }

  /// <summary>
  /// Element factory for <see cref="FindGroupPackages"/>: binds a package to the domain of the
  /// database that owns it.
  /// </summary>
  /// <remarks>
  /// <c>alpm_find_group_pkgs</c> answers with packages from several databases and does not say which
  /// one each came from, so the owner is asked for with <c>alpm_pkg_get_db</c> and resolved through
  /// the session's pointer registry. Without this the members would all carry the session domain,
  /// which is correct but coarse: it is the last domain to move, so one database being unregistered
  /// would retire packages that still belong to a live sibling.
  /// <para>
  /// A package with no database - one this library loaded from a file - falls back to the session,
  /// because nothing narrower owns it.
  /// </para>
  /// </remarks>
  internal static unsafe PackageView GroupPackageFactory(void* ptr, Lifetime? sessionDomain)
  {
    if (sessionDomain is null) return new PackageView((_alpm_pkg_t*)ptr, null);

    var db = NativeMethods.alpm_pkg_get_db((_alpm_pkg_t*)ptr);
    return new PackageView((_alpm_pkg_t*)ptr,
      db is null ? sessionDomain : sessionDomain.Root.GetLifetimeTokenForHandle(db, "a group member's database"));
  }
}
