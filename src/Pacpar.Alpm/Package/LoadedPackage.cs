using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A package this library loaded from a file with <c>alpm_pkg_load</c>. It owns the package and
/// releases it on <see cref="Dispose"/>, or from the finalizer when the caller forgets.
/// </summary>
/// <remarks>
/// Ownership can also be handed to a transaction, which takes over the release
/// (<see cref="Transaction.AddPackage(LoadedPackage)"/> and <c>alpm.h</c>: a package loaded by
/// <c>alpm_pkg_load()</c> is freed upon <c>alpm_trans_release</c>). After that hand-over this
/// instance is inert: <see cref="Dispose"/> and the finalizer do nothing, and reading the package
/// throws, so the pointer cannot be released twice.
/// <para>
/// This instance is the root owner of its native package, so it anchors its own lifetime-token
/// tree: releasing or disowning it invalidates the root token, which retires every view issued
/// from the package - its <see cref="Files"/> list, group members, anything else - in one step.
/// </para>
/// </remarks>
public sealed unsafe class LoadedPackage : PackageBase, IDisposable
{
  private readonly Lifetime _lifetime;

  internal LoadedPackage(_alpm_pkg_t* backingStruct) : base(backingStruct)
  {
    _lifetime = Lifetime.CreateRoot(this, "a loaded package");
    Lifetime = _lifetime;
  }

  /// <summary>Whether this instance still owns the package (it stops owning it on dispose or hand-over).</summary>
  internal bool OwnsPackage => !Disposed;

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
  /// now and releases it. <see cref="Dispose"/> and the finalizer become no-ops.
  /// </summary>
  /// <remarks>
  /// Named "disown" rather than "release" on purpose: nothing is freed here. Handing the package to
  /// a transaction is the only caller, and it must happen after the native call succeeded - the
  /// caller keeps ownership when it fails.
  /// </remarks>
  internal void Disown()
  {
    ThrowIfNotOwned();
    Disposed = true;
    GC.SuppressFinalize(this);
    // The pointer stays valid inside the transaction, but this wrapper can no longer observe its
    // lifetime, so every view issued from it must stop reading through it.
    _lifetime.Invalidate("the hand-over to a transaction");
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing)
  {
    if (Disposed) return;

    // A failing alpm_pkg_free leaves nothing useful to do: the caller either disposed explicitly
    // or the finalizer is running, and neither can handle a thrown error.
    _ = NativeMethods.alpm_pkg_free(BackingStruct);
    Disposed = true;

    // The native package is gone: retire the token tree rooted at this instance. On the finalizer
    // path this must stay O(1) and allocation-free, hence fromFinalizer (no registry walking, no
    // debug assertion).
    _lifetime.Invalidate(
      disposing ? "LoadedPackage.Dispose()" : "the owning LoadedPackage was garbage-collected",
      fromFinalizer: !disposing);
  }

  ~LoadedPackage() => Dispose(false);
}
