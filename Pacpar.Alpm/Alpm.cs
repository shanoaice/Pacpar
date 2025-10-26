using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public class Alpm : IDisposable
{
  // opaque handle to libalpm, details not exposed
  private unsafe byte* _handle = (byte*)IntPtr.Zero;
  internal unsafe _alpm_errno_t* errno = (_alpm_errno_t*)IntPtr.Zero;
  private bool _disposed = false;

  public unsafe Alpm(string root, string dbpath)
  {
    errno = (_alpm_errno_t*)Marshal.AllocHGlobal(sizeof(_alpm_errno_t));
    *errno = _alpm_errno_t.ALPM_ERR_OK;
    _handle = NativeMethods.alpm_initialize((byte*)Marshal.StringToHGlobalAnsi(root), (byte*)Marshal.StringToHGlobalAnsi(dbpath), errno);
  }

  public unsafe IntPtr Handle => (IntPtr)_handle;
  public unsafe _alpm_errno_t Errno => *errno;

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

      Marshal.FreeHGlobal((nint)errno);
      _disposed = true;
    }
  }

  ~Alpm()
  {
    Dispose(disposing: false);
  }
}
