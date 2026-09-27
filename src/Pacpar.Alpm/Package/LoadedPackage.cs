using System.Runtime.CompilerServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package this library loaded from a file with <c>alpm_pkg_load</c>. It owns the package and
/// releases it on <see cref="Dispose"/>, or when the owning <see cref="Alpm"/> context is disposed.
/// </summary>
/// <remarks>
/// Ownership can also be handed to a transaction, which takes over the release
/// (<see cref="Transaction.AddPackage(LoadedPackage)"/> and <c>alpm.h</c>: a package loaded by
/// <c>alpm_pkg_load()</c> is freed upon <c>alpm_trans_release</c>). After that hand-over this
/// instance is inert: <see cref="Dispose"/> does nothing, and reading the package
/// throws, so the pointer cannot be released twice.
/// <para>
/// In accordance with .NET Framework Design Guidelines, this public class is deliberately
/// non-finalizable. Native lifetime is tracked by the parent <see cref="Alpm"/> session registry
/// as a safety backstop if <see cref="Dispose"/> is omitted, sweeping any unreleased packages when
/// <see cref="Alpm.Dispose()"/> is called. Disposing the owning <see cref="Alpm"/> context is
/// required to ensure all unmanaged memory is released.
/// </para>
/// </remarks>
public sealed unsafe class LoadedPackage : PackageBase, IDisposable
{
  private readonly Alpm _alpm;
  private readonly Lifetime _lifetime;
  private nint _rawPackage;

  internal LoadedPackage(Alpm alpm, _alpm_pkg_t* backingStruct, Lifetime lifetime) : base(backingStruct)
  {
    _alpm = alpm ?? throw new ArgumentNullException(nameof(alpm));
    _rawPackage = (nint)backingStruct;
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    Lifetime = _lifetime;
  }

  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !Disposed && _rawPackage != 0;

  /// <summary>Throws when the package was already released or handed to a transaction.</summary>
  /// <remarks>
  /// Checked by the caller <i>before</i> it touches libalpm: a hand-over that already happened is a
  /// caller error, and repeating the native call would be pointless (libalpm dedupes the same
  /// package pointer in a transaction's list anyway, probed).
  /// </remarks>
  internal void ThrowIfNotOwned()
  {
    if (!OwnsPackage) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Gives up ownership <b>without</b> releasing the package, because somebody else owns the pointer
  /// now and releases it. <see cref="Dispose"/> becomes a no-op.
  /// </summary>
  /// <remarks>
  /// Named "disown" rather than "release" on purpose: nothing is freed here. Handing the package to
  /// a transaction is the only caller, and it must happen after the native call succeeded - the
  /// caller keeps ownership when it fails.
  /// </remarks>
  internal void Disown()
  {
    ThrowIfNotOwned();
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg != null)
    {
      Disposed = true;
      _alpm.UnregisterLoadedPackage(pkg, freeNative: false);
      _lifetime.Invalidate("the hand-over to a transaction");
    }
  }

  public void Dispose()
  {
    if (Disposed) return;
    Disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg != null)
    {
      _lifetime.Invalidate("LoadedPackage.Dispose()");
      _alpm.UnregisterLoadedPackage(pkg, freeNative: true);
    }
  }
}
