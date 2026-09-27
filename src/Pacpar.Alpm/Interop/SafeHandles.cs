using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// SafeHandle wrapper around an unmanaged libalpm library handle (<c>alpm_handle_t*</c>).
/// Encapsulates the lifecycle of <c>alpm_initialize</c> and <c>alpm_release</c>.
/// </summary>
internal sealed unsafe class SafeAlpmHandle : SafeHandleZeroOrMinusOneIsInvalid
{
  private Callback? _callback;
  private _alpm_errno_t* _initializeErrno;
  private volatile int _releaseResult = -1;

  internal SafeAlpmHandle() : base(ownsHandle: true)
  {
  }

  internal SafeAlpmHandle(_alpm_handle_t* handle) : base(ownsHandle: true)
  {
    SetHandle((nint)handle);
  }

  internal void SetContext(Callback callback, _alpm_errno_t* initializeErrno)
  {
    _callback = callback;
    _initializeErrno = initializeErrno;
  }

  /// <summary>
  /// Whether the native <c>alpm_release</c> invocation succeeded (returned 0).
  /// </summary>
  internal bool ReleaseSucceeded => _releaseResult == 0;

  protected override bool ReleaseHandle()
  {
    var err = NativeMethods.alpm_release((_alpm_handle_t*)handle);
    _releaseResult = err;

    if (err == 0)
    {
      _callback?.Dispose();
    }

    if (_initializeErrno != null)
    {
      NativeMemory.Free(_initializeErrno);
      _initializeErrno = null;
    }

    return err == 0;
  }
}
