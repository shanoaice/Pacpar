using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

// ReSharper disable once ClassNeverInstantiated.Global
public class Alpm : IDisposable
{
  // opaque handle to libalpm, details not exposed
  private unsafe byte* _handle;
  // A handle to this managed object that can be safely passed to native code.
  private GCHandle<Alpm> _thisHandle;
  // ReSharper disable once MemberCanBePrivate.Global
  private readonly unsafe _alpm_errno_t* _errno;
  // ReSharper disable once RedundantDefaultMemberInitializer
  private bool _disposed = false;

  public unsafe Alpm(string root, string dbpath)
  {
    _errno = (_alpm_errno_t*)Marshal.AllocHGlobal(sizeof(_alpm_errno_t));
    *_errno = _alpm_errno_t.ALPM_ERR_OK;

    // Allocate a GCHandle to this instance. This gives us a stable
    // reference that we can pass to native code as a 'context' pointer.
    _thisHandle = new GCHandle<Alpm>(this);

    var rootPtr = Marshal.StringToHGlobalAnsi(root);
    var dbpathPtr = Marshal.StringToHGlobalAnsi(dbpath);
    try
    {
      _handle = NativeMethods.alpm_initialize((byte*)rootPtr, (byte*)dbpathPtr, _errno);
    }
    finally
    {
      Marshal.FreeHGlobal(rootPtr);
      Marshal.FreeHGlobal(dbpathPtr);
    }
    if (_handle == null) throw ErrorHandler.GetException(*_errno) ?? new Exception("Failed to initialize libalpm.");

    Options = new AlpmOptions(_handle);
  }

  private void ThrowIfDisposed()
  {
    if (_disposed) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Exposes methods to set libalpm options.
  /// </summary>
  public AlpmOptions Options { get; }

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
  /// The current errno of libalpm
  /// </summary>
  public unsafe _alpm_errno_t Errno
  {
    get
    {
      ThrowIfDisposed();
      return *_errno;
    }
  }

  // ReSharper disable once MemberCanBePrivate.Global
  public unsafe string? GetCurrentErrorString()
  {
    ThrowIfDisposed();
    return Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_strerror(Errno));
  }

  public Exception? GetCurrentError()
  {
    ThrowIfDisposed();
    return ErrorHandler.GetException(Errno);
  }

  public unsafe Package LoadPackage(string filename, bool full, int level)
  {
    ThrowIfDisposed();
    var filenamePtr = Marshal.StringToHGlobalAnsi(filename);
    // This is a pointer to a pointer, where libalpm will write the package handle.
    var pkgOutPtr = (byte**)Marshal.AllocHGlobal(sizeof(nint));
    try
    {
      var err = NativeMethods.alpm_pkg_load(_handle, (byte*)filenamePtr, full ? 1 : 0, level, pkgOutPtr);
      if (err != 0)
      {
        // Note: alpm_pkg_load sets the handle errno on failure.
        throw GetCurrentError() ?? new Exception($"Failed to load package: {GetCurrentErrorString()}");
      }
      // The Package class now takes ownership of the native handle *pkgOutPtr
      return new Package(*pkgOutPtr, false);
    }
    finally
    {
      Marshal.FreeHGlobal(filenamePtr);
      // We must free the memory we allocated for the output pointer.
      Marshal.FreeHGlobal((IntPtr)pkgOutPtr);
    }
  }

  public Transactions BeginTransaction(TransactionFlags flags)
  {
    ThrowIfDisposed();
    return new Transactions(this, flags);
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (_disposed) return;
    if (disposing)
    {
      // dispose managed state (managed objects)

      // It is crucial to free the GCHandle when the object is disposed
      // to allow the GC to collect the object and to avoid handle leaks.
      if (_thisHandle.IsAllocated)
      {
        _thisHandle.Dispose();
      }
    }
    // even when alpm_release fails with -1 the handle is invalidated, regardless
    // the handle pointer is not owned by us, so we don't need to free it
    // we should set it to zero anyway, just in case
    _ = NativeMethods.alpm_release(_handle);
    _handle = (byte*)IntPtr.Zero;

    Marshal.FreeHGlobal((nint)_errno);
    _disposed = true;
  }

  ~Alpm()
  {
    Dispose(disposing: false);
  }
}
