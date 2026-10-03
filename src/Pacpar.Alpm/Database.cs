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
  private _alpm_db_t* BackingStruct { get; }

  /// <summary>
  /// The only way to obtain the native pointer from outside this class. The guard runs here, so a
  /// sibling cannot read the pointer of a released database.
  /// </summary>
  internal _alpm_db_t* ValidatedPtr()
  {
    ThrowIfInvalidated();
    return BackingStruct;
  }

  private _alpm_handle_t* RawHandle => NativeMethods.alpm_db_get_handle(BackingStruct);

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
    this.BackingStruct = backingStruct;
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

  /// <summary>
  /// The name of this database.
  /// </summary>
  public string Name
  {
    get
    {
      ThrowIfInvalidated();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(BackingStruct))!;
    }
  }

  /// <summary>
  /// Finds a package by name within this database.
  /// </summary>
  /// <param name="name">The name of the package to find.</param>
  /// <returns>A <see cref="PackageView"/> for the package, or <c>null</c> if not found.</returns>
  public PackageView? GetPackage(string name)
  {
    ThrowIfInvalidated();
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var pkg = NativeMethods.alpm_db_get_pkg(BackingStruct, nameBuf.Ptr);
    if ((nint)pkg == IntPtr.Zero) return null;
    return new PackageView(pkg, Lifetime);
  }

  /// <summary>
  /// The package cache of this database.
  /// </summary>
  /// <remarks>
  /// A <c>null</c> native cache with an ok errno is a legitimately empty cache and yields an empty
  /// view; a non-ok errno means loading the cache failed and is thrown. That rule is sound here and
  /// only here, because <c>alpm_db_get_pkgcache</c> clears <c>pm_errno</c> in its first statement -
  /// see <see cref="ThrowIfTheLastCallFailed"/>. The view is borrowed and never frees the cache (see
  /// <see cref="AlpmList{T}"/>).
  /// </remarks>
  public AlpmList<PackageView> GetPackageCache()
  {
    ThrowIfInvalidated();
    var handlePtr = RawHandle;
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(BackingStruct);
    ThrowIfTheLastCallFailed(handlePtr, "load package cache");
    return AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
  }

  /// <summary>
  /// The servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetServers()
  {
    ThrowIfInvalidated();
    var servers = NativeMethods.alpm_db_get_servers(BackingStruct);
    return new AlpmStringList(servers, Lifetime);
  }

  /// <summary>
  /// The cache servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetCacheServers()
  {
    ThrowIfInvalidated();
    var servers = NativeMethods.alpm_db_get_cache_servers(BackingStruct);
    return new AlpmStringList(servers, Lifetime);
  }

  /// <summary>
  /// Finds a package group by name within this database.
  /// </summary>
  /// <param name="name">The name of the group to find.</param>
  /// <returns>A <see cref="Group"/> matching the specified name, or <c>null</c> if not found.</returns>
  public Group? GetGroup(string name)
  {
    ThrowIfInvalidated();
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var group = NativeMethods.alpm_db_get_group(BackingStruct, nameBuf.Ptr);
    if ((nint)group == IntPtr.Zero) return null;
    return new Group(group, Lifetime);
  }

  /// <summary>
  /// The group cache of this database. See <see cref="GetPackageCache"/> for the null/errno rule.
  /// </summary>
  public AlpmList<Group> GetGroupCache()
  {
    ThrowIfInvalidated();
    var handlePtr = RawHandle;
    var groupCache = NativeMethods.alpm_db_get_groupcache(BackingStruct);
    ThrowIfTheLastCallFailed(handlePtr, "load group cache");
    return AlpmList<Group>.Borrow(groupCache, &Group.Factory, Lifetime);
  }

  /// <summary>
  /// Throws when the call that just returned left an error on the handle.
  /// </summary>
  /// <param name="handlePtr">The handle, captured before the call being checked.</param>
  /// <param name="operation">What failed, e.g. <c>"load package cache"</c>.</param>
  /// <remarks>
  /// This is only valid after an entry point that resets <c>pm_errno</c> at entry, because only then
  /// is a non-ok value guaranteed to come from that call. <c>alpm_db_get_pkgcache</c> and
  /// <c>alpm_db_get_groupcache</c> both do; the two server getters deliberately do not, which is why
  /// <see cref="GetServers"/> and <see cref="GetCacheServers"/> cannot use this and must treat a null
  /// list as "none configured" whatever the handle's errno happens to say. Anything that reports
  /// failure through its return value goes through <see cref="NativeCall"/> instead.
  /// </remarks>
  private void ThrowIfTheLastCallFailed(_alpm_handle_t* handlePtr, string operation)
  {
    var errno = NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);
    if (errno != _alpm_errno_t.ALPM_ERR_OK) throw ErrorHandler.ToException((int)errno, operation);
  }

  /// <summary>
  /// The signature verification level configured for this database.
  /// </summary>
  public SigLevel SigLevel
  {
    get
    {
      ThrowIfInvalidated();
      var sig = (SigLevel)NativeMethods.alpm_db_get_siglevel(BackingStruct);
      GC.KeepAlive(this);
      return sig;
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

    // Captured before the call: on the failure path the errno read has to be the next native
    // interaction, so it cannot be the property access that fetches the handle.
    var handlePtr = RawHandle;
    var err = NativeMethods.alpm_db_unregister(BackingStruct);
    if (err != 0)
    {
      GC.KeepAlive(this);
      throw NativeCall.Failure(handlePtr, "unregister database");
    }
    Lifetime.Invalidate("Database.Unregister()");
    Lifetime.Parent?.ForgetHandle(BackingStruct);
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
      var valid = NativeMethods.alpm_db_get_valid(BackingStruct) == 0;
      GC.KeepAlive(this);
      return valid;
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
    var handlePtr = RawHandle;
    if (IsValid) return;

    GC.KeepAlive(this);
    throw NativeCall.Failure(handlePtr, "validate database");
  }
  /// <summary>
  /// Throws <see cref="AlpmLifetimeException"/> when this database was released; the guard every
  /// public accessor runs before dereferencing <c>backingStruct</c>.
  /// </summary>
  private void ThrowIfInvalidated() => Lifetime.ThrowIfStale();
}
