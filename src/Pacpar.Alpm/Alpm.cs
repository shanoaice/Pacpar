using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

// ReSharper disable once ClassNeverInstantiated.Global
public class Alpm : IDisposable
{
  // opaque handle to libalpm, details not exposed
  private unsafe byte* _handle;

  // The out-parameter of alpm_initialize: libalpm writes it only when initialization fails. The
  // handle's *current* error is a different thing and is read with alpm_errno(handle) - see Errno.
  private readonly unsafe _alpm_errno_t* _initializeErrno;

  // ReSharper disable once RedundantDefaultMemberInitializer
  private bool _disposed = false;

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

    Options = new AlpmOptions(_handle);
    Callback = new Callback(_handle);
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <summary>
  /// Exposes methods to set libalpm options.
  /// </summary>
  public AlpmOptions Options { get; }

  /// <summary>
  /// Expose methods to set libalpm callbacks.
  /// </summary>
  public Callback Callback { get; }

  /// <summary>
  /// The IntPtr version of the handle to libalpm, allows
  ///  passing around without unsafe.
  ///  DO NOT MODIFY IT IN ANY WAYS WHEN PASSING AROUND. BAD THINGS WILL HAPPEN!
  /// </summary>
  public unsafe IntPtr Handle
  {
    get
    {
      ThrowIfDisposed();
      return (IntPtr)_handle;
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

  public unsafe Package LoadPackage(string filename, bool full, SigLevel level)
  {
    ThrowIfDisposed();
    var filenamePtr = NativeString.ToNative(filename);
    // This is a pointer to a pointer, where libalpm will write the package handle.
    var pkgOutPtr = (byte**)Marshal.AllocHGlobal(sizeof(nint));
    try
    {
      var err = NativeMethods.alpm_pkg_load(_handle, filenamePtr, full ? 1 : 0, (int)level, pkgOutPtr);
      if (err != 0)
      {
        // Note: alpm_pkg_load sets the handle errno on failure.
        throw GetRequiredCurrentError();
      }

      // The Package class now takes ownership of the native handle *pkgOutPtr
      return new Package(*pkgOutPtr, false);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)filenamePtr);
      // We must free the memory we allocated for the output pointer.
      Marshal.FreeHGlobal((IntPtr)pkgOutPtr);
    }
  }

  public Transactions BeginTransaction(TransactionFlags flags)
  {
    ThrowIfDisposed();
    return new Transactions(this, flags);
  }

  public unsafe Database GetLocalDatabase()
  {
    ThrowIfDisposed();
    var databasePtr = NativeMethods.alpm_get_localdb(_handle);
    ThrowIfCurrentError();
    return new Database(databasePtr);
  }

  public unsafe AlpmList<Database> GetSyncDatabases()
  {
    ThrowIfDisposed();
    var syncDatabases = NativeMethods.alpm_get_syncdbs(_handle);
    ThrowIfCurrentError();
    return AlpmList<Database>.Borrow(syncDatabases, &Database.Factory);
  }

  public unsafe Database RegisterSyncDatabase(string treename, SigLevel level)
  {
    ThrowIfDisposed();
    var treeNameCString = NativeString.ToNative(treename);
    try
    {
      var database = NativeMethods.alpm_register_syncdb(_handle, treeNameCString, (int)level);
      ThrowIfCurrentError();
      return new Database(database);
    }
    finally
    {
      Marshal.FreeHGlobal((nint)treeNameCString);
    }
  }

  public unsafe void UnregisterAllSyncDatabases()
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_unregister_all_syncdbs(_handle);
    if (err != 0) throw GetRequiredCurrentError();
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (_disposed) return;

    // even when alpm_release fails with -1 the handle is invalidated, regardless
    // the handle pointer is not owned by us, so we don't need to free it
    // we should set it to zero anyway, just in case
    _ = NativeMethods.alpm_release(_handle);
    _handle = (byte*)IntPtr.Zero;

    // The callback context must stay alive until native code can no longer call back, and
    // alpm_release itself may still fire events, so it is released only now that alpm_release has
    // returned. This must also happen on the finalizer path: Callback is strongly rooted by its own
    // GCHandle, so if Alpm does not release it, nothing ever will (the Callback and every object its
    // handler delegates keep alive would leak for the life of the process).
    if (disposing)
    {
      Callback.Dispose();
    }
    else
    {
      // A finalizer must never throw - an exception escaping it terminates the process - and the
      // only work here is freeing one GC handle, so failure is not worth propagating.
      try
      {
        Callback.Dispose();
      }
      catch (Exception)
      {
        // Best-effort cleanup on the finalizer thread; skipping it merely leaks the handle.
      }
    }

    Marshal.FreeHGlobal((nint)_initializeErrno);
    _disposed = true;
  }

  ~Alpm()
  {
    Dispose(disposing: false);
  }
}
