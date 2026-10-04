using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;

namespace Pacpar.Alpm;

/// <summary>
///  Transaction flags
/// </summary>
/// <remarks>
/// <c>0</c> — the value the optional parameter of <see cref="Alpm.BeginTransaction"/> defaults to — is
/// libalpm's default mode rather than "no configuration": every dependency, conflict and file-conflict
/// check runs, hooks and scriptlets are executed, the database is locked and the filesystem is
/// written. Every member below deviates from that, either as an opt-out
/// (<c>NODEPS</c>, <c>NOCONFLICTS</c>, <c>NOSAVE</c>, <c>NOLOCK</c>, <c>DBONLY</c>, …) or as an
/// explicit opt-in (<c>CASCADE</c>, <c>RECURSE</c>, <c>ALLDEPS</c>, …), so a caller who does not want
/// one of those deviations has nothing to pass.
/// </remarks>
[Flags]
public enum TransactionFlags : uint
{
  /// <summary>
  ///  Ignore dependency checks.
  /// </summary>
  AlpmTransFlagNodeps = 1,
  /// <summary>
  ///  Delete files even if they are tagged as backup.
  /// </summary>
  AlpmTransFlagNosave = 4,
  /// <summary>
  ///  Ignore version numbers when checking dependencies.
  /// </summary>
  AlpmTransFlagNodepversion = 8,
  /// <summary>
  ///  Remove also any packages depending on a package being removed.
  /// </summary>
  AlpmTransFlagCascade = 16,
  /// <summary>
  ///  Remove packages and their unneeded deps (not explicitly installed).
  /// </summary>
  AlpmTransFlagRecurse = 32,
  /// <summary>
  ///  Modify database but do not commit changes to the filesystem.
  /// </summary>
  AlpmTransFlagDbonly = 64,
  /// <summary>
  ///  Do not run hooks during a transaction
  /// </summary>
  AlpmTransFlagNohooks = 128,
  /// <summary>
  ///  Use ALPM_PKG_REASON_DEPEND when installing packages.
  /// </summary>
  AlpmTransFlagAlldeps = 256,
  /// <summary>
  ///  Only download packages and do not actually install.
  /// </summary>
  AlpmTransFlagDownloadonly = 512,
  /// <summary>
  ///  Do not execute install scriptlets after installing.
  /// </summary>
  AlpmTransFlagNoscriptlet = 1024,
  /// <summary>
  ///  Ignore dependency conflicts.
  /// </summary>
  AlpmTransFlagNoconflicts = 2048,
  /// <summary>
  ///  Do not install a package if it is already installed and up to date.
  /// </summary>
  AlpmTransFlagNeeded = 8192,
  /// <summary>
  ///  Use ALPM_PKG_REASON_EXPLICIT when installing packages.
  /// </summary>
  AlpmTransFlagAllexplicit = 16384,
  /// <summary>
  ///  Do not remove a package if it is needed by another one.
  /// </summary>
  AlpmTransFlagUnneeded = 32768,
  /// <summary>
  ///  Remove also explicitly installed unneeded deps (use with ALPM_TRANS_FLAG_RECURSE).
  /// </summary>
  AlpmTransFlagRecurseall = 65536,
  /// <summary>
  ///  Do not lock the database during the operation.
  /// </summary>
  AlpmTransFlagNolock = 131072,
}

public class Transaction : IDisposable
{
  private bool _released;

  private readonly Alpm _library;

  /// <summary>
  /// The transaction's lifetime token, a child of the handle's root token: it is retired both by
  /// this transaction's own successful release and - through the parent chain - by the handle's
  /// disposal. Every view this transaction issues (the <see cref="AddPackage(LoadedPackage)"/>
  /// return value and the elements of <see cref="GetAddedPackages"/>/<see cref="GetRemovedPackages"/>)
  /// carries it.
  /// </summary>
  internal readonly Lifetime Lifetime;

  private Transaction(Alpm alpmLibrary)
  {
    _library = alpmLibrary;
    Lifetime = alpmLibrary.RootLifetime.CreateChild("the transaction");
  }

