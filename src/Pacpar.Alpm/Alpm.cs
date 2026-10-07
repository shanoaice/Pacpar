using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
/// A live session with libalpm: the root object that owns the native handle and is the entry point
/// for databases and transactions.
/// </summary>
/// <remarks>
/// libalpm is not thread-safe, so a session, the databases and transactions that hang off it, and
/// every view they issue belong to one thread at a time. This is the one place the contract is
/// stated; the rest of the library assumes it. Concurrent work belongs on other threads that hand
/// results back to the session's thread, and anything that must cross a thread boundary is copied
/// out with <see cref="PackageBase.ToSnapshot"/> first. The two places where the thread model is
/// spelled out again are where libalpm crosses back into consumer code: <see cref="Callback"/>
/// handlers and the questions raised through <see cref="Callback.QuestionHandler"/>.
/// </remarks>
public class Alpm : IDisposable
{
  // opaque handle to libalpm wrapped in a SafeHandle
  private readonly SafeAlpmHandle _handle;
  // Registry tracking file-loaded packages for session-scoped cleanup. The lock is defensive rather
  // than a promise: this list belongs to the session's thread like everything else (see the class
  // remarks), and under concurrent use libalpm's own state would break first. It is cheap - the
  // sweep and the load/unload paths, never a per-element read - so it stays. The lifetime domains
  // take the other route and are deliberately unsynchronized; see the registry note on Lifetime.
  private readonly List<nint> _loadedPackages = [];
  private readonly Lock _loadedPackagesLock = new();

  private readonly RootLifetime _lifetime;
  private ChildLifetime? _localDatabaseLifetime;

  private int _disposeStarted;
  private int _disposedFlag;

  private Alpm(SafeAlpmHandle handle)
  {
    _handle = handle;
    _lifetime = RootLifetime.Create(this, "the ALPM handle");
    _handle.OwnDomain(_lifetime);
    Options = new AlpmOptions(_handle, _lifetime);
    BindingConfig = new AlpmBindingConfig();
    Callback = new Callback(_handle, _lifetime, BindingConfig);
  }

  private static SafeAlpmHandle InitializeOrThrow(string root, string dbpath)
  {
    if (!TryInitializeHandle(root, dbpath, out var handle, out var failure))
    {
      throw failure.ToException("Failed to initialize libalpm.");
    }
    return handle;
  }

  private static unsafe bool TryInitializeHandle(string root, string dbpath,
    [NotNullWhen(true)] out SafeAlpmHandle? handle,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ArgumentNullException.ThrowIfNull(root);
    ArgumentNullException.ThrowIfNull(dbpath);

    var initializeErrno = (_alpm_errno_t*)NativeMemory.Alloc(sizeof(_alpm_errno_t));
    *initializeErrno = _alpm_errno_t.ALPM_ERR_OK;

    _alpm_handle_t* rawHandle;
    try
    {
      Span<byte> rootScratch = stackalloc byte[256];
      Span<byte> dbpathScratch = stackalloc byte[256];
      using var rootBuf = new Utf8Buffer(root, rootScratch);
      using var dbpathBuf = new Utf8Buffer(dbpath, dbpathScratch);
      rawHandle = NativeMethods.alpm_initialize(rootBuf.Ptr, dbpathBuf.Ptr, initializeErrno);
    }
    catch
    {
      NativeMemory.Free(initializeErrno);
      throw;
    }

    if (rawHandle == null)
    {
      var rawErrno = (int)*initializeErrno;
      NativeMemory.Free(initializeErrno);
      handle = null;
      failure = NativeCall.Failure(rawErrno, "initialize libalpm");
      return false;
    }

    NativeMemory.Free(initializeErrno);
    handle = new SafeAlpmHandle(rawHandle);
    failure = null;
    return true;
  }

  /// <summary>
  /// Gets the version of libalpm.
  /// </summary>
  public static unsafe string Version => NativeString.FromNative((nint)NativeMethods.alpm_version())!;

  /// <summary>
  /// Gets the compile-time capabilities of libalpm.
  /// </summary>
  public static Capability Capabilities => (Capability)(uint)NativeMethods.alpm_capabilities();

