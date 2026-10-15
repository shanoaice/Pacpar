using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
/// A package database: the local database or a registered sync tree.
/// </summary>
/// <remarks>
/// A borrowed handle: libalpm owns the database, this wrapper only reads through it. Every accessor
/// checks first that the database is still live, so using one after <see cref="Unregister"/>, after
/// <see cref="AlpmSession.UnregisterAllSyncDatabases"/> or after the owning <see cref="AlpmSession"/> was released
/// throws <see cref="AlpmLifetimeException"/> instead of reading freed memory. Views handed out here
/// (packages, groups, server lists) die with it the same way.
/// </remarks>
public unsafe partial class Database
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
  /// pointer registry (sync databases) or held by a dedicated <see cref="AlpmSession"/> field (the local
  /// database, which <c>alpm_unregister_all_syncdbs</c> never releases).
  /// </summary>
  internal readonly ChildLifetime Lifetime;

  private readonly bool _isLocal;
  private readonly Action? _onUnregistered;

  /// <summary>When this wrapper was created, so <see cref="ThrowIfInvalidated"/> can detect a bump.</summary>
  private LifetimeStamp _stamp;

  /// <param name="backingStruct">The libalpm-owned database.</param>
  /// <param name="lifetime">The database's domain; all accessors and issued views guard on it.</param>
  /// <param name="onUnregistered">Optional callback invoked when the database is unregistered.</param>
  /// <param name="isLocal"><c>true</c> if this is the local database; <c>false</c> for sync databases.</param>
  internal Database(_alpm_db_t* backingStruct, ChildLifetime lifetime, Action? onUnregistered = null,
    bool isLocal = false)
  {
    ValidatedPtr = backingStruct;
    Lifetime = lifetime;
    _isLocal = isLocal;
    _onUnregistered = onUnregistered;
    _stamp = lifetime.Capture();
  }

  /// <summary>
  /// Element factory for <see cref="AlpmSession.GetSyncDatabases"/>: resolves the per-database token from
  /// the handle registry keyed by the native pointer, so two wrappers for the same database - and
  /// two enumerations of the same list - share one token and one invalidation.
  /// </summary>
  /// <remarks>
  /// The name is read only when the pointer is not registered yet: on a fresh list the database is
  /// alive and the read is safe. A list obtained <i>before</i> a registry mutation
  /// (<see cref="AlpmSession.RegisterSyncDatabase"/>, <see cref="Unregister"/>,
  /// <see cref="AlpmSession.UnregisterAllSyncDatabases"/>) can hold nodes those calls freed; re-fetch the
  /// list after mutating the registry instead of re-enumerating a stale one.
  /// </remarks>
  internal static Database Factory(void* ptr, Lifetime? lifetime)
  {
    ArgumentNullException.ThrowIfNull(lifetime);
    if (lifetime is not RootLifetime root)
    {
      throw new InvalidOperationException("Sync databases must be enumerated against a root session lifetime domain.");
    }

    var name = NativeString.FromNative((nint)NativeMethods.alpm_db_get_name((_alpm_db_t*)ptr)) ?? "(unknown)";
    return new Database((_alpm_db_t*)ptr, root.GetLifetimeTokenForHandle(ptr, $"the sync database {name}"), () =>
    {
      root.ForgetHandle(ptr);
      root.Invalidate("Database.Unregister()");
    }, isLocal: false);
  }

  /// <summary>
  /// The domain of the wrapper that already owns <paramref name="dbPtr"/>, picked out of the
  /// wrappers the caller is holding.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Use this instead of <see cref="RootLifetime.GetLifetimeTokenForHandle"/> whenever libalpm hands back
  /// a package the caller's own databases own. The registry deduplicates by native pointer, but it
  /// only covers databases that were registered through it: <see cref="AlpmSession.GetLocalDatabase"/>
  /// mints its domain directly and never files an entry, so a registry lookup with the local
  /// database's pointer cannot find one and mints a <i>second</i> domain for the same native
  /// database. Every view issued from that duplicate then survives the invalidation of the real
  /// domain - the use-after-free this hierarchy exists to prevent.
  /// </para>
  /// <para>
  /// The lookup answers from the caller's wrappers, which is both correct for every database kind
  /// and free of the side effect above.
  /// </para>
  /// </remarks>
  /// <param name="databases">The wrappers the caller passed into the call that produced the pointer.</param>
  /// <param name="dbPtr">The native database to resolve. A null pointer matches nothing.</param>
  /// <exception cref="InvalidOperationException">
  /// No wrapper owns <paramref name="dbPtr"/>. libalpm can only return packages found inside the list
  /// it was given, so this is an invariant violation rather than a result to hand out: issuing the
  /// view under a freshly minted domain would hide it, and issuing it with none would leave it
  /// unanchored.
  /// </exception>
  internal static ChildLifetime ResolveLifetime(IReadOnlyList<Database> databases, _alpm_db_t* dbPtr)
  {
    foreach (var database in databases)
    {
      if (database.ValidatedPtr == dbPtr) return database.Lifetime;
    }

    throw new InvalidOperationException(
      "libalpm returned a package owned by a database that is not among the ones passed to the call; " +
      "there is no domain to anchor the resulting view to.");
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
  /// Searches this database for packages matching the given patterns.
  /// </summary>
  public IReadOnlyList<PackageView> Search(IEnumerable<string> needles)
  {
    if (!TrySearch(needles, out var results, out var failure))
    {
      throw failure.ToException();
    }

    return results;
  }

  /// <summary>
  /// Loads this database's package cache, or returns the one libalpm has already populated.
  /// </summary>
  /// <remarks>
  /// A method, not a property, on purpose: libalpm parses the database file the first time this is
  /// asked for, which is orders of magnitude slower than reading a field. <see cref="Servers"/> is a
  /// property for the opposite reason - <c>alpm_db_get_servers</c> hands back a list libalpm already
  /// holds and does no work. Later calls answer from the populated cache and are cheap, but the
  /// shape has to price the first one, and a property that occasionally costs a full parse is a
  /// silent lie about what <c>db.PackageCache.Count</c> does.
  /// <para>
  /// A <c>null</c> native cache with an ok errno is a legitimately empty cache and yields an empty
  /// view; a non-ok errno means loading the cache failed and is thrown. That rule is sound here and
  /// only here, because <c>alpm_db_get_pkgcache</c> clears <c>pm_errno</c> in its first statement -
  /// see <see cref="LastCallFailed"/>. The view is borrowed and never frees the cache (see
  /// <see cref="AlpmList{T}"/>).
  /// </para>
  /// </remarks>
  public AlpmList<PackageView> LoadPackageCache()
  {
    if (!TryLoadPackageCache(out var cache, out var failure)) throw failure.ToException();
    return cache;
  }


  /// <summary>
  /// The servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList Servers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_servers(ValidatedPtr);
      return new AlpmStringList(servers, Lifetime);
    }
  }

  /// <summary>
  /// The cache servers configured for this database; empty when none are set.
  /// </summary>
  /// <remarks>The list is borrowed from the database and is never freed by the view.</remarks>
  public AlpmStringList CacheServers
  {
    get
    {
      var servers = NativeMethods.alpm_db_get_cache_servers(ValidatedPtr);
      return new AlpmStringList(servers, Lifetime);
    }
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
  /// Loads this database's group cache, or returns the one libalpm has already populated. See
  /// <see cref="LoadPackageCache"/> for the null/errno rule and for why this is a method.
  /// </summary>
  public AlpmList<Group> LoadGroupCache()
  {
    if (!TryLoadGroupCache(out var cache, out var failure)) throw failure.ToException();
    return cache;
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

  /// <summary>
  /// Gets or sets the allowed usage flags for this database.
  /// </summary>
  public DatabaseUsage Usage
  {
    get => TryGetUsage(out var usage, out var failure) ? usage : throw failure.ToException();
    set
    {
      if (!TrySetUsage(value, out var failure))
      {
        throw failure.ToException();
      }
    }
  }


  /// <summary>
  /// Adds a download server to this database.
  /// </summary>
  public void AddServer(string url)
  {
    if (!TryAddServer(url, out var failure))
    {
      throw failure.ToException();
    }
  }


  /// <summary>
  /// Removes a download server from this database.
  /// </summary>
  /// <returns><c>true</c> if the server was removed; <c>false</c> if it was not found.</returns>
  public bool RemoveServer(string url)
  {
    if (!TryRemoveServer(url, out var removed, out var failure))
    {
      throw failure.ToException();
    }

    return removed;
  }


  /// <summary>
  /// Sets the complete list of download servers for this database.
  /// </summary>
  public void SetServers(IEnumerable<string> servers)
  {
    if (!TrySetServers(servers, out var failure))
    {
      throw failure.ToException();
    }
  }


  /// <summary>
  /// Adds a download cache server to this database.
  /// </summary>
  public void AddCacheServer(string url)
  {
    if (!TryAddCacheServer(url, out var failure))
    {
      throw failure.ToException();
    }
  }


  /// <summary>
  /// Removes a download cache server from this database.
  /// </summary>
  /// <returns><c>true</c> if the cache server was removed; <c>false</c> if it was not found.</returns>
  public bool RemoveCacheServer(string url)
  {
    if (!TryRemoveCacheServer(url, out var removed, out var failure))
    {
      throw failure.ToException();
    }

    return removed;
  }


  /// <summary>
  /// Sets the complete list of download cache servers for this database.
  /// </summary>
  public void SetCacheServers(IEnumerable<string> servers)
  {
    if (!TrySetCacheServers(servers, out var failure))
    {
      throw failure.ToException();
    }
  }

  /// <summary>
  /// Retires all existing views issued from this database while keeping the database wrapper alive.
  /// </summary>
  internal void InvalidateViews(string reason)
  {
    Lifetime.Invalidate(reason);
    _stamp = Lifetime.Capture();
  }


  /// <summary>
  /// Updates this database from its configured download servers.
  /// </summary>
  /// <param name="force">If <c>true</c>, forces the download even if the local copy is current.</param>
  /// <returns><c>true</c> if the database was updated; <c>false</c> if it was already up to date.</returns>
  public bool Update(bool force = false)
  {
    if (!TryUpdate(force, out var updated, out var failure))
    {
      throw failure.ToException();
    }

    return updated;
  }

  /// <summary>
  /// Unregisters this database from its handle, releasing it.
  /// </summary>
  /// <remarks>
  /// On success every wrapper and view for this database starts throwing
  /// <see cref="AlpmLifetimeException"/>, and the handle's sync-database list is retired as well,
  /// because unregistering frees this database's node in it. A later registration that reuses the
  /// same address is treated as a brand-new database. A failed unregister leaves the database usable.
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
  /// validity answer only: a database that was already released still throws
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