  internal static bool TryCreate(Alpm alpmLibrary, TransactionFlags flags,
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

  private void ThrowIfDisposed()
  {
    if (_released) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Whether this transaction has been released: <c>true</c> once <see cref="Dispose()"/> has run to
  /// the point where nothing of it is left to release.
  /// </summary>
  /// <remarks>
  /// <see cref="Dispose()"/> never throws, so this is how a caller learns whether the release
  /// actually happened. <c>false</c> after a <see cref="Dispose()"/> means
  /// <c>alpm_trans_release</c> failed: the transaction and its database lock are still held, and a
  /// later <c>alpm_release</c> fails with <c>ALPM_ERR_TRANS_NOT_NULL</c> without freeing the handle.
  /// Read <see cref="ReleaseFailure"/> for why.
  /// <para>
  /// It is <c>true</c> when the owning handle was already disposed instead: <see cref="Alpm"/>
  /// disposes the active transaction before it releases the handle, precisely because
  /// <c>alpm_release</c> refuses to run while one is initialized.
  /// </para>
  /// </remarks>
  public bool IsReleased => _released;

  /// <summary>
  /// The failure <see cref="Dispose()"/> observed from <c>alpm_trans_release</c>, or <c>null</c>
  /// when the release succeeded or was never attempted.
  /// </summary>
  /// <remarks>
  /// A cleanup failure is reported here rather than thrown: throwing out of a <c>finally</c> or a
  /// <c>using</c> turns a teardown problem into a crash that has nothing to do with the operation
  /// the caller wrote. The value is an <see cref="AlpmException"/> carrying the errno — an
  /// <see cref="AlpmNativeFailureException"/> when libalpm reported the failure without setting one
  /// — or an <see cref="OutOfMemoryException"/> for <c>ALPM_ERR_MEMORY</c>.
  /// <para>
  /// The first failure is kept: a later <see cref="Dispose()"/> that retries does not clear it,
  /// because the state that failure describes is the one still in force.
  /// </para>
  /// <para>
  /// This is not written to the consumer's log callback. That callback is an inbound channel from
  /// libalpm and may never have been registered; teardown is also when it is least safe to call.
  /// </para>
  /// </remarks>
  public Exception? ReleaseFailure { get; private set; }

  /// <summary>
  /// Prepares the transaction: the dependency, conflict and architecture checks.
  /// </summary>
  /// <remarks>
  /// A successful call has nothing to report - libalpm leaves the list it dumps into the output
  /// parameter empty (measured), which is why this method answers <c>void</c>. A failure is reported
  /// as the <see cref="AlpmTransactionException"/> case that matches the errno, carrying the payload
  /// as a managed snapshot; the native list is freed at the same moment, with the element destructor
  /// the errno requires (see <see cref="AlpmFailure.Take"/>).
  /// </remarks>
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
      failure = AlpmFailure.Take(_library.Errno, errData, "prepare transaction");
      GC.KeepAlive(_library);
      GC.KeepAlive(this);
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// Prepares the transaction: the dependency, conflict and architecture checks.
  /// </summary>
  /// <remarks>
  /// A successful call has nothing to report - libalpm leaves the list it dumps into the output
  /// parameter empty (measured), which is why this method answers <c>void</c>. A failure is reported
  /// as the <see cref="AlpmTransactionException"/> case that matches the errno, carrying the payload
  /// as a managed snapshot; the native list is freed at the same moment, with the element destructor
  /// the errno requires (see <see cref="AlpmFailure.Take"/>).
  /// </remarks>
  public unsafe void Prepare()
  {
    if (!TryPrepare(out var failure))
    {
      throw failure.ToException("Failed to prepare transaction");
    }
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
      failure = NativeCall.Failure(_library.Handle, "add package");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// Adds a package that libalpm already owns (a database package, for example a member of the local
  /// database).
  /// </summary>
  /// <remarks>
  /// The transaction only borrows it: releasing the transaction does not free it. A package this
  /// library loaded from a file must go through <see cref="AddPackage(LoadedPackage)"/> instead, which
  /// is the only overload that can take over the release - the two overloads cannot be confused,
  /// because <see cref="PackageView"/> and <see cref="LoadedPackage"/> do not convert to one another.
  /// </remarks>
  public void AddPackage(PackageView pkg)
  {
    if (!TryAddPackage(pkg, out var failure))
    {
      throw failure.ToException($"Failed to add package: {pkg.Name}");
    }
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
  /// Adds a package this library loaded from a file, handing over its ownership: libalpm frees such a
  /// package when the transaction is released (<c>alpm.h</c>: "If the package was loaded by
  /// <c>alpm_pkg_load()</c>, it will be freed upon <c>alpm_trans_release</c> invocation").
  /// </summary>
  /// <returns>
  /// A borrowed view of the package, valid while the transaction owns it: it carries this
  /// transaction's lifetime token, so releasing the transaction - or disposing the handle - retires
  /// it. The view replaces the wrapper whose ownership was given up, which stops answering reads
  /// after this call; the view itself owns nothing, so giving it to
  /// <see cref="AddPackage(PackageView)"/> never transfers ownership.
  /// </returns>
  /// <exception cref="ObjectDisposedException">
  /// The package was already released or handed to a transaction.
  /// </exception>
  public unsafe PackageView AddPackage(LoadedPackage pkg)
  {
    if (!TryAddPackage(pkg, out var view, out var failure))
    {
      throw failure.ToException($"Failed to add package: {pkg.Name}");
    }
    return view;
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
      failure = NativeCall.Failure(_library.Handle, "remove package");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// Marks an installed package to be removed as part of this transaction.
  /// </summary>
  /// <param name="pkg">The package to remove.</param>
  public unsafe void RemovePackage(PackageView pkg)
  {
    if (!TryRemovePackage(pkg, out var failure))
    {
      throw failure.ToException($"Failed to remove package: {pkg.Name}");
    }
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
      failure = NativeCall.Failure(_library.Handle, "compute system upgrade");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// Searches registered sync databases for updates to installed packages and adds them to this transaction.
  /// </summary>
  /// <param name="enableDowngrade">Whether to allow downgrading packages if the repository version is older.</param>
  public void SystemUpgrade(bool enableDowngrade)
  {
    if (!TrySystemUpgrade(enableDowngrade, out var failure))
    {
      throw failure.ToException();
    }
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
      failure = NativeCall.Failure(_library.Handle, "interrupt transaction");
      return false;
    }

    failure = null;
    return true;
  }

  /// <summary>
  /// Requests that the active transaction operation be interrupted.
  /// </summary>
  public void Interrupt()
  {
    if (!TryInterrupt(out var failure))
    {
      throw failure.ToException();
    }
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
      failure = AlpmFailure.Take(_library.Errno, messages, "commit transaction");
      GC.KeepAlive(_library);
      GC.KeepAlive(this);
      return false;
    }
    // A successful commit rewrites the local database and frees libalpm's in-memory package caches:
    // every view borrowed from the local database now points into freed memory. Retire the local
    // database's token conservatively - libalpm does not tell us which caches it dropped - and let
    // the next GetLocalDatabase() issue a fresh one. Callers that keep package data across a commit
    // call ToSnapshot() first.
    _library.InvalidateLocalDatabase("Transaction.Commit()");
    failure = null;
    return true;
  }

  /// <summary>
  /// Commits the transaction.
  /// </summary>
  /// <remarks>
  /// A successful commit has nothing to report - libalpm leaves the output parameter empty
  /// (measured), which is why this method answers <c>void</c>. A failure is reported as the
  /// <see cref="AlpmTransactionException"/> case that matches the errno: conflicting files, or a list
  /// of package names, depending on the errno. The native list is freed at the same moment, with the
  /// element destructor that errno requires (see <see cref="AlpmFailure.Take"/>).
  /// </remarks>
  public unsafe void Commit()
  {
    if (!TryCommit(out var failure))
    {
      throw failure.ToException("Failed to commit transaction");
    }
  }

  /// <summary>
  /// The packages this transaction is going to install, as a borrowed view.
  /// </summary>
  /// <remarks>
  /// The list and its elements carry the transaction's lifetime token: releasing the transaction
  /// frees the file-loaded packages it owns, after which this view answers
  /// <see cref="AlpmLifetimeException"/> instead of reading freed memory.
  /// </remarks>
  public unsafe AlpmList<PackageView> GetAddedPackages()
  {
    ThrowIfDisposed();
    return AlpmList<PackageView>.Borrow(NativeMethods.alpm_trans_get_add(_library.Handle),
      &PackageView.Factory, Lifetime);
  }

  /// <summary>
  /// Gets the flags configured for this transaction.
  /// </summary>
  public TransactionFlags GetFlags()
  {
    ThrowIfDisposed();
    var flags = (TransactionFlags)NativeMethods.alpm_trans_get_flags(_library.Handle);
    GC.KeepAlive(_library);
    GC.KeepAlive(this);
    return flags;
  }
  /// <summary>
  /// The packages this transaction is going to remove, as a borrowed view.
  /// </summary>
  /// <remarks>
  /// Like <see cref="GetAddedPackages"/>, the view carries the transaction's lifetime token and is
  /// retired when the transaction is released.
  /// </remarks>
  public unsafe AlpmList<PackageView> GetRemovedPackages()
  {
    ThrowIfDisposed();
    return AlpmList<PackageView>.Borrow(NativeMethods.alpm_trans_get_remove(_library.Handle),
      &PackageView.Factory, Lifetime);
  }

  /// <summary>
  /// Releases the transaction (and its database lock) deterministically.
  /// </summary>
  /// <remarks>
  /// A failed <c>alpm_trans_release</c> is recorded, not thrown: throwing from a <c>finally</c> or a
  /// <c>using</c> would replace the caller's own failure with one about teardown. Ask
  /// <see cref="IsReleased"/> whether the release happened, and <see cref="ReleaseFailure"/> why it
  /// did not.
  /// </remarks>
  public void Dispose()
  {
    if (_released) return;

    GC.SuppressFinalize(this);

    if (_library.Disposed)
    {
      // The session released this transaction on its way out; see IsReleased.
      _released = true;
      return;
    }

    // Retire before the free. alpm_trans_release frees the transaction and the file-loaded packages
    // it owns, and a view must not pass its check while that happens. Invalidate is also correct on
    // the failure path: alpm_trans_release only fails when the transaction is already gone.
    Lifetime.Invalidate("Transaction.Dispose()");

    var err = NativeMethods.alpm_trans_release(_library.Handle);
    if (err == 0)
    {
      _released = true;
      _library.CurrentTransaction = null;
      return;
    }

    // Nothing is thrown, so the outcome is what the caller has to go on. The errno is read here, as
    // the first thing after the failing call, and only then is the owner kept alive.
    var failure = NativeCall.Failure(_library.Handle, "release transaction");
    GC.KeepAlive(_library);
    GC.KeepAlive(this);

    ReleaseFailure ??= failure.ToException();
  }
}
