using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

public partial class Transaction
{
  internal static bool TryCreate(AlpmSession alpmLibrary, TransactionFlags flags,
    [NotNullWhen(true)] out Transaction? transaction,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    var err = NativeMethods.alpm_trans_init(alpmLibrary.Handle, (int)flags);
    if (err != 0)
    {
      transaction = null;
      failure = NativeCall.Failure(alpmLibrary.Handle, "start transaction");
      return false;
    }

    transaction = new Transaction(alpmLibrary);
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Prepare"/>.
  /// </summary>
  internal unsafe bool TryPrepare([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    _alpm_list_t* errData = null;

    var err = NativeMethods.alpm_trans_prepare(_library.Handle, &errData);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Take(_library.Errno, errData, "prepare transaction");
      GC.KeepAlive(_library);
      GC.KeepAlive(this);
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="AddPackage(PackageView)"/>.
  /// </summary>
  internal unsafe bool TryAddPackage(PackageView pkg, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(pkg);

    var err = NativeMethods.alpm_add_pkg(_library.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = NativeCall.Failure(_library.Handle, "add package");
      return false;
    }
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="AddPackage(LoadedPackage)"/>.
  /// </summary>
  internal unsafe bool TryAddPackage(LoadedPackage pkg,
    [NotNullWhen(true)] out PackageView? view,
    [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(pkg);

    // Before touching libalpm: this instance is already inert when it was handed over once, and
    // repeating the call is a caller error rather than something libalpm could act on.
    pkg.ThrowIfNotOwned();

    var err = NativeMethods.alpm_add_pkg(_library.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      view = null;
      failure = NativeCall.Failure(_library.Handle, "add package");
      return false;
    }

    // The pointer is now the transaction's to free; the wrapper must never release it again. Read the
    // views off it first, because the hand-over retires the wrapper for reads too. The view carries
    // the transaction's token: the package lives exactly as long as the transaction owns it.
    view = new PackageView(pkg.BackingStruct, Lifetime);
    pkg.Disown();
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="RemovePackage(PackageView)"/>.
  /// </summary>
  internal unsafe bool TryRemovePackage(PackageView pkg, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(pkg);

    var err = NativeMethods.alpm_remove_pkg(_library.Handle, pkg.BackingStruct);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = NativeCall.Failure(_library.Handle, "remove package");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="SystemUpgrade(bool)"/>.
  /// </summary>
  internal bool TrySystemUpgrade(bool enableDowngrade, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_sync_sysupgrade(_library.Handle, enableDowngrade ? 1 : 0);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = NativeCall.Failure(_library.Handle, "compute system upgrade");
      return false;
    }
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Interrupt"/>.
  /// </summary>
  internal bool TryInterrupt([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_trans_interrupt(_library.Handle);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = NativeCall.Failure(_library.Handle, "interrupt transaction");
      return false;
    }
    failure = null;
    return true;
  }

  /// <summary>
  /// The failure-returning seam for <see cref="Commit"/>.
  /// </summary>
  internal unsafe bool TryCommit([NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();

    // A commit may free package caches and the packages this transaction loaded, and its events hand
    // payload views to callbacks. Retire every stamp in the session before the call, so nothing can
    // pass its check while libalpm rewrites the object graph; views a callback creates capture the
    // post-bump generation and stay valid for the duration of that callback.
    _library.RootLifetime.Invalidate("Transaction.Commit()");

    _alpm_list_t* messages = null;
    var err = NativeMethods.alpm_trans_commit(_library.Handle, &messages);
    if (err != 0)
    {
      _completionState = CompletionState.Faulted;
      failure = AlpmFailure.Take(_library.Errno, messages, "commit transaction");
      GC.KeepAlive(_library);
      GC.KeepAlive(this);
      return false;
    }
    _completionState = CompletionState.Committed;
    // every view borrowed from the local database now points into freed memory. Retire the local
    // database's token conservatively - libalpm does not tell us which caches it dropped - and let
    // the next GetLocalDatabase() issue a fresh one. Callers that keep package data across a commit
    // call ToSnapshot() first.
    _library.InvalidateLocalDatabase("Transaction.Commit()");
    failure = null;
    return true;
  }
}
