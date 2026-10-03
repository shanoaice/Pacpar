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
  // Registry tracking file-loaded packages for session-scoped cleanup
  private readonly List<nint> _loadedPackages = [];
  private readonly Lock _loadedPackagesLock = new();

  private readonly Lifetime _lifetime;
  private Lifetime? _localDatabase;

  private int _disposeStarted;
  private int _disposedFlag;

  /// <summary>
  /// Initializes a new instance of the <see cref="Alpm"/> library handle.
  /// </summary>
  /// <param name="root">The root directory of the installation (e.g. <c>"/"</c>).</param>
  /// <param name="dbpath">The path to the pacman database directory (e.g. <c>"/var/lib/pacman"</c>).</param>
  /// <exception cref="Exception">Thrown if libalpm fails to initialize.</exception>
  public unsafe Alpm(string root, string dbpath)
  {
    var initializeErrno = (_alpm_errno_t*)NativeMemory.Alloc(sizeof(_alpm_errno_t));
    *initializeErrno = _alpm_errno_t.ALPM_ERR_OK;

    // alpm_initialize copies root and dbpath during the call (the buffer lifetime ends with this
    // frame), and both are paths - hence the 256-byte scratch.
    Span<byte> rootScratch = stackalloc byte[256];
    Span<byte> dbpathScratch = stackalloc byte[256];
    using var rootBuf = new Utf8Buffer(root, rootScratch);
    using var dbpathBuf = new Utf8Buffer(dbpath, dbpathScratch);
    var rawHandle = NativeMethods.alpm_initialize(rootBuf.Ptr, dbpathBuf.Ptr, initializeErrno);

    if (rawHandle == null)
    {
      var exception = ErrorHandler.GetException(*initializeErrno) ?? new Exception("Failed to initialize libalpm.");
      NativeMemory.Free(initializeErrno);
      throw exception;
    }

    _handle = new SafeAlpmHandle(rawHandle);
    _lifetime = Lifetime.CreateRoot(this, "the ALPM handle");
    // The handle finalizes the session path, so it needs the domain to retire stamps from there.
    _handle.OwnDomain(_lifetime);
    Options = new AlpmOptions(_handle, _lifetime);
    BindingConfig = new AlpmBindingConfig();
    Callback = new Callback(_handle, _lifetime, BindingConfig);
    NativeMemory.Free(initializeErrno);
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
  /// The handle's callback thunks read it per invocation, so mutate it only while no native call is
  /// in flight.
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
  internal Lifetime RootLifetime => _lifetime;

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
  /// the hierarchy deliberately collapses. The generated binding enum is internal since ADR 0006.
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
  /// Loads a package archive from disk.
  /// </summary>
  /// <param name="filename">The path to the package archive file.</param>
  /// <param name="full">Whether to load all package metadata eagerly into memory.</param>
  /// <param name="level">The signature verification requirements for loading the package.</param>
  /// <returns>A <see cref="LoadedPackage"/> representing the package file.</returns>
  /// <exception cref="Exception">Thrown if libalpm fails to load or parse the package archive.</exception>
  public unsafe LoadedPackage LoadPackage(string filename, bool full, SigLevel level)
  {
    ThrowIfDisposed();
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
        // Note: alpm_pkg_load sets the handle errno on failure.
        throw NativeCall.Failure(Handle, "load package");
      }

      var rawPkg = *pkgOutPtr;
      lock (_loadedPackagesLock)
      {
        _loadedPackages.Add((nint)rawPkg);
      }

      var pkgLifetime = _lifetime.CreateChild("a loaded package");
      return new LoadedPackage(this, rawPkg, pkgLifetime);
    }
    finally
    {
      // We must free the memory we allocated for the output pointer.
      NativeMemory.Free(pkgOutPtr);
    }
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
    ThrowIfDisposed();

    if (CurrentTransaction is { } active)
    {
      if (active.GetFlags() != flags)
      {
        throw new InvalidOperationException(
          $"A transaction with flags {active.GetFlags()} is already active on this handle; dispose it " +
          $"before starting one with flags {flags}.");
      }

      return active;
    }

    var transaction = new Transaction(this, flags);
    CurrentTransaction = transaction;
    return transaction;
  }

  /// <summary>
  /// The local database of this handle.
  /// </summary>
  /// <remarks>
  /// Repeated calls share one lifetime token, so all wrappers for the local database are retired
  /// together. A successful transaction commit invalidates the current token - committing frees
  /// libalpm's in-memory package caches - and the next call issues a fresh one for the re-opened
  /// database.
  /// </remarks>
  public unsafe Database GetLocalDatabase()
  {
    ThrowIfDisposed();
    var databasePtr = NativeMethods.alpm_get_localdb(Handle);
    _localDatabase ??= _lifetime.CreateChild("the local database");
    return new Database(databasePtr, _localDatabase);
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
  /// Registers a new sync package database on this handle.
  /// </summary>
  /// <param name="treename">The name of the database repository (e.g. "core", "extra").</param>
  /// <param name="level">The signature verification requirements for this database.</param>
  /// <returns>The newly registered <see cref="Database"/>.</returns>
  public unsafe Database RegisterSyncDatabase(string treename, SigLevel level)
  {
    ThrowIfDisposed();
    Span<byte> scratch = stackalloc byte[64];
    using var treeNameBuf = new Utf8Buffer(treename, scratch);
    var database = NativeMethods.alpm_register_syncdb(Handle, treeNameBuf.Ptr, (int)level);
    if (database is null)
    {
      // The null pointer is the failure signal; the errno read is the next thing that happens.
      throw NativeCall.Failure(Handle, "register sync database");
    }
    var token = _lifetime.GetLifetimeTokenForHandle(database, $"the sync database {treename}");
    return new Database(database, token);
  }

  /// <summary>
  /// Unregisters every sync database from this handle.
  /// </summary>
  public void UnregisterAllSyncDatabases()
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_unregister_all_syncdbs(Handle);
    if (err != 0) throw NativeCall.Failure(Handle, "unregister all sync databases");

    _lifetime.InvalidateHandles("Alpm.UnregisterAllSyncDatabases()");
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
    _localDatabase?.Invalidate(reason);
    _localDatabase = null;
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

    // Free any undisposed file-loaded packages from this session
    lock (_loadedPackagesLock)
    {
      foreach (var ptr in _loadedPackages)
      {
        NativeMethods.alpm_pkg_free((_alpm_pkg_t*)ptr);
      }
      _loadedPackages.Clear();
    }

    // The handle is gone: retire the whole token tree in one step.
    _lifetime.Invalidate("Alpm.Dispose()");

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