  /// <summary>
  /// The failure-returning seam for creating an <see cref="Alpm"/> session.
  /// </summary>
  /// <param name="root">The root directory of the installation (e.g. <c>"/"</c>).</param>
  /// <param name="dbpath">The path to the pacman database directory (e.g. <c>"/var/lib/pacman"</c>).</param>
  /// <param name="session">The newly created session, or <c>null</c> when initialization failed.</param>
  /// <param name="failure">The failure, or <c>null</c> when initialization succeeded.</param>
  /// <returns><c>true</c> if initialization succeeded; otherwise <c>false</c>.</returns>
  internal static bool TryCreate(string root, string dbpath,
    [NotNullWhen(true)] out Alpm? session,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    if (!TryInitializeHandle(root, dbpath, out var handle, out failure))
    {
      session = null;
      return false;
    }

    session = new Alpm(handle);
    return true;
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="Alpm"/> library handle.
  /// </summary>
  /// <param name="root">The root directory of the installation (e.g. <c>"/"</c>).</param>
  /// <param name="dbpath">The path to the pacman database directory (e.g. <c>"/var/lib/pacman"</c>).</param>
  /// <exception cref="Exception">Thrown if libalpm fails to initialize.</exception>
  public Alpm(string root, string dbpath) : this(InitializeOrThrow(root, dbpath))
  {
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(Disposed, this);
  }

  /// <summary>
  /// Exposes properties to set libalpm options.
  /// </summary>
  public AlpmOptions Options { get; }

  /// <summary>
  /// Exposes properties to configure libalpm callbacks on demand.
  /// </summary>
  public Callback Callback { get; }

  /// <summary>
  /// Exposes the wrapper's per-handle binding policy: what to copy out of libalpm when a callback
  /// payload is materialized.
  /// </summary>
  /// <remarks>
  /// libalpm's own options live in <see cref="Options"/>; this object only carries choices this
  /// wrapper makes on the consumer's behalf, such as <see cref="AlpmBindingConfig.QuestionPayloadIncludeFiles"/>.
  /// The value is read on each callback, so mutate it only while no native call is in flight.
  /// </remarks>
  public AlpmBindingConfig BindingConfig { get; }

  /// <summary>
  /// The transaction currently initialized on this handle, or <c>null</c> when there is none.
  /// </summary>
  /// <remarks>
  /// libalpm allows one transaction per handle at a time, and <see cref="BeginTransaction"/> hands
  /// this one back instead of initializing a second (it refuses the join when the requested flags
  /// differ). The property is cleared when the transaction is disposed.
  /// <para>
  /// <see cref="Dispose()"/> releases this transaction before releasing the handle: an initialized
  /// transaction holds the database lock, and <c>alpm_release</c> refuses to run while one exists
  /// (<c>ALPM_ERR_TRANS_NOT_NULL</c>, freeing nothing), which would leak the handle and
  /// <c>db.lck</c>.
  /// </para>
  /// </remarks>
  public Transaction? CurrentTransaction { get; internal set; }

  /// <summary>
  /// Whether the handle is fully released. Published with a volatile write at the end of teardown
  /// and read with <c>Volatile.Read</c>, so a concurrent caller never sees a half-disposed handle
  /// as usable.
  /// </summary>
  internal bool Disposed => Volatile.Read(ref _disposedFlag) != 0;

  /// <summary>
  /// The root of this handle's lifetime token tree. Transactions, the local-database token and the
  /// sync-database handle registry all hang off it, and a successful <see cref="Dispose()"/>
  /// retires every wrapper and view issued from this handle in one step.
  /// </summary>
  internal RootLifetime RootLifetime => _lifetime;

  /// <summary>
  /// The safe handle that owns this instance's libalpm context.
  /// </summary>
  /// <remarks>
  /// Handing this to the <c>[LibraryImport]</c> overloads in <see cref="NativeMethods"/> routes the
  /// call through the runtime's SafeHandle marshaller, which takes a refcount on the handle for the
  /// whole duration of the native call and releases it afterwards. That is what keeps
  /// <c>alpm_release</c> from running underneath an in-flight call, so it replaces both the
  /// per-call-site <c>GC.KeepAlive</c> sprinkling and the former public raw-handle escape hatch:
  /// nothing outside this assembly can reach a bare <c>_alpm_handle_t*</c> any more.
  /// </remarks>
  internal SafeAlpmHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return _handle;
    }
  }

  /// <summary>
  /// The handle's current errno, as reported by libalpm.
  /// </summary>
  /// <remarks>
  /// Read from the handle itself (<c>alpm_errno(handle)</c>). The out-parameter of
  /// <c>alpm_initialize</c> is a different value: libalpm writes it only when initialization fails,
  /// so using it here reported <c>ALPM_ERR_OK</c> for the entire lifetime of a healthy handle.
  /// </remarks>
  /// <remarks>
  /// The value is libalpm's own numbering; consult <c>alpm.h</c>. It carries no stability promise of
  /// its own - match on the exception hierarchy, and use this only to distinguish or report a case
  /// the hierarchy deliberately collapses.
  /// </remarks>
  public int Errno
  {
    get
    {
      ThrowIfDisposed();
      // No keep-alive: the SafeHandle marshaller refcounts the handle across the call, so
      // alpm_release cannot run underneath it even when this Alpm is already unreachable.
      return (int)NativeMethods.alpm_errno(Handle);
    }
  }

  /// <summary>
  /// Gets the textual description of the last error reported by libalpm on this handle.
  /// </summary>
  /// <returns>A string describing the current error, or <c>null</c> if none.</returns>
  public unsafe string? GetCurrentErrorString()
  {
    ThrowIfDisposed();
    var str = NativeString.FromNative((nint)NativeMethods.alpm_strerror((_alpm_errno_t)Errno));
    GC.KeepAlive(this);
    return str;
  }

  /// <summary>
  /// Gets the current error reported by libalpm on this handle as an <see cref="Exception"/>, or <c>null</c> if there is no error.
  /// </summary>
  /// <returns>An exception representing the current error, or <c>null</c> if <see cref="Errno"/> is OK.</returns>
  public Exception? GetCurrentError()
  {
    ThrowIfDisposed();
    return ErrorHandler.GetException(Errno);
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
    // This is a pointer to a pointer, where libalpm will write the package handle.
    var pkgOutPtr = (_alpm_pkg_t**)NativeMemory.Alloc((nuint)sizeof(nint));
    try
    {
      // The SafeHandle marshaller refcounts the handle for the whole call, so the failure path
      // below cannot leak a refcount and no keep-alive is needed here.
      var err = NativeMethods.alpm_pkg_load(Handle, filenameBuf.Ptr, full ? 1 : 0, (int)level, pkgOutPtr);
      if (err != 0)
      {
        package = null;
        failure = NativeCall.Failure(Handle, "load package");
        return false;
      }

      var rawPkg = *pkgOutPtr;
      lock (_loadedPackagesLock)
      {
        _loadedPackages.Add((nint)rawPkg);
      }

      var pkgLifetime = _lifetime.CreateChild("a loaded package");
      package = new LoadedPackage(this, rawPkg, pkgLifetime);
      failure = null;
      return true;
    }
    finally
    {
      // We must free the memory we allocated for the output pointer.
      NativeMemory.Free(pkgOutPtr);
    }
  }

  /// <summary>
  /// Loads a package archive from disk.
  /// </summary>
  /// <param name="filename">The path to the package archive file.</param>
  /// <param name="full">Whether to load all package metadata eagerly into memory.</param>
  /// <param name="level">The signature verification requirements for loading the package.</param>
  /// <returns>A <see cref="LoadedPackage"/> representing the package file.</returns>
  /// <exception cref="Exception">Thrown if libalpm fails to load or parse the package archive.</exception>
  public unsafe LoadedPackage LoadPackage(string filename, bool full, SigLevel level)
  {
    if (!TryLoadPackage(filename, full, level, out var package, out var failure))
    {
      throw failure.ToException();
    }
    return package;
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
      if (active.GetFlags() != flags)
      {
        throw new InvalidOperationException(
          $"A transaction with flags {active.GetFlags()} is already active on this handle; dispose it " +
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
  /// Returns the transaction this handle has initialized, beginning one with <paramref name="flags"/>
  /// when there is none.
  /// </summary>
  /// <param name="flags">
  /// The transaction's flags. The default, <c>0</c>, is libalpm's fully-checked mode - see
  /// <see cref="TransactionFlags"/> for what each deviation from it means.
  /// </param>
  /// <returns>The new transaction, or the active one this call joined.</returns>
  /// <remarks>
  /// libalpm initializes at most one transaction per handle, so an active transaction is joined
  /// instead of a second native one being refused.
  /// </remarks>
  /// <exception cref="InvalidOperationException">
  /// A transaction is already active and was initialized with different flags. The flags decide
  /// whether the transaction locks the database, writes to the filesystem and runs hooks, so a
  /// request for one mode must not be answered with a transaction configured for another.
  /// </exception>
  public Transaction BeginTransaction(TransactionFlags flags = default)
  {
    if (!TryBeginTransaction(flags, out var transaction, out var failure))
    {
      throw failure.ToException();
    }
    return transaction;
  }

  /// <summary>
  /// The local database of this handle.
  /// </summary>
  /// <remarks>
  /// Repeated calls return wrappers that are retired together. A successful transaction commit
  /// retires the ones issued before it - committing frees libalpm's in-memory package caches - and
  /// the next call returns a fresh wrapper for the re-opened database.
  /// </remarks>
  public unsafe Database GetLocalDatabase()
  {
    ThrowIfDisposed();
    var databasePtr = NativeMethods.alpm_get_localdb(Handle);
    if (databasePtr is null)
    {
      throw new InvalidOperationException("The local database is not available on this handle.");
    }
    _localDatabaseLifetime ??= _lifetime.CreateChild("the local database");
    return new Database(databasePtr, _localDatabaseLifetime, isLocal: true);
  }
  /// <summary>
  /// Gets the list of registered sync package databases.
  /// </summary>
  /// <returns>A read-only <see cref="AlpmList{Database}"/> of sync databases.</returns>
  public unsafe AlpmList<Database> GetSyncDatabases()
  {
    ThrowIfDisposed();
    var syncDatabases = NativeMethods.alpm_get_syncdbs(Handle);
    return AlpmList<Database>.Borrow(syncDatabases, &Database.Factory, _lifetime);
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
  /// Registers a new sync package database on this handle.
  /// </summary>
  /// <param name="treename">The name of the database repository (e.g. "core", "extra").</param>
  /// <param name="level">The signature verification requirements for this database.</param>
  /// <returns>The newly registered <see cref="Database"/>.</returns>
  /// <remarks>
  /// The registration appends a node to the handle's sync-database list. It never frees an existing
  /// node, so a list view taken before this call stays safe to walk - but it cannot see the new
  /// database either, so take a fresh <see cref="GetSyncDatabases"/> afterwards.
  /// </remarks>
  public unsafe Database RegisterSyncDatabase(string treename, SigLevel level)
  {
    if (!TryRegisterSyncDatabase(treename, level, out var database, out var failure))
    {
      throw failure.ToException();
    }
    return database;
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

    _lifetime.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");
    failure = null;
    return true;
  }

  /// <summary>
  /// Unregisters every sync database from this handle.
  /// </summary>
  public void UnregisterAllSyncDatabases()
  {
    if (!TryUnregisterAllSyncDatabases(out var failure))
    {
      throw failure.ToException();
    }
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
  /// Updates the specified synchronization databases from their configured download servers.
  /// </summary>
  /// <param name="databases">The databases to update.</param>
  /// <param name="force">If <c>true</c>, forces the download even if local copies are current.</param>
  /// <returns><c>true</c> if any database was updated; <c>false</c> if all were already up to date.</returns>
  public bool UpdateDatabases(IEnumerable<Database> databases, bool force = false)
  {
    ThrowIfDisposed();
    if (!TryUpdateDatabases(databases, force, out var updated, out var failure))
    {
      throw failure.ToException();
    }
    return updated;
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
  /// Releases the database lock this session holds.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The lock is not owned by a transaction but by the handle, and libalpm represents it as a pair:
  /// an open file descriptor on the lock file, and the file itself, created with
  /// <c>open(O_CREAT | O_EXCL)</c> — there is no <c>flock</c>, so the file's existence <i>is</i> the
  /// lock. This method drops both halves in the order libalpm requires: it closes the descriptor
  /// first, so the handle stops believing it is locked, and unlinks afterwards. Removing the file
  /// with <see cref="System.IO.File"/> instead leaves the descriptor open, and libalpm then answers
  /// every later lock attempt with "already locked" — a session that can no longer take the lock it
  /// thinks it holds, while other sessions are free to create their own.
  /// </para>
  /// <para>
  /// Releasing is meaningful only while this session holds the lock, which is only ever true between
  /// <see cref="BeginTransaction"/> and the transaction's release, or inside
  /// <see cref="UpdateDatabases"/>. With nothing locked, libalpm returns success without doing
  /// anything, and so does this method.
  /// </para>
  /// <para>
  /// What this deliberately does <b>not</b> do: remove a lock file some other session — or a crashed
  /// one — left behind. A lock left by a dead process looks exactly like a lock held by a live one
  /// (an empty, mode-0000 file carrying no holder information), so the two cannot be told apart by
  /// looking at it; the decision belongs to whoever knows no other instance is running. Delete the
  /// file with <see cref="System.IO.File"/> once you have decided that, not through this method —
  /// letting every caller of <see cref="Unlock"/> delete whatever they find turns a no-op into a
  /// silent unlock of someone else's session.
  /// </para>
  /// </remarks>
  public void Unlock()
  {
    ThrowIfDisposed();
    if (!TryUnlock(out var failure))
    {
      throw failure.ToException();
    }
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
  /// Writes an audit record to the log file.
  /// </summary>
  public void LogAction(string prefix, string message)
  {
    ThrowIfDisposed();
    if (!TryLogAction(prefix, message, out var failure))
    {
      throw failure.ToException();
    }
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
  /// Fetches packages from the specified URLs.
  /// </summary>
  public IReadOnlyList<string> FetchPackageUrls(IEnumerable<string> urls)
  {
    ThrowIfDisposed();
    if (!TryFetchPackageUrls(urls, out var fetchedUrls, out var failure))
    {
      throw failure.ToException();
    }

    return fetchedUrls;
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
  /// Checks a list of packages for missing dependencies.
  /// </summary>
  public IReadOnlyList<DepMissing> CheckDependencies(
    IEnumerable<PackageView> packages,
    IEnumerable<PackageView>? remove = null,
    IEnumerable<PackageView>? upgrade = null,
    bool reverseDependencies = false)
  {
    ThrowIfDisposed();
    if (!TryCheckDependencies(packages, remove, upgrade, reverseDependencies, out var missing, out var failure))
    {
      throw failure.ToException();
    }
    return missing;
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
  /// Checks a list of packages for conflicts.
  /// </summary>
  public IReadOnlyList<Conflict> CheckConflicts(IEnumerable<PackageView> packages)
  {
    ThrowIfDisposed();
    if (!TryCheckConflicts(packages, out var conflicts, out var failure))
    {
      throw failure.ToException();
    }
    return conflicts;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="FindSatisfier"/>.
  /// </summary>
  internal unsafe bool TryFindSatisfier(
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
      // database is never registered there (see Alpm.GetLocalDatabase), and a registry lookup would
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

  /// <summary>
  /// Finds a package satisfying the specified dependency across the given databases.
  /// </summary>
  public PackageView? FindSatisfier(IEnumerable<Database> databases, string dependency)
  {
    ThrowIfDisposed();
    if (!TryFindSatisfier(databases, dependency, out var satisfier, out var failure))
    {
      throw failure.ToException();
    }
    return satisfier;
  }

  /// <summary>
  /// Retires the current local-database token and forgets it, so the next
  /// <see cref="GetLocalDatabase"/> issues a fresh one.
  /// </summary>
  /// <remarks>
  /// Called after a successful <c>alpm_trans_commit</c>: committing rewrites the local database and
  /// frees the in-memory package caches every borrowed view points into. The native database itself
  /// stays registered - which is why the token is replaced rather than the database unregistered -
  /// but every view issued before the commit is stale. Callers that must keep package data across a
  /// commit take a <c>ToSnapshot()</c> first.
  /// </remarks>
  internal void InvalidateLocalDatabase(string reason)
  {
    _localDatabaseLifetime?.Invalidate(reason);
    _localDatabaseLifetime = null;
  }

  internal unsafe void UnregisterLoadedPackage(_alpm_pkg_t* pkg, bool freeNative)
  {
    bool wasTracked;
    lock (_loadedPackagesLock)
    {
      wasTracked = _loadedPackages.Remove((nint)pkg);
    }

    if (wasTracked && freeNative)
    {
      NativeMethods.alpm_pkg_free(pkg);
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0) return;

    CurrentTransaction?.Dispose();

    // Retire the whole tree in one step before anything is freed: the sweep below releases the
    // file-loaded packages and alpm_release below that frees the rest. Bumping the root retires
    // every child stamp - including the ones for packages we are about to alpm_pkg_free - so no
    // wrapper can pass its check while its memory is on the way out.
    _lifetime.Invalidate("Alpm.Dispose()");
    _localDatabaseLifetime = null;

    // Free any undisposed file-loaded packages from this session
    lock (_loadedPackagesLock)
    {
      foreach (var ptr in _loadedPackages)
      {
        NativeMethods.alpm_pkg_free((_alpm_pkg_t*)ptr);
      }
      _loadedPackages.Clear();
    }

    // Release the native library handle. SafeAlpmHandle.ReleaseHandle calls alpm_release.
    _handle.Dispose();

    // Only after alpm_release has succeeded and native code will no longer call back,
    // dispose the callback context GCHandle.
    if (_handle.ReleaseSucceeded)
    {
      Callback.Dispose();
    }

    Volatile.Write(ref _disposedFlag, 1);
  }
}
