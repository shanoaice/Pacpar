using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Covers the transaction failure contract: <see cref="Transactions.Prepare"/> and
/// <see cref="Transactions.Commit"/> turn the caller-owned list libalpm dumps into their output
/// parameter into a typed <see cref="AlpmTransactionException"/>, with the payload shape and the
/// element destructor that the errno actually has.
/// </summary>
/// <remarks>
/// The failures are produced for real, from packages that <see cref="PackageArchive"/> synthesizes:
/// a <c>.pkg.tar</c> holding a <c>.PKGINFO</c> (plus the odd file entry) is accepted by
/// <c>alpm_pkg_load</c> even without <c>.MTREE</c>, so none of this needs the integration container. The conflicting-dependency case
/// is the regression for the worst of the old behaviour: the wrapper released that payload with
/// <c>alpm_depmissing_free</c>, which aborts the process with <c>free(): invalid pointer</c> (probe
/// <c>.dsh-scratch/audit-probe/own.c</c>) instead of reporting anything.
/// </remarks>
public sealed unsafe class TransactionFailureTests : IDisposable
{
  /// <summary>No flags: dependency and conflict checks stay on, which is what these tests exercise.</summary>
  private const TransactionFlags NoFlags = (TransactionFlags)0;

  private readonly string _workspaceRoot;
  private readonly string _root;
  private readonly string _packageDirectory;
  private readonly Alpm _alpm;

  public TransactionFailureTests()
  {
    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-transaction-tests", Guid.NewGuid().ToString("n"));
    _root = Path.Combine(_workspaceRoot, "root");
    var dbpath = Path.Combine(_workspaceRoot, "var", "lib", "pacman");
    _packageDirectory = Path.Combine(_workspaceRoot, "packages");

    Directory.CreateDirectory(_root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(_root, "tmp"));
    Directory.CreateDirectory(Path.Combine(_root, "var", "cache", "pacman", "pkg"));
    Directory.CreateDirectory(_packageDirectory);

    _alpm = new Alpm(_root, dbpath);
  }

  public void Dispose()
  {
    _alpm.Dispose();

    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }

  /// <summary>
  /// Loads a package and hands it to <paramref name="transaction"/>, which takes over the release.
  /// </summary>
  /// <remarks>
  /// The wrapper is retired by the hand-over, so nothing here has to remember to free it - which is
  /// exactly what makes the calling pattern of these tests safe.
  /// </remarks>
  private void AddTo(Transactions transaction, string path)
  {
    var package = _alpm.LoadPackage(path, full: true, SigLevel.ALPM_SIG_USE_DEFAULT);
    transaction.AddPackage(package);
  }

  [Fact]
  public void Prepare_WithAnUnsatisfiedDependency_ReportsTheMissingDependency()
  {
    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-needs", depend: "missing-xyz"));

    var failure = Assert.Throws<AlpmTransactionException.MissingDependencies>(() => transaction.Prepare());

    Assert.Equal(_alpm_errno_t.ALPM_ERR_UNSATISFIED_DEPS, failure.Errno);
    Assert.Contains("Failed to prepare transaction", failure.Message);

    var missing = Assert.Single(failure.Dependencies);
    Assert.Equal("audit-needs", missing.Target);
    Assert.Equal("missing-xyz", missing.Depend?.Name);
  }

  /// <summary>
  /// The conflict payload is a list of <c>alpm_conflict_t</c>, not of <c>alpm_depmissing_t</c>.
  /// Reaching the assertions at all is half of the test: the previous wrapper released that list with
  /// <c>alpm_depmissing_free</c>, which is a SIGABRT on the spot.
  /// </summary>
  [Fact]
  public void Prepare_WithConflictingPackages_ReportsTheConflictWithoutAborting()
  {
    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-left"));
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-right", conflict: "audit-left"));

    var failure = Assert.Throws<AlpmTransactionException.ConflictingDependencies>(() => transaction.Prepare());

    Assert.Equal(_alpm_errno_t.ALPM_ERR_CONFLICTING_DEPS, failure.Errno);

    var conflict = Assert.Single(failure.Conflicts);
    Assert.Equal(["audit-left", "audit-right"],
      new[] { conflict.Package1Name, conflict.Package2Name }.Order());
    Assert.Equal("audit-left", conflict.Reason.Name);
  }

  [Fact]
  public void Prepare_WithAForeignArchitecture_ReportsThePackageNames()
  {
    _alpm.Options.Architectures.Add("aarch64");

    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-arch", arch: "notarealarchitecture"));

    var failure = Assert.Throws<AlpmTransactionException.InvalidPackageArchitecture>(() => transaction.Prepare());

    Assert.Equal(_alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH, failure.Errno);

    // libalpm names the offending packages itself as pkgname-pkgver-pkgarch - not the file they were
    // loaded from.
    Assert.Equal([$"audit-arch-{PackageArchive.Version}-notarealarchitecture"], failure.Packages);
  }

