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
/// This public class is deliberately non-finalizable, in accordance with the .NET Framework Design
/// Guidelines. A wrapper the garbage collector collects without <see cref="Dispose"/> therefore
/// keeps its package until the session ends: <see cref="Alpm.Dispose()"/> releases every
/// file-loaded package the session still tracks before it releases the handle, and the hand-over to a
/// transaction leaves the release to <c>alpm_trans_release</c>. Disposing the owning
/// <see cref="Alpm"/> is what guarantees that all unmanaged memory goes back.
/// <para>
/// That leak is accepted rather than open. Section 8 of the <c>Lifetime and GC Safety</c> chapter
/// states why no finalizer was added: the guidelines forbid one on a public type, the anchoring
/// chain anchors the session rather than the package - so a borrow such as
/// <see cref="PackageBase.Depends"/> would be freed underneath it - and a finalizer cannot promise
/// to run before <c>alpm_release</c>.
/// </para>
/// </para>
/// </remarks>
public sealed unsafe class LoadedPackage : PackageBase, IDisposable
{
  private readonly Alpm _alpm;
  private readonly ChildLifetime _lifetime;
  private nint _rawPackage;

  internal LoadedPackage(Alpm alpm, _alpm_pkg_t* backingStruct, ChildLifetime lifetime) : base(backingStruct)
  {
    _alpm = alpm ?? throw new ArgumentNullException(nameof(alpm));
    _rawPackage = (nint)backingStruct;
    _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    Lifetime = _lifetime.Capture();
  }

  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !Disposed && _rawPackage != 0;

  /// <summary>Throws when the package was already released or handed to a transaction.</summary>
  /// <remarks>
  /// Checked before invoking native calls to prevent double-free or performing operations on a package
  /// whose ownership has already been transferred to a transaction.
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
    if (pkg == null) return;
    Disposed = true;
    _alpm.UnregisterLoadedPackage(pkg, freeNative: false);
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  public void Dispose()
  {
    if (Disposed) return;
    Disposed = true;
    var pkg = (_alpm_pkg_t*)Interlocked.Exchange(ref _rawPackage, 0);
    if (pkg == null) return;
    _lifetime.Invalidate("LoadedPackage.Dispose()");
    _alpm.UnregisterLoadedPackage(pkg, freeNative: true);
  }
}
