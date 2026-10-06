using System.Diagnostics.CodeAnalysis;
using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.List;
namespace Pacpar.Alpm;

/// <summary>
/// A package libalpm owns: a database package, a transaction member, or one reached through a group.
/// </summary>
/// <remarks>
/// A package view is a lightweight reference to memory owned by libalpm.
/// If you need package information to persist after the database, transaction, or ALPM handle is disposed,
/// call <see cref="PackageBase.ToSnapshot"/> to create an independent managed snapshot.
/// </remarks>
public sealed unsafe class PackageView : PackageBase
{
  /// <param name="backingStruct">The libalpm-owned package. Not dereferenced here.</param>
  /// <param name="lifetime">Domain of the context that owns the package memory, if any.</param>
  internal PackageView(_alpm_pkg_t* backingStruct, Lifetime? lifetime) : base(backingStruct)
  {
    Lifetime = lifetime?.Capture();
  }

  internal static PackageView Factory(void* ptr, Lifetime? lifetime) => new((_alpm_pkg_t*)ptr, lifetime);

  /// <summary>
  /// Finds a package satisfying the specified dependency in a collection of packages.
  /// </summary>
  /// <param name="packages">The packages to search.</param>
  /// <param name="dependency">The dependency query string (e.g. <c>"glibc&gt;=2.30"</c>).</param>
  /// <returns>The matching <see cref="PackageView"/>, or <c>null</c> if no satisfier was found.</returns>
  public static PackageView? FindSatisfier(IEnumerable<PackageView> packages, string dependency)
  {
    ArgumentNullException.ThrowIfNull(packages);
    ArgumentNullException.ThrowIfNull(dependency);

    var pkgList = packages.ToList();
    Span<byte> scratch = stackalloc byte[64];
    using var depBuf = new Utf8Buffer(dependency, scratch);
    _alpm_list_t* nativeList = null;
    try
    {
      nativeList = AlpmNativeList.BuildPointerList(pkgList.Select(p => (nint)p.BackingStruct));
      var matchedPtr = NativeMethods.alpm_find_satisfier(nativeList, depBuf.Ptr);
      if (matchedPtr == null) return null;

      // The match always belongs to the caller's own views - libalpm searches the list it was handed
      // - so returning one of those views keeps its domain. Fabricating a view here would issue it
      // with no domain at all, and reading it after the owning database was retired would silently
      // read freed memory instead of throwing AlpmLifetimeException.
      return pkgList.FirstOrDefault(p => p.BackingStruct == matchedPtr)
        ?? throw new InvalidOperationException(
          "libalpm returned a package that is not among the ones passed to the call; " +
          "there is no domain to anchor the resulting view to.");
    }
    finally
    {
      if (nativeList != null) NativeMethods.alpm_list_free(nativeList);
    }
  }

  /// <summary>
  /// The failure-returning seam for <see cref="SetReason"/>.
  /// </summary>
  internal bool TrySetReason(PackageReason reason, [NotNullWhen(false)] out AlpmFailure? failure)
  {
    ThrowIfDisposed();
    var err = NativeMethods.alpm_pkg_set_reason(BackingStruct, (_alpm_pkgreason_t)reason);
    if (err != 0)
    {
      var dbPtr = NativeMethods.alpm_pkg_get_db(BackingStruct);
      var handlePtr = dbPtr != null ? NativeMethods.alpm_db_get_handle(dbPtr) : null;
      var rawErrno = handlePtr != null ? (int)NativeMethods.alpm_errno(handlePtr) : 0;
      GC.KeepAlive(this);
      failure = NativeCall.Failure(rawErrno, "set package install reason");
      return false;
    }

    GC.KeepAlive(this);
    failure = null;
    return true;
  }

  /// <summary>
  /// Sets the install reason for this package in the local database.
  /// </summary>
  /// <param name="reason">The reason for installation.</param>
  public void SetReason(PackageReason reason)
  {
    if (!TrySetReason(reason, out var failure))
    {
      throw failure.ToException();
    }
  }

  /// <summary>
  /// Gets or sets why this package was installed on the system.
  /// </summary>
  public PackageReason InstallReason
  {
    get => Reason;
    set => SetReason(value);
  }

  /// <summary>
  /// Checks whether a newer version of this package is available in the given synchronization databases.
  /// </summary>
  /// <param name="syncDatabases">The databases to check for upgrades.</param>
  /// <returns>A <see cref="PackageView"/> for the newer package, or <c>null</c> if no upgrade is available.</returns>
  public PackageView? GetNewVersion(IEnumerable<Database> syncDatabases)
  {
    ArgumentNullException.ThrowIfNull(syncDatabases);
    ThrowIfDisposed();

    var dbs = syncDatabases.ToList();
    _alpm_list_t* dbsList = null;
    try
    {
      dbsList = AlpmNativeList.BuildPointerList(dbs.Select(d => (nint)d.ValidatedPtr));
      var newPkgPtr = NativeMethods.alpm_sync_get_new_version(BackingStruct, dbsList);
      if (newPkgPtr == null) return null;

      var dbPtr = NativeMethods.alpm_pkg_get_db(newPkgPtr);
      // Resolved among the caller's wrappers rather than the handle registry, for the same reason
      // Alpm.TryFindSatisfier does: see Database.ResolveLifetime.
      return new PackageView(newPkgPtr, Database.ResolveLifetime(dbs, dbPtr));
    }
    finally
    {
      if (dbsList != null) NativeMethods.alpm_list_free(dbsList);
    }
  }
}
