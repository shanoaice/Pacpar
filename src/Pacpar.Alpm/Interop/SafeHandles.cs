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

  // The session's root lifetime domain, held weakly on purpose. This handle is the only object with a
  // finalizer on the session-release path, so it needs the domain to retire every stamp before it
  // frees the native graph. A strong reference would also root the Alpm through the domain's owner
  // reference, and with it the callback context, which the weak-GCHandle design deliberately keeps
  // collectable. A weak reference is enough: a live view holds its domain strongly, so the target is
  // still there exactly when there is something to invalidate. When it is gone, no view can exist.
  private WeakReference<RootLifetime>? _domain;

  internal SafeAlpmHandle() : base(ownsHandle: true)
  {
  }

  internal SafeAlpmHandle(_alpm_handle_t* handle) : base(ownsHandle: true)
  {
    SetHandle((nint)handle);
  }

  /// <summary>Attaches the session's root domain so <see cref="ReleaseHandle"/> can retire its stamps.</summary>
  internal void OwnDomain(RootLifetime domain) => _domain = new WeakReference<RootLifetime>(domain);

  /// <summary>
  /// Whether the native <c>alpm_release</c> invocation succeeded (returned 0).
  /// </summary>
  internal bool ReleaseSucceeded => _releaseResult == 0;

  protected override bool ReleaseHandle()
  {
    // Before the free, never after: a stamp that still passes its check while alpm_release tears the
    // graph down would let a view read freed memory. Invalidating early only risks a false positive.
    if (_domain is not null && _domain.TryGetTarget(out var domain))
    {
      domain.Invalidate("the owning Alpm was garbage-collected");
    }

    var err = NativeMethods.alpm_release((_alpm_handle_t*)handle);
    _releaseResult = err;

    return err == 0;
  }
}
