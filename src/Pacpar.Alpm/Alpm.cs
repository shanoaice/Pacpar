using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public class Alpm : IDisposable
{
  // opaque handle to libalpm, details not exposed
  private unsafe byte* _handle;
  // ReSharper disable once MemberCanBePrivate.Global
  private readonly unsafe _alpm_errno_t* _errno;
  // ReSharper disable once RedundantDefaultMemberInitializer
  private bool _disposed = false;

  public unsafe Alpm(string root, string dbpath)
  {
    _errno = (_alpm_errno_t*)Marshal.AllocHGlobal(sizeof(_alpm_errno_t));
    *_errno = _alpm_errno_t.ALPM_ERR_OK;
    _handle = NativeMethods.alpm_initialize((byte*)Marshal.StringToHGlobalAnsi(root), (byte*)Marshal.StringToHGlobalAnsi(dbpath), _errno);
    Options = new(_handle);
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
  public unsafe IntPtr Handle => (IntPtr)_handle;

  /// <summary>
  /// The current errno of libalpm
  /// </summary>
  public unsafe _alpm_errno_t Errno => *_errno;

  public unsafe string? GetCurrentErrorString() => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_strerror(Errno));

  public Exception? GetCurrentError() => ErrorHandler.GetException(Errno);

  public unsafe Package LoadPackage(string filename, bool full, int level)
  {
    byte** pkg = (byte**)Marshal.AllocHGlobal(sizeof(nint));
    var err = NativeMethods.alpm_pkg_load(_handle, (byte*)Marshal.StringToHGlobalAnsi(filename), full ? 1 : 0, level, pkg);
    if (err != 0)
    {
      throw GetCurrentError()!;
    }
    return new(*pkg, false);
  }

  public Transactions BeginTransaction(TransactionFlags flags) => new(this, flags);

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual unsafe void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        // dispose managed state (managed objects)
      }
      // even when alpm_release fails with -1 the handle is invalidated, regardless
      // the handle pointer is not owned by us, so we don't need to free it
      // we should set it to zero anyway, just in case
      _ = NativeMethods.alpm_release(_handle);
      _handle = (byte*)IntPtr.Zero;

      Marshal.FreeHGlobal((nint)_errno);
      _disposed = true;
    }
  }

  ~Alpm()
  {
    Dispose(disposing: false);
  }
}
