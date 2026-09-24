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
  ALPM_TRANS_FLAG_NODEPS = 1,
  /// <summary>
  ///  Delete files even if they are tagged as backup.
  /// </summary>
  ALPM_TRANS_FLAG_NOSAVE = 4,
  /// <summary>
  ///  Ignore version numbers when checking dependencies.
  /// </summary>
  ALPM_TRANS_FLAG_NODEPVERSION = 8,
  /// <summary>
  ///  Remove also any packages depending on a package being removed.
  /// </summary>
  ALPM_TRANS_FLAG_CASCADE = 16,
  /// <summary>
  ///  Remove packages and their unneeded deps (not explicitly installed).
  /// </summary>
  ALPM_TRANS_FLAG_RECURSE = 32,
  /// <summary>
  ///  Modify database but do not commit changes to the filesystem.
  /// </summary>
  ALPM_TRANS_FLAG_DBONLY = 64,
  /// <summary>
  ///  Do not run hooks during a transaction
  /// </summary>
  ALPM_TRANS_FLAG_NOHOOKS = 128,
  /// <summary>
  ///  Use ALPM_PKG_REASON_DEPEND when installing packages.
  /// </summary>
  ALPM_TRANS_FLAG_ALLDEPS = 256,
  /// <summary>
  ///  Only download packages and do not actually install.
  /// </summary>
  ALPM_TRANS_FLAG_DOWNLOADONLY = 512,
  /// <summary>
  ///  Do not execute install scriptlets after installing.
  /// </summary>
  ALPM_TRANS_FLAG_NOSCRIPTLET = 1024,
  /// <summary>
  ///  Ignore dependency conflicts.
  /// </summary>
  ALPM_TRANS_FLAG_NOCONFLICTS = 2048,
  /// <summary>
  ///  Do not install a package if it is already installed and up to date.
  /// </summary>
  ALPM_TRANS_FLAG_NEEDED = 8192,
  /// <summary>
  ///  Use ALPM_PKG_REASON_EXPLICIT when installing packages.
  /// </summary>
  ALPM_TRANS_FLAG_ALLEXPLICIT = 16384,
  /// <summary>
  ///  Do not remove a package if it is needed by another one.
  /// </summary>
  ALPM_TRANS_FLAG_UNNEEDED = 32768,
  /// <summary>
  ///  Remove also explicitly installed unneeded deps (use with ALPM_TRANS_FLAG_RECURSE).
  /// </summary>
  ALPM_TRANS_FLAG_RECURSEALL = 65536,
  /// <summary>
  ///  Do not lock the database during the operation.
  /// </summary>
  ALPM_TRANS_FLAG_NOLOCK = 131072,
}

public class Transactions : IDisposable
{
  private bool _released;

  private readonly Alpm _library;

  internal unsafe Transactions(Alpm alpmLibrary, TransactionFlags flags)
  {
    _library = alpmLibrary;
    var err = NativeMethods.alpm_trans_init((_alpm_handle_t*)_library.AsHandle(), (int)flags);
    if (err != 0)
    {
      throw _library.GetRequiredCurrentError();
    }
  }

  private void ThrowIfDisposed()
  {
    if (_released) throw new ObjectDisposedException(GetType().FullName);
  }

  /// <summary>
  /// Prepares the transaction: the dependency, conflict and architecture checks.
  /// </summary>
  /// <remarks>
  /// A successful call has nothing to report - libalpm leaves the list it dumps into the output
  /// parameter empty (measured), which is why this method answers <c>void</c>. A failure is reported
  /// as the <see cref="AlpmTransactionException"/> case that matches the errno, carrying the payload
  /// as a managed snapshot; the native list is freed at the same moment, with the element destructor
  /// the errno requires (see the exception's <c>TakeFailure</c> factory).
  /// </remarks>
  public unsafe void Prepare()
  {
    ThrowIfDisposed();

    _alpm_list_t* errData = null;

    var err = NativeMethods.alpm_trans_prepare((_alpm_handle_t*)_library.AsHandle(), &errData);
    if (err != 0)
    {
      throw AlpmTransactionException.TakeFailure(_library.Errno, errData, "Failed to prepare transaction");
    }
  }

  /// <summary>
  /// Adds a package that libalpm already owns (a database package, for example a member of the local
  /// database).
  /// </summary>
  /// <remarks>
  /// The transaction only borrows it: releasing the transaction does not free it. A package this
  /// library loaded from a file must go through <see cref="AddPackage(LoadedPackage)"/> instead, which
  /// is the only overload that can take over the release - the two overloads cannot be confused,
  /// because <see cref="Package"/> and <see cref="LoadedPackage"/> do not convert to one another.
  /// </remarks>
  public unsafe void AddPackage(Package pkg)
  {
    ThrowIfDisposed();
    AddCore(pkg);
  }

  /// <summary>
  /// Adds a package this library loaded from a file, handing over its ownership: libalpm frees such a
  /// package when the transaction is released (<c>alpm.h</c>: "If the package was loaded by
  /// <c>alpm_pkg_load()</c>, it will be freed upon <c>alpm_trans_release</c> invocation").
  /// </summary>
  /// <returns>
  /// A borrowed view of the package, valid while the transaction owns it. It replaces the wrapper
  /// whose ownership was given up, which stops answering reads after this call; the view does not own
  /// anything, so giving it to <see cref="AddPackage(Package)"/> never transfers ownership.
  /// </returns>
  /// <exception cref="ObjectDisposedException">
  /// The package was already released or handed to a transaction.
  /// </exception>
  public unsafe Package AddPackage(LoadedPackage pkg)
  {
    ThrowIfDisposed();

    // Before touching libalpm: this instance is already inert when it was handed over once, and
    // repeating the call is a caller error rather than something libalpm could act on.
    pkg.ThrowIfNotOwned();

    AddCore(pkg);

    // The pointer is now the transaction's to free; the wrapper must never release it again. Read the
    // views off it first, because the hand-over retires the wrapper for reads too.
    var view = new Package(pkg.BackingStruct);
    pkg.Disown();
    return view;
  }

