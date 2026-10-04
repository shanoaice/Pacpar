using System.Diagnostics.CodeAnalysis;
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
  /// <summary>
  /// The only way to obtain the native pointer from outside this class. The guard runs here, so a
  /// sibling cannot read the pointer of a released database.
  /// </summary>
  internal _alpm_db_t* ValidatedPtr
  {
    get
    {
      ThrowIfInvalidated();
      return field;
    }

    // Assignable from this class's constructor only: a wrapper's pointer is fixed for its lifetime.
    private init;
  }

  private _alpm_handle_t* RawHandle => NativeMethods.alpm_db_get_handle(ValidatedPtr);

  /// <summary>
  /// The domain guarding this database and every view issued from it. Registered in the handle's
  /// pointer registry (sync databases) or held by a dedicated <see cref="Alpm"/> field (the local
  /// database, which <c>alpm_unregister_all_syncdbs</c> never releases).
  /// </summary>
  internal readonly Lifetime Lifetime;

  /// <summary>When this wrapper was created, so <see cref="ThrowIfInvalidated"/> can detect a bump.</summary>
  private readonly LifetimeStamp _stamp;

  /// <param name="backingStruct">The libalpm-owned database.</param>
  /// <param name="lifetime">The database's domain; all accessors and issued views guard on it.</param>
  internal Database(_alpm_db_t* backingStruct, Lifetime lifetime)
  {
    ValidatedPtr = backingStruct;
    Lifetime = lifetime;
    _stamp = lifetime.Capture();
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
      // Explicit guard on purpose: `field ??=` short-circuits once the name is cached, so the guarded
      // accessor would not run on later reads. See the same note on PackageBase.Name.
      ThrowIfInvalidated();
      return field ??= NativeString.FromNative((nint)NativeMethods.alpm_db_get_name(ValidatedPtr))!;
    }
  }

  /// <summary>
  /// Finds a package by name within this database.
  /// </summary>
  /// <param name="name">The name of the package to find.</param>
  /// <returns>A <see cref="PackageView"/> for the package, or <c>null</c> if not found.</returns>
  public PackageView? GetPackage(string name)
  {
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var pkg = NativeMethods.alpm_db_get_pkg(ValidatedPtr, nameBuf.Ptr);
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
  /// see <see cref="LastCallFailed"/>. The view is borrowed and never frees the cache (see
  /// <see cref="AlpmList{T}"/>).
  /// </remarks>
  public AlpmList<PackageView> GetPackageCache()
  {
    if (!TryGetPackageCache(out var cache, out var failure)) throw failure.ToException();
    return cache;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="GetPackageCache"/>.
  /// </summary>
  /// <param name="cache">The borrowed view, or <c>null</c> when the call failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call succeeded.</param>
  /// <returns><c>true</c> when the cache was read.</returns>
  /// <remarks>
  /// The first of the seam's two shapes: an entry point libalpm resets <c>pm_errno</c> in, so the
  /// handle's errno after the call is this call's own and no return value says failure (ADR 0012).
  /// An ok errno means "empty cache", not "no failure to report".
  /// </remarks>
  internal bool TryGetPackageCache([NotNullWhen(true)] out AlpmList<PackageView>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var pkgCache = NativeMethods.alpm_db_get_pkgcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load package cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<PackageView>.Borrow(pkgCache, &PackageView.Factory, Lifetime);
    return true;
  }

  /// <summary>
  /// The servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetServers()
  {
    var servers = NativeMethods.alpm_db_get_servers(ValidatedPtr);
    return new AlpmStringList(servers, Lifetime);
  }

  /// <summary>
  /// The cache servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList GetCacheServers()
  {
    var servers = NativeMethods.alpm_db_get_cache_servers(ValidatedPtr);
    return new AlpmStringList(servers, Lifetime);
  }

  /// <summary>
  /// Finds a package group by name within this database.
  /// </summary>
  /// <param name="name">The name of the group to find.</param>
  /// <returns>A <see cref="Group"/> matching the specified name, or <c>null</c> if not found.</returns>
  public Group? GetGroup(string name)
  {
    Span<byte> scratch = stackalloc byte[64];
    using var nameBuf = new Utf8Buffer(name, scratch);
    var group = NativeMethods.alpm_db_get_group(ValidatedPtr, nameBuf.Ptr);
    if ((nint)group == IntPtr.Zero) return null;
    return new Group(group, Lifetime);
  }

  /// <summary>
  /// The group cache of this database. See <see cref="GetPackageCache"/> for the null/errno rule.
  /// </summary>
  public AlpmList<Group> GetGroupCache()
  {
    if (!TryGetGroupCache(out var cache, out var failure)) throw failure.ToException();
    return cache;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="GetGroupCache"/>. See
  /// <see cref="TryGetPackageCache"/> for the shape and for why this entry point may read the errno.
  /// </summary>
  internal bool TryGetGroupCache([NotNullWhen(true)] out AlpmList<Group>? cache,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    var groupCache = NativeMethods.alpm_db_get_groupcache(ValidatedPtr);

    if (!LastCallFailed(handlePtr, "load group cache", out failure))
    {
      cache = null;
      return false;
    }

    cache = AlpmList<Group>.Borrow(groupCache, &Group.Factory, Lifetime);
    return true;
  }

  /// <summary>
  /// Reports the handle's errno as a failure when the call that just returned left one there.
  /// </summary>
  /// <param name="handlePtr">The handle, captured before the call being checked.</param>
  /// <param name="operation">What failed, e.g. <c>"load package cache"</c>.</param>
  /// <param name="failure">The failure, or <c>null</c> when the call left no errno.</param>
  /// <returns><c>true</c> when the handle reports no error.</returns>
  /// <remarks>
  /// This is only valid after an entry point that resets <c>pm_errno</c> at entry, because only then
  /// is a non-ok value guaranteed to come from that call. <c>alpm_db_get_pkgcache</c> and
  /// <c>alpm_db_get_groupcache</c> both do; the two server getters deliberately do not, which is why
  /// <see cref="GetServers"/> and <see cref="GetCacheServers"/> cannot use this and must treat a null
  /// list as "none configured" whatever the handle's errno happens to say. Anything that reports
  /// failure through its return value goes through <see cref="NativeCall"/> instead.
  /// <para>
  /// An ok errno means the call succeeded, and here that includes "the cache is legitimately empty".
  /// It is not the same reading as <see cref="NativeCall.Failure(int, string)"/> gives the same
  /// value: there, the return value already said the call failed, so a missing errno is a failure
  /// libalpm cannot describe and is thrown.
  /// </para>
  /// </remarks>
  private bool LastCallFailed(_alpm_handle_t* handlePtr, string operation,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var errno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);

    if (errno == (int)_alpm_errno_t.ALPM_ERR_OK)
    {
      failure = null;
      return true;
    }

    failure = AlpmFailure.Of(errno, operation);
    return false;
  }

  /// <summary>
  /// The signature verification level configured for this database.
  /// </summary>
  public SigLevel SigLevel
  {
    get
    {
      var sig = (SigLevel)NativeMethods.alpm_db_get_siglevel(ValidatedPtr);
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
  /// fresh token instead of the dead one. The session root is bumped too, because the unregister
  /// frees this database's node in the handle's sync-database list. A failed unregister leaves the
  /// database - and its token - alive.
  /// </remarks>
  /// <summary>
  /// The failure-returning seam for <see cref="Unregister"/>.
  /// </summary>
  internal bool TryUnregister([NotNullWhen(false)] out AlpmFailure? failure)
  {
    // Both captured before anything is invalidated. ValidatedPtr runs the stamp guard, so it cannot
    // be read again after the Invalidate below - it would throw on its own database. The handle has
    // to come from the same read, because on the failure path the errno read must be the next native
    // interaction after the call that failed.
    var dbPtr = ValidatedPtr;
    var handlePtr = NativeMethods.alpm_db_get_handle(dbPtr);
    var err = NativeMethods.alpm_db_unregister(dbPtr);
    if (err != 0)
    {
      // Read the errno where the failure is known, and only then anchor: KeepAlive keeps this alive
      // up to its own instruction, so it has to come after the last native read on this path.
      var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "unregister database");
      return false;
    }

    Lifetime.Invalidate("Database.Unregister()");
    Lifetime.Root.ForgetHandle(dbPtr);
    // Removing this database also frees its node in the handle's sync-database list, so a retained
    // list view - which is stamped with the session, not with the database - has to die as well.
    // Bumping the root is the conservative way to say "the handle's own lists moved".
    Lifetime.Root.Invalidate("Database.Unregister()");
    failure = null;
    return true;
  }

  /// <summary>
  /// Unregisters this database from its handle, releasing it.
  /// </summary>
  /// <remarks>
  /// On success the database's lifetime token is invalidated and its registry entry dropped, in
  /// that order: every wrapper and view for this database starts throwing
  /// <see cref="AlpmLifetimeException"/>, and a future registration at the same address receives a
  /// fresh token instead of the dead one. The session root is bumped too, because the unregister
  /// frees this database's node in the handle's sync-database list. A failed unregister leaves the
  /// database - and its token - alive.
  /// </remarks>
  public void Unregister()
  {
    if (!TryUnregister(out var failure))
    {
      throw failure.ToException();
    }
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
      var valid = NativeMethods.alpm_db_get_valid(ValidatedPtr) == 0;
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
  /// <summary>
  /// The failure-returning seam for <see cref="Validate"/>.
  /// </summary>
  internal bool TryValidate([NotNullWhen(false)] out AlpmFailure? failure)
  {
    var handlePtr = RawHandle;
    if (IsValid)
    {
      failure = null;
      return true;
    }

    var rawErrno = (int)NativeMethods.alpm_errno(handlePtr);
    GC.KeepAlive(this);
    failure = NativeCall.Failure(rawErrno, "validate database");
    return false;
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
    if (!TryValidate(out var failure))
    {
      throw failure.ToException();
    }
  }
  /// <summary>
  /// Throws <see cref="AlpmLifetimeException"/> when this database was released; the guard every
  /// public accessor runs before dereferencing <c>backingStruct</c>.
  /// </summary>
  private void ThrowIfInvalidated() => _stamp.ThrowIfStale();
}
