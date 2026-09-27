using System.ComponentModel;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A package database: the local database or a registered sync tree.
/// </summary>
/// <remarks>
/// A borrowed handle: libalpm owns the database, this wrapper only reads through it. Every accessor
/// validates the database's <see cref="Lifetime"/> token first, so using a database after
/// <see cref="Unregister"/>, after <see cref="Alpm.UnregisterAllSyncDatabases"/> or after the owning
/// <see cref="Alpm"/> was released throws <see cref="AlpmLifetimeException"/> instead of reading
/// freed memory. Views handed out here (packages, groups, server lists) carry the same token and
/// die with it.
/// </remarks>
public unsafe class Database
{
  private readonly _alpm_db_t* backingStruct;

  /// <summary>
  /// The token guarding this database and every view issued from it. Registered in the handle's
  /// pointer registry (sync databases) or held by a dedicated <see cref="Alpm"/> field (the local
  /// database, which <c>alpm_unregister_all_syncdbs</c> never releases).
  /// </summary>
  internal readonly Lifetime Lifetime;

  /// <param name="backingStruct">The libalpm-owned database.</param>
  /// <param name="lifetime">The database's token; all accessors and issued views guard on it.</param>
  internal Database(_alpm_db_t* backingStruct, Lifetime lifetime)
  {
    this.backingStruct = backingStruct;
    Lifetime = lifetime;
  }

