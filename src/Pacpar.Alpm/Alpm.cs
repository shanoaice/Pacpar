using System.ComponentModel;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

// ReSharper disable once ClassNeverInstantiated.Global
public class Alpm : IDisposable
{
  // opaque handle to libalpm wrapped in a SafeHandle
  private readonly SafeAlpmHandle _handle;
  // Low-cardinality registry tracking file-loaded packages for Option 4 lifetime management
  private readonly List<nint> _loadedPackages = [];
  private readonly Lock _loadedPackagesLock = new();

  // The out-parameter of alpm_initialize: libalpm writes it only when initialization fails. The
  // handle's *current* error is a different thing and is read with alpm_errno(handle) - see Errno.
  private readonly unsafe _alpm_errno_t* _initializeErrno;

  // Root of the lifetime token tree for this handle: the local-database token, the sync-database
  // registry tokens and every transaction token are children of it, so one successful alpm_release
  // retires every wrapper and view ever issued from this handle. The root also anchors this Alpm
  // instance for GC purposes: any live token keeps the owner reachable in one hop.
  private readonly Lifetime _lifetime;

  // Token of the local database, deliberately kept out of the handle registry:
  // alpm_unregister_all_syncdbs never releases the local database, so its token must not ride the
  // registry sweep that retires sync databases. Reset to null by InvalidateLocalDatabase so the
  // next GetLocalDatabase issues a fresh token after a commit.
  private Lifetime? _localDatabase;

  // Dispose bookkeeping: _disposeStarted admits exactly one teardown winner;
  // _disposedFlag is the published state read through Disposed.
  private int _disposeStarted;
  private int _disposedFlag;

  public unsafe Alpm(string root, string dbpath)
  {
    _initializeErrno = (_alpm_errno_t*)NativeMemory.Alloc((nuint)sizeof(_alpm_errno_t));
    *_initializeErrno = _alpm_errno_t.ALPM_ERR_OK;

    // alpm_initialize copies root and dbpath during the call (the buffer lifetime ends with this
    // frame), and both are paths - hence the 256-byte scratch.
    Span<byte> rootScratch = stackalloc byte[256];
    Span<byte> dbpathScratch = stackalloc byte[256];
    using var rootBuf = new Utf8Buffer(root, rootScratch);
    using var dbpathBuf = new Utf8Buffer(dbpath, dbpathScratch);
    var rawHandle = NativeMethods.alpm_initialize(rootBuf.Ptr, dbpathBuf.Ptr, _initializeErrno);

    if (rawHandle == null)
    {
      throw ErrorHandler.GetException(*_initializeErrno) ?? new Exception("Failed to initialize libalpm.");
    }

    _handle = new SafeAlpmHandle(rawHandle);
    _lifetime = Lifetime.CreateRoot(this, "the ALPM handle");
    Options = new AlpmOptions(_handle, _lifetime);
    Callback = new Callback(_handle, _lifetime);
    _handle.SetContext(Callback, _initializeErrno);
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
  public unsafe _alpm_errno_t Errno
  {
    get
    {
      ThrowIfDisposed();
      // No keep-alive: the SafeHandle marshaller refcounts the handle across the call, so
      // alpm_release cannot run underneath it even when this Alpm is already unreachable.
      return NativeMethods.alpm_errno(Handle);
    }
  }

  // ReSharper disable once MemberCanBePrivate.Global
  public unsafe string? GetCurrentErrorString()
  {
    ThrowIfDisposed();
    var str = NativeString.FromNative((nint)NativeMethods.alpm_strerror(Errno));
    GC.KeepAlive(this);
    return str;
  }

  public Exception? GetCurrentError()
  {
    ThrowIfDisposed();
    return ErrorHandler.GetException(Errno);
  }

  /// <summary>
  /// The current error as an exception; never <c>null</c>.
  /// </summary>
  /// <remarks>
  /// Use this at sites that have already observed a native failure: a failing libalpm call that did
  /// not set an errno is a contract violation and must still surface, instead of becoming
  /// <c>null</c> and then a <see cref="NullReferenceException"/> at the throw site.
  /// </remarks>
  internal Exception GetRequiredCurrentError()
    => GetCurrentError() ?? new InvalidOperationException(
      "libalpm reported a failure without setting an error code.");

  /// <summary>
  /// Throws when the handle's current errno reports an error.
  /// </summary>
  /// <remarks>
  /// Used right after a native call that signals failure through the handle errno, and whose return
  /// value can also be a <c>null</c> pointer that is legitimate when there is no error.
  /// </remarks>
  private unsafe void ThrowIfCurrentError()
  {
    var errno = Errno;
    if (errno != _alpm_errno_t.ALPM_ERR_OK) throw ErrorHandler.ToException(errno);
  }

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
        throw GetRequiredCurrentError();
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
    ThrowIfCurrentError();
    _localDatabase ??= _lifetime.CreateChild("the local database");
    return new Database(databasePtr, _localDatabase);
  }

  public unsafe AlpmList<Database> GetSyncDatabases()
  {
    ThrowIfDisposed();
    var syncDatabases = NativeMethods.alpm_get_syncdbs(Handle);
    ThrowIfCurrentError();
    // The list itself belongs to the handle, so it is guarded by the root token; each element
    // resolves its own per-database child token through the handle registry in Database.Factory.
    return AlpmList<Database>.Borrow(syncDatabases, &Database.Factory, _lifetime);
  }

  public unsafe Database RegisterSyncDatabase(string treename, SigLevel level)
  {
    ThrowIfDisposed();
    // Database names are short, hence the 64-byte scratch; alpm_register_syncdb copies the name.
    Span<byte> scratch = stackalloc byte[64];
    using var treeNameBuf = new Utf8Buffer(treename, scratch);
    var database = NativeMethods.alpm_register_syncdb(Handle, treeNameBuf.Ptr, (int)level);
    ThrowIfCurrentError();
    // Registry lookup by native pointer: GetSyncDatabases' element factory resolves to this same
    // token, and a tree re-registered at a recycled address receives a fresh one (Unregister
    // dropped the dead entry).
    var token = _lifetime.GetLifetimeTokenForHandle(database, $"the sync database {treename}");
    return new Database(database, token);
  }

  /// <summary>
  /// Unregisters every sync database from this handle.
  /// </summary>
  /// <remarks>
  /// On success the lifetime tokens of all registered sync databases are invalidated and the handle
  /// registry is emptied: wrappers and views issued from those databases throw
  /// <see cref="AlpmLifetimeException"/>, and a future registration at a recycled address receives
  /// a fresh token. The local database is not affected - its token is deliberately not in the
  /// registry.
  /// </remarks>
  public unsafe void UnregisterAllSyncDatabases()
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_unregister_all_syncdbs(Handle);
    if (err != 0) throw GetRequiredCurrentError();

    // Strictly after the native release succeeded (§4.1 order): one sweep retires every registered
    // token and clears the registry.
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

    // Option 4: Sweep and free any undisposed file-loaded packages from this session
    lock (_loadedPackagesLock)
    {
      foreach (var ptr in _loadedPackages)
      {
        NativeMethods.alpm_pkg_free((_alpm_pkg_t*)ptr);
      }
      _loadedPackages.Clear();
    }

    // The handle is gone: retire the whole token tree in one step.
    _lifetime.Invalidate("Alpm.Dispose()", fromFinalizer: false);

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
