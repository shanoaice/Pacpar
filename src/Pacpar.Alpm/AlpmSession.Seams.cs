using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

public partial class AlpmSession
{
  /// <summary>
  /// The failure-returning seam for creating an <see cref="AlpmSession"/> session.
  /// </summary>
  /// <param name="root">The root directory of the installation (e.g. <c>"/"</c>).</param>
  /// <param name="dbpath">The path to the pacman database directory (e.g. <c>"/var/lib/pacman"</c>).</param>
  /// <param name="session">The newly created session, or <c>null</c> when initialization failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when initialization succeeded.</param>
  /// <returns><c>true</c> if initialization succeeded; otherwise <c>false</c>.</returns>
  internal static bool TryCreate(string root, string dbpath,
    [NotNullWhen(true)] out AlpmSession? session,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    if (!TryInitializeHandle(root, dbpath, out var handle, out failure))
    {
      session = null;
      return false;
    }

    session = new AlpmSession(handle);
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="LoadPackage"/>.
  /// </summary>
  internal unsafe bool TryLoadPackage(string filename, bool full, SigLevel level,
    [NotNullWhen(true)] out LoadedPackage? package,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(filename);

    // Paths are long, hence the 256-byte scratch; alpm_pkg_load only reads the string.
    Span<byte> scratch = stackalloc byte[256];
    using var filenameBuf = new Utf8Buffer(filename, scratch);
    // Pass the address of a stack local where libalpm will write the package handle.
    _alpm_pkg_t* rawPkg = null;
    // The SafeHandle marshaller refcounts the handle for the whole call, so the failure path
    // below cannot leak a refcount and no keep-alive is needed here.
    var err = NativeMethods.alpm_pkg_load(Handle, filenameBuf.Ptr, full ? 1 : 0, (int)level, &rawPkg);
    if (err != 0)
    {
      package = null;
      failure = NativeCall.Failure(Handle, "load package");
      return false;
    }

    lock (_loadedPackagesLock)
    {
      _loadedPackages.Add((nint)rawPkg);
    }

    var pkgLifetime = _lifetime.CreateChild("a loaded package");
    package = new LoadedPackage(this, rawPkg, pkgLifetime);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="BeginTransaction"/>.
  /// </summary>
  internal bool TryBeginTransaction(TransactionFlags flags,
    [NotNullWhen(true)] out Transaction? transaction,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    if (CurrentTransaction is { } active)
    {
      if (active.Flags != flags)
      {
        throw new InvalidOperationException(
          $"A transaction with flags {active.Flags} is already active on this handle; dispose it " +
          $"before starting one with flags {flags}.");
      }

      transaction = active;
      failure = null;
      return true;
    }

    if (!Transaction.TryCreate(this, flags, out transaction, out failure))
    {
      return false;
    }

    CurrentTransaction = transaction;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="RegisterSyncDatabase"/>.
  /// </summary>
  internal unsafe bool TryRegisterSyncDatabase(string treename, SigLevel level,
    [NotNullWhen(true)] out Database? database,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(treename);

    Span<byte> scratch = stackalloc byte[64];
    using var treeNameBuf = new Utf8Buffer(treename, scratch);
    var dbPtr = NativeMethods.alpm_register_syncdb(Handle, treeNameBuf.Ptr, (int)level);
    if (dbPtr is null)
    {
      database = null;
      failure = NativeCall.Failure(Handle, "register sync database");
      return false;
    }

    var token = _lifetime.GetLifetimeTokenForHandle(dbPtr, $"the sync database {treename}");
    database = new Database(dbPtr, token, () =>
    {
      _lifetime.ForgetHandle(dbPtr);
      _lifetime.Invalidate("Database.Unregister()");
    }, isLocal: false);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="UnregisterAllSyncDatabases"/>.
  /// </summary>
  internal bool TryUnregisterAllSyncDatabases([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_unregister_all_syncdbs(Handle);
    if (err != 0)
    {
      failure = NativeCall.Failure(Handle, "unregister all sync databases");
      return false;
    }

    _lifetime.InvalidateHandles("AlpmSession.UnregisterAllSyncDatabases()");
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="UpdateDatabases"/>.
  /// </summary>
  internal unsafe bool TryUpdateDatabases(
    IEnumerable<Database> databases,
    bool force,
    out bool updated,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(databases);

    var dbList = databases.ToList();
    foreach (var db in dbList)
    {
      db.InvalidateViews("Database.Update");
    }

    _alpm_list_t* nativeList = null;
    try
    {
      nativeList = AlpmNativeList.BuildPointerList(dbList.Select(d => (nint)d.ValidatedPtr));
      var ret = NativeMethods.alpm_db_update(Handle, nativeList, force ? 1 : 0);
      if (ret < 0)
      {
        updated = false;
        failure = NativeCall.Failure(Handle, "update databases");
        return false;
      }

      updated = ret == 0;
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(nativeList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Unlock"/>.
  /// </summary>
  /// <remarks>
  /// Thin forwarding: libalpm owns the lock and releases it here, when this session is the one that
  /// holds it. Nothing here touches a lock this session did not take. See the
  /// <see cref="Unlock"/> remarks for what that means and what it does not cover.
  /// </remarks>
  internal bool TryUnlock([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_unlock(Handle);
    if (err != 0)
    {
      failure = NativeCall.Failure(Handle, "unlock database");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="LogAction"/>.
  /// </summary>
  internal unsafe bool TryLogAction(string prefix, string message, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(prefix);
    ArgumentNullException.ThrowIfNull(message);

    var messageWithNewline = message.EndsWith('\n') ? message : message + "\n";
    var safeMessage = messageWithNewline.Replace("%", "%%");
    Span<byte> prefixScratch = stackalloc byte[64];
    Span<byte> messageScratch = stackalloc byte[256];
    using var prefixBuf = new Utf8Buffer(prefix, prefixScratch);
    using var messageBuf = new Utf8Buffer(safeMessage, messageScratch);

    var err = NativeMethods.alpm_logaction(Handle, prefixBuf.Ptr, messageBuf.Ptr);
    if (err != 0)
    {
      failure = NativeCall.Failure(Handle, "log action");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="FetchPackageUrls"/>.
  /// </summary>
  internal unsafe bool TryFetchPackageUrls(IEnumerable<string> urls,
    [NotNullWhen(true)] out IReadOnlyList<string>? fetchedUrls,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(urls);

    var nativeUrls = AlpmNativeList.BuildStringList(urls, out var buffers);
    _alpm_list_t* fetchedList = null;
    try
    {
      var err = NativeMethods.alpm_fetch_pkgurl(Handle, nativeUrls, &fetchedList);
      if (err != 0)
      {
        if (fetchedList != null)
        {
          AlpmNativeList.Free(fetchedList, &MemoryManagement.CFreeExtern);
        }

        fetchedUrls = null;
        failure = NativeCall.Failure(Handle, "fetch package URLs");
        return false;
      }

      fetchedUrls = AlpmStringList.TakeOwned(fetchedList, &MemoryManagement.CFreeExtern);
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.FreeStringListBuffers(buffers, nativeUrls);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="CheckDependencies"/>.
  /// </summary>
  internal unsafe bool TryCheckDependencies(
    IEnumerable<PackageView> packages,
    IEnumerable<PackageView>? remove,
    IEnumerable<PackageView>? upgrade,
    bool reverseDependencies,
    [NotNullWhen(true)] out IReadOnlyList<DepMissing>? missing,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(packages);

    _alpm_list_t* pkgList = null;
    _alpm_list_t* remList = null;
    _alpm_list_t* upgList = null;
    try
    {
      pkgList = AlpmNativeList.BuildPointerList(packages.Select(p => (nint)p.BackingStruct));
      if (remove is not null)
      {
        remList = AlpmNativeList.BuildPointerList(remove.Select(p => (nint)p.BackingStruct));
      }

      if (upgrade is not null)
      {
        upgList = AlpmNativeList.BuildPointerList(upgrade.Select(p => (nint)p.BackingStruct));
      }

      var result = NativeMethods.alpm_checkdeps(Handle, pkgList, remList, upgList, reverseDependencies ? 1 : 0);
      missing = result != null
        ? AlpmOwnedList<DepMissing>.Take(result, &DepMissing.Factory, &MemoryManagement.DepMissingFreeExtern)
        : Array.Empty<DepMissing>();
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(pkgList);
      AlpmNativeList.Free(remList);
      AlpmNativeList.Free(upgList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="CheckConflicts"/>.
  /// </summary>
  internal unsafe bool TryCheckConflicts(
    IEnumerable<PackageView> packages,
    [NotNullWhen(true)] out IReadOnlyList<Conflict>? conflicts,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(packages);

    _alpm_list_t* pkgList = null;
    try
    {
      pkgList = AlpmNativeList.BuildPointerList(packages.Select(p => (nint)p.BackingStruct));
      var result = NativeMethods.alpm_checkconflicts(Handle, pkgList);
      conflicts = result != null
        ? AlpmOwnedList<Conflict>.Take(result, &Conflict.Factory, &MemoryManagement.ConflictFreeExtern)
        : Array.Empty<Conflict>();
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(pkgList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="ResolveDependency"/>.
  /// </summary>
  /// <remarks>
  /// Every path here leaves <paramref name="failure"/> null, and that is not an oversight in this
  /// wrapper: <c>alpm_find_dbs_satisfier</c> signals a miss and a failure with the same null pointer
  /// and the seam reads no errno, so the two cannot be told apart today. The seam is kept because
  /// ADR 0013 gives every handle-touching operation one. Do not add a failure branch here without
  /// first establishing, against the native source, that the call actually reports one.
  /// </remarks>
  internal unsafe bool TryResolveDependency(
    IEnumerable<Database> databases,
    string dependency,
    out PackageView? satisfier,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(databases);
    ArgumentNullException.ThrowIfNull(dependency);

    var dbList = databases.ToList();
    _alpm_list_t* nativeList = null;
    Span<byte> scratch = stackalloc byte[64];
    using var depBuf = new Utf8Buffer(dependency, scratch);
    try
    {
      nativeList = AlpmNativeList.BuildPointerList(dbList.Select(d => (nint)d.ValidatedPtr));
      var pkgPtr = NativeMethods.alpm_find_dbs_satisfier(Handle, nativeList, depBuf.Ptr);
      if (pkgPtr == null)
      {
        satisfier = null;
        failure = null;
        return true;
      }

      var dbPtr = NativeMethods.alpm_pkg_get_db(pkgPtr);
      // Resolved among the wrappers the caller handed in, not through the handle registry: the local
      // database is never registered there (see AlpmSession.GetLocalDatabase), and a registry lookup would
      // therefore mint a second domain for it. See Database.ResolveLifetime for what rides on this.
      var dbLifetime = Database.ResolveLifetime(dbList, dbPtr);
      satisfier = new PackageView(pkgPtr, dbLifetime);
      failure = null;
      return true;
    }
    finally
    {
      AlpmNativeList.Free(nativeList);
    }
  }
}