  /// <summary>
  /// Element factory for <see cref="Alpm.GetSyncDatabases"/>: resolves the per-database token from
  /// the handle registry keyed by the native pointer, so two wrappers for the same database - and
  /// two enumerations of the same list - share one token and one invalidation.
  /// </summary>
  /// <remarks>
  /// The name is read only when the pointer is not registered yet: on a fresh list the database is
  /// alive and the read is safe. A list obtained <i>before</i> a registry mutation
  /// (<see cref="Alpm.RegisterSyncDatabase"/>, <see cref="Unregister"/>,
  /// <see cref="Alpm.UnregisterAllSyncDatabases"/>) can hold nodes those calls freed; re-fetch the
  /// list after mutating the registry instead of re-enumerating a stale one.
  /// </remarks>
  internal static Database Factory(void* ptr, Lifetime? lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    var name = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name((_alpm_db_t*)ptr)) ?? "(unknown)";
    return new Database((_alpm_db_t*)ptr, lifetime.GetLifetimeTokenForHandle(ptr, $"the sync database {name}"));
  }

  public string Name
  {
    get
    {
      ThrowIfInvalidated();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(backingStruct))!;
    }
  }

  public PackageView? GetPackage(string name)
  {
    ThrowIfInvalidated();
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var pkg = NativeMethods.alpm_db_get_pkg(backingStruct, nameBuf.Ptr);
    if ((nint)pkg == IntPtr.Zero) return null;
    return new PackageView(pkg, Lifetime);
  }

  /// <summary>
  /// The package cache of this database.
  /// </summary>
  /// <remarks>
  /// A <c>null</c> native cache with an ok errno is a legitimately empty cache and yields an empty
  /// view; any other errno is thrown. The view is borrowed and never frees the cache (see
  /// <see cref="AlpmList{T}"/>).
  /// </remarks>
  public AlpmList<PackageView> GetPackageCache()
  {
    ThrowIfInvalidated();
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(backingStruct);
    ThrowIfErrnoSet();
    return AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
  }

  /// <summary>
  /// The servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetServers()
  {
    ThrowIfInvalidated();
    var servers = NativeMethods.alpm_db_get_servers(backingStruct);
    ThrowIfErrnoSet();
    return new AlpmStringList(servers, Lifetime);
  }

  /// <summary>
  /// The cache servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetCacheServers()
  {
    ThrowIfInvalidated();
    var servers = NativeMethods.alpm_db_get_cache_servers(backingStruct);
    ThrowIfErrnoSet();
    return new AlpmStringList(servers, Lifetime);
  }

  public Group? GetGroup(string name)
  {
    ThrowIfInvalidated();
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var group = NativeMethods.alpm_db_get_group(backingStruct, nameBuf.Ptr);
    if ((nint)group == IntPtr.Zero) return null;
    return new Group(group, Lifetime);
  }

  /// <summary>
  /// The group cache of this database. See <see cref="GetPackageCache"/> for the null/errno rule.
  /// </summary>
  public AlpmList<Group> GetGroupCache()
  {
    ThrowIfInvalidated();
    var groupCache = NativeMethods.alpm_db_get_groupcache(backingStruct);
    ThrowIfErrnoSet();
    return AlpmList<Group>.Borrow(groupCache, &Group.Factory, Lifetime);
  }

  /// <summary>The raw libalpm handle this database belongs to.</summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public nint AsHandle()
  {
    ThrowIfInvalidated();
    return (nint)NativeMethods.alpm_db_get_handle(backingStruct);
  }

  public SigLevel SigLevel
  {
    get
    {
      ThrowIfInvalidated();
      return (SigLevel)NativeMethods.alpm_db_get_siglevel(backingStruct);
    }
  }

  // TODO: USAGE

  /// <summary>
  /// Unregisters this database from its handle, releasing it.
  /// </summary>
  /// <remarks>
  /// On success the database's lifetime token is invalidated and its registry entry dropped, in
  /// that order: every wrapper and view for this database starts throwing
  /// <see cref="AlpmLifetimeException"/>, and a future registration at the same address receives a
  /// fresh token instead of the dead one. A failed unregister leaves the database - and its token -
  /// alive.
  /// </remarks>
  public void Unregister()
  {
    ThrowIfInvalidated();

    var err = NativeMethods.alpm_db_unregister(backingStruct);
    if (err != 0) throw ErrorHandler.ToException(NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle()));

    // Strictly after the native release succeeded (§4.1 order): invalidate the token, then prune
    // the registry so the address can be re-issued cleanly.
    Lifetime.Invalidate("Database.Unregister()");
    Lifetime.Parent?.ForgetHandle(backingStruct);
  }

  /// <summary>
  /// Whether this database is valid. Never throws: an invalid database is a normal answer.
  /// </summary>
  /// <remarks>
  /// libalpm reports validity as <c>0</c> == valid, <c>-1</c> == invalid. "Never throws" covers the
  /// validity answer only: a database whose lifetime token was invalidated still throws
  /// <see cref="AlpmLifetimeException"/>, because the native struct itself is gone.
  /// </remarks>
  public bool IsValid
  {
    get
    {
      ThrowIfInvalidated();
      return NativeMethods.alpm_db_get_valid(backingStruct) == 0;
    }
  }

  /// <summary>
  /// Validates this database, throwing when it is invalid.
  /// </summary>
  /// <remarks>
  /// libalpm sets the handle errno when it reports an invalid database, and that errno becomes the
  /// thrown exception's. Use <see cref="IsValid"/> when an invalid database is an expected answer
  /// rather than an error.
  /// </remarks>
  public void Validate()
  {
    ThrowIfInvalidated();
    if (IsValid) return;

    var errno = NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle());
    throw errno == _alpm_errno_t.ALPM_ERR_OK
      ? new InvalidOperationException("The database is invalid but libalpm did not set an error code.")
      : ErrorHandler.ToException(errno);
  }

  /// <summary>
  /// Throws when libalpm set the handle errno.
  /// </summary>
  /// <remarks>
  /// Used after a native call whose <c>null</c> return is legitimate when there is no error (an
  /// empty list, for instance): <c>null</c> plus an ok errno means "empty", <c>null</c> plus an
  /// errno means "failed".
  /// </remarks>
  private void ThrowIfErrnoSet()
  {
    var errno = NativeMethods.alpm_errno((_alpm_handle_t*)AsHandle());
    if (errno != _alpm_errno_t.ALPM_ERR_OK) throw ErrorHandler.ToException(errno);
  }

  /// <summary>
  /// Throws <see cref="AlpmLifetimeException"/> when this database was released; the guard every
  /// public accessor runs before dereferencing <c>backingStruct</c>.
  /// </summary>
  private void ThrowIfInvalidated() => Lifetime.ThrowIfStale();
}