  /// <summary>Adds <paramref name="pkg"/> to the transaction, without deciding who owns it.</summary>
  private unsafe void AddCore(PackageBase pkg)
  {
    var err = NativeMethods.alpm_add_pkg((_alpm_handle_t*)_library.AsHandle(), pkg.BackingStruct);
    if (err != 0)
    {
      throw new AlpmPackageException(_library.Errno, package: pkg, context: $"Failed to add package: {pkg.Name}");
    }
  }

  public unsafe void RemovePackage(Package pkg)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_remove_pkg((_alpm_handle_t*)_library.AsHandle(), pkg.BackingStruct);
    if (err != 0)
    {
      throw new AlpmPackageException(_library.Errno, package: pkg, context: $"Failed to remove package: {pkg.Name}");
    }
  }

  public unsafe void SystemUpgrade(bool enableDowngrade)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_sync_sysupgrade((_alpm_handle_t*)_library.AsHandle(), enableDowngrade ? 1 : 0);
    if (err != 0)
    {
      throw _library.GetRequiredCurrentError();
    }
  }

  public unsafe void Interrupt()
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_trans_interrupt((_alpm_handle_t*)_library.AsHandle());
    if (err != 0)
    {
      throw _library.GetRequiredCurrentError();
    }
  }

  /// <summary>
  /// Commits the transaction.
  /// </summary>
  /// <remarks>
  /// A successful commit has nothing to report - libalpm leaves the output parameter empty
  /// (measured), which is why this method answers <c>void</c>. A failure is reported as the
  /// <see cref="AlpmTransactionException"/> case that matches the errno: conflicting files, or a list
  /// of package names, depending on the errno. The native list is freed at the same moment, with the
  /// element destructor that errno requires (see the exception's <c>TakeFailure</c> factory).
  /// </remarks>
  public unsafe void Commit()
  {
    ThrowIfDisposed();

    _alpm_list_t* messages = null;
    var err = NativeMethods.alpm_trans_commit((_alpm_handle_t*)_library.AsHandle(), &messages);
    if (err != 0)
    {
      throw AlpmTransactionException.TakeFailure(_library.Errno, messages,  "Failed to commit transaction");
    }
  }

  public unsafe AlpmList<Package> GetAddedPackages()
  {
    ThrowIfDisposed();
    return AlpmList<Package>.Borrow(NativeMethods.alpm_trans_get_add((_alpm_handle_t*)_library.AsHandle()),
      &Package.Factory);
  }

  public unsafe TransactionFlags GetFlags()
  {
    ThrowIfDisposed();
    return (TransactionFlags)NativeMethods.alpm_trans_get_flags((_alpm_handle_t*)_library.AsHandle());
  }

  public unsafe AlpmList<Package> GetRemovedPackages()
  {
    ThrowIfDisposed();
    return AlpmList<Package>.Borrow(NativeMethods.alpm_trans_get_remove((_alpm_handle_t*)_library.AsHandle()),
      &Package.Factory);
  }

  /// <summary>
  /// Releases the transaction (and its database lock) deterministically.
  /// </summary>
  /// <remarks>
  /// Releasing also <b>frees every file-loaded package that was handed over</b> (see
  /// <see cref="AddPackage(LoadedPackage)"/>), so after this call no wrapper - and no borrowed view
  /// handed out earlier by <see cref="GetAddedPackages"/> - still points at a live package. Packages
  /// added through <see cref="AddPackage(Package)"/> are not affected: libalpm owns those.
  /// <para>
  /// There is deliberately no finalizer. An initialized transaction is released by
  /// <see cref="Alpm.Dispose()"/> before it releases the handle, because <c>alpm_release</c> does
  /// <b>not</b> release an active transaction: it answers <c>ALPM_ERR_TRANS_NOT_NULL</c> and frees
  /// nothing, which leaks the handle and <c>db.lck</c>. A finalizer here would run after that
  /// release, with the handle already gone, and an exception escaping it terminates the process
  /// (that is what <c>~Transactions()</c> used to do through <see cref="Alpm.AsHandle"/>'s disposed
  /// check).
  /// </para>
  /// <para>
  /// <see cref="GC.SuppressFinalize"/> is still called, so a derived type that adds its own
  /// finalizer does not have to override <see cref="Dispose"/> to suppress it: the native
  /// transaction this instance owns is already released by the time it would run.
  /// </para>
  /// </remarks>
  public unsafe void Dispose()
  {
    if (_released) return;

    _released = true;

    GC.SuppressFinalize(this);

    if (_library.Disposed) return;

    // The library is still alive, so release the native transaction - which drops the database lock
    // - and clear the handle's reference to it, so Alpm.Dispose finds nothing left to release.
    _ = NativeMethods.alpm_trans_release((_alpm_handle_t*)_library.AsHandle());
    _library.CurrentTransaction = null;
  }
}
