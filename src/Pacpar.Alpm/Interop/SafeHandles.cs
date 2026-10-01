using Microsoft.Win32.SafeHandles;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// SafeHandle wrapper around an unmanaged libalpm library handle (<c>alpm_handle_t*</c>).
/// Encapsulates the lifecycle of <c>alpm_initialize</c> and <c>alpm_release</c>.
/// </summary>
internal sealed unsafe class SafeAlpmHandle : SafeHandleZeroOrMinusOneIsInvalid
{
  private volatile int _releaseResult = -1;

  internal SafeAlpmHandle() : base(ownsHandle: true)
  {
  }

  internal SafeAlpmHandle(_alpm_handle_t* handle) : base(ownsHandle: true)
  {
    SetHandle((nint)handle);
  }

  /// <summary>
  /// Whether the native <c>alpm_release</c> invocation succeeded (returned 0).
  /// </summary>
  internal bool ReleaseSucceeded => _releaseResult == 0;

  protected override bool ReleaseHandle()
  {
    var err = NativeMethods.alpm_release((_alpm_handle_t*)handle);
    _releaseResult = err;

    return err == 0;
  }
}