  /// <summary>A payload is a list, not a single value: two rejected packages are both reported.</summary>
  [Fact]
  public void Prepare_WithSeveralForeignArchitectures_ReportsAllOfThem()
  {
    _alpm.Options.Architectures.Add("aarch64");

    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-arch-one", arch: "notarealarchitecture"));
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-arch-two", arch: "notarealarchitecture"));

    var failure = Assert.Throws<AlpmTransactionException.InvalidPackageArchitecture>(() => transaction.Prepare());

    Assert.Equal(
      [$"audit-arch-one-{PackageArchive.Version}-notarealarchitecture", $"audit-arch-two-{PackageArchive.Version}-notarealarchitecture"],
      failure.Packages);
  }

  [Fact]
  public void Commit_WithAFileConflict_ReportsTheFile()
  {
    var owned = Path.Combine(_root, "usr", "bin", "probe-file");
    Directory.CreateDirectory(Path.GetDirectoryName(owned)!);
    System.IO.File.WriteAllText(owned, "already on disk, owned by nobody");

    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-file", file: "usr/bin/probe-file"));

    transaction.Prepare();

    var failure = Assert.Throws<AlpmTransactionException.ConflictingFiles>(() => transaction.Commit());

    Assert.Equal(_alpm_errno_t.ALPM_ERR_FILE_CONFLICTS, failure.Errno);
    Assert.Contains("Failed to commit transaction", failure.Message);

    var conflict = Assert.Single(failure.Conflicts);
    Assert.Equal("audit-file", conflict.Target);
    Assert.EndsWith(Path.Combine("usr", "bin", "probe-file"), conflict.File);
  }

  /// <summary>
  /// The other half of the contract: a sound transaction reports nothing at all - which is why both
  /// methods answer <c>void</c>. The success paths of <c>alpm_trans_prepare</c>/<c>alpm_trans_commit</c>
  /// leave the output parameter <c>null</c>, so there is no payload to return.
  /// </summary>
  [Fact]
  public void Prepare_And_Commit_ReportNothingForASoundTransaction()
  {
    var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-ok", file: "usr/bin/probe-file"));

    transaction.Prepare();
    transaction.Commit();
    transaction.Dispose();

    Assert.NotNull(_alpm.GetLocalDatabase().GetPackage("audit-ok"));
  }

  /// <summary>
  /// An errno whose output parameter libalpm left empty still has to produce a usable exception,
  /// carrying the errno and libalpm's own message. <c>Commit</c> before <c>Prepare</c> is the
  /// reachable case: <c>ALPM_ERR_TRANS_NOT_PREPARED</c> with <c>data == null</c>.
  /// </summary>
  [Fact]
  public void Commit_BeforePrepare_ReportsTheBaseState()
  {
    using var transaction = _alpm.BeginTransaction(NoFlags);
    AddTo(transaction, PackageArchive.Create(_packageDirectory, "audit-early"));

    var failure = Assert.Throws<AlpmTransactionException>(() => transaction.Commit());

    Assert.Equal(typeof(AlpmTransactionException), failure.GetType());
    Assert.Equal(_alpm_errno_t.ALPM_ERR_TRANS_NOT_PREPARED, failure.Errno);
    Assert.Contains("Failed to commit transaction", failure.Message);
  }

  /// <summary>
  /// The factory's contract for an errno it does not know: the list is consumed, but its elements are
  /// left alone - the pointer below must never be dereferenced, because libalpm could have put
  /// anything there, and guessing the destructor is what aborts the process.
  /// </summary>
  [Fact]
  public void TakeFailure_WithAnUnknownErrno_ConsumesTheListWithoutTouchingTheElements()
  {
    var list = NativeMethods.alpm_list_add(null, (void*)0x1);

    var failure = AlpmTransactionException.TakeFailure(_alpm_errno_t.ALPM_ERR_TRANS_NULL, list);

    Assert.Equal(typeof(AlpmTransactionException), failure.GetType());
    Assert.Equal(_alpm_errno_t.ALPM_ERR_TRANS_NULL, failure.Errno);
  }

  [Fact]
  public void Prepare_AfterDispose_ThrowsObjectDisposed()
  {
    var transaction = _alpm.BeginTransaction(NoFlags);
    transaction.Dispose();

    Assert.Throws<ObjectDisposedException>(() => transaction.Prepare());
  }
}
