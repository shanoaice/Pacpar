using System.ComponentModel;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

// ReSharper disable once ClassNeverInstantiated.Global
public class Alpm : IDisposable
{
  // opaque handle to libalpm, details not exposed
  private unsafe _alpm_handle_t* _handle;

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

  // Dispose bookkeeping: _disposeStarted admits exactly one teardown winner (Dispose and the
  // finalizer can race); _disposedFlag is the published state read through Disposed.
  private int _disposeStarted;
  private int _disposedFlag;

  public unsafe Alpm(string root, string dbpath)
  {
    _initializeErrno = (_alpm_errno_t*)Marshal.AllocHGlobal(sizeof(_alpm_errno_t));
    *_initializeErrno = _alpm_errno_t.ALPM_ERR_OK;

    var rootPtr = NativeString.ToNative(root);
    var dbpathPtr = NativeString.ToNative(dbpath);
    try
    {
      _handle = NativeMethods.alpm_initialize(rootPtr, dbpathPtr, _initializeErrno);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)rootPtr);
      Marshal.FreeHGlobal((nint)dbpathPtr);
    }

    if (_handle == null)
    {
      throw ErrorHandler.GetException(*_initializeErrno) ?? new Exception("Failed to initialize libalpm.");
    }

    // Created before its consumers: Options and Callback both carry the root token, and every
    // later token (databases, transactions) is a child of it.
    _lifetime = Lifetime.CreateRoot(this, "the ALPM handle");
    Options = new AlpmOptions(_handle, _lifetime);
    Callback = new Callback(_handle, _lifetime);
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
  /// The raw handle to libalpm, as an <see cref="IntPtr"/> so it can be passed around without
  /// <c>unsafe</c>.
  /// </summary>
  /// <remarks>
  /// An escape hatch for interop this wrapper does not cover, not part of the normal API: the
  /// handle is owned by this instance, and modifying it, releasing it or handing it to another
  /// <see cref="Alpm"/> is undefined behaviour. The wrapper's own types are the supported way to
  /// reach libalpm.
  /// </remarks>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public unsafe IntPtr AsHandle()
  {
    ThrowIfDisposed();
    return (IntPtr)_handle;
  }

  internal unsafe _alpm_handle_t* Handle => _handle;

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
      return NativeMethods.alpm_errno(_handle);
    }
  }

  // ReSharper disable once MemberCanBePrivate.Global
  public unsafe string? GetCurrentErrorString()
  {
    ThrowIfDisposed();
    return NativeString.FromNative((nint)NativeMethods.alpm_strerror(Errno));
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
    var filenamePtr = NativeString.ToNative(filename);
    // This is a pointer to a pointer, where libalpm will write the package handle.
    var pkgOutPtr = (_alpm_pkg_t**)Marshal.AllocHGlobal(sizeof(nint));
    try
    {
      var err = NativeMethods.alpm_pkg_load(_handle, filenamePtr, full ? 1 : 0, (int)level, pkgOutPtr);
      if (err != 0)
      {
        // Note: alpm_pkg_load sets the handle errno on failure.
        throw GetRequiredCurrentError();
      }

      // The LoadedPackage wrapper now takes ownership of the native handle *pkgOutPtr
      return new LoadedPackage(*pkgOutPtr);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)filenamePtr);
      // We must free the memory we allocated for the output pointer.
      Marshal.FreeHGlobal((IntPtr)pkgOutPtr);
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
    var databasePtr = NativeMethods.alpm_get_localdb(_handle);
    ThrowIfCurrentError();
    _localDatabase ??= _lifetime.CreateChild("the local database");
    return new Database(databasePtr, _localDatabase);
  }

  public unsafe AlpmList<Database> GetSyncDatabases()
  {
    ThrowIfDisposed();
    var syncDatabases = NativeMethods.alpm_get_syncdbs(_handle);
    ThrowIfCurrentError();
    // The list itself belongs to the handle, so it is guarded by the root token; each element
    // resolves its own per-database child token through the handle registry in Database.Factory.
    return AlpmList<Database>.Borrow(syncDatabases, &Database.Factory, _lifetime);
  }

  public unsafe Database RegisterSyncDatabase(string treename, SigLevel level)
  {
    ThrowIfDisposed();
    var treeNameCString = NativeString.ToNative(treename);
    try
    {
      var database = NativeMethods.alpm_register_syncdb(_handle, treeNameCString, (int)level);
      ThrowIfCurrentError();
      // Registry lookup by native pointer: GetSyncDatabases' element factory resolves to this same
      // token, and a tree re-registered at a recycled address receives a fresh one (Unregister
      // dropped the dead entry).
      var token = _lifetime.GetLifetimeTokenForHandle(database, $"the sync database {treename}");
      return new Database(database, token);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)treeNameCString);
    }
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
    var err = NativeMethods.alpm_unregister_all_syncdbs(_handle);
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

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    // Exactly one teardown ever runs: Dispose() and the finalizer can race on the same instance,
    // and a second entrant would double-release the handle and double-free the errno buffer. The
    // loser returns immediately; the winner's teardown is self-contained.
    if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0) return;

    // An initialized transaction holds the database lock, and libalpm refuses to release a handle
    // while one exists: alpm_release answers ALPM_ERR_TRANS_NOT_NULL and frees nothing, which leaks
    // the handle and <dbpath>/db.lck. Releasing the transaction first drops the lock, so the
    // release below can succeed.
    CurrentTransaction?.Dispose();

    var releaseErr = NativeMethods.alpm_release(_handle);
    // A failed release leaves the handle alive, so its errno can still be read; the failure is only
    // reported at the end, once the wrapper itself is fully disposed. Not from the finalizer path,
    // where an escaping exception would terminate the process.
    var releaseFailure = disposing && releaseErr != 0 ? ErrorHandler.ToException(Errno) : null;
    _handle = (_alpm_handle_t*)IntPtr.Zero;

    // The callback context must stay alive until native code can no longer call back, and
    // alpm_release itself may still fire events, so it is released only now that alpm_release has
    // returned. A release that failed, on the other hand, leaves the handle alive - and that handle
    // still holds the context's GCHandle, which its native thunks dereference: a freed handle
    // resolves to a null target, so the next callback entered from it dereferences null in LogAgent
    // and the exception escaping that [UnmanagedCallersOnly] thunk terminates the process (measured).
    // The context therefore stays with the handle it belongs to.
    // On the success path this must also happen on the finalizer path: Callback is strongly rooted by
    // its own GCHandle, so if Alpm does not release it, nothing ever will (the Callback and every
    // object its handler delegates keep alive would leak for the life of the process).
    if (releaseErr == 0)
    {
      // The handle is gone: retire the whole token tree in one step. Every database, transaction and
      // view token chains up to this root, so from here on they all answer AlpmLifetimeException
      // instead of reading freed memory. On the finalizer path this stays a single volatile write -
      // the token registry is never enumerated from the GC thread (§4.4 invariant).
      _lifetime.Invalidate(
        disposing ? "Alpm.Dispose()" : "the owning Alpm was garbage-collected",
        fromFinalizer: !disposing);

      // Deliberately not guarded, although Dispose should not throw: this call cannot, so a guard
      // would only hide a future defect in this wrapper's own bookkeeping. Callback.Dispose is an
      // IsAllocated check around GCHandle<T>.Dispose, and that call is idempotent - a repeat call
      // is a no-op (measured, sequentially and under 8 threads racing on one handle).
      // Similarly, not guarded on finalizer path since currently it cannot throw, and if there's
      // a future defect it is alarming that it crashes directly.
      Callback.Dispose();
    }

    Marshal.FreeHGlobal((nint)_initializeErrno);
    Volatile.Write(ref _disposedFlag, 1);

    // Thrown last, after the wrapper is fully disposed, and never from the finalizer path: a failed
    // alpm_release is the only sign that the handle and its lock leaked.
    if (releaseFailure != null) throw releaseFailure;
  }

  ~Alpm()
  {
    Dispose(disposing: false);
  }
}
