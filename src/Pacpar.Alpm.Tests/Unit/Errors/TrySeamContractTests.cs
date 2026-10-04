using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Errors;

/// <summary>
/// Verifies the ADR 0013 Try* seam contract across all twin methods:
/// on success (true), failure is null and any result is non-null;
/// on failure (false), failure is non-null and any result is null.
/// </summary>
public sealed class TrySeamContractTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();
  private readonly string _packageDirectory;

  public TrySeamContractTests()
  {
    _packageDirectory = Path.Combine(Path.GetTempPath(), "pacpar-tryseam-tests", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_packageDirectory);
  }

  public void Dispose()
  {
    _environment.Dispose();
    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  [Fact]
  public void TryCreate_Success_SetsSessionAndNullFailure()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "pacpar-trycreate-success-" + Guid.NewGuid().ToString("n"));
    var root = Path.Combine(tempRoot, "root");
    var dbpath = Path.Combine(tempRoot, "db");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));

    try
    {
      var ok = Alpm.TryCreate(root, dbpath, out var session, out var failure);
      Assert.True(ok);
      Assert.NotNull(session);
      Assert.Null(failure);
      session.Dispose();
    }
    finally
    {
      if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
    }
  }

  [Fact]
  public void TryCreate_Failure_SetsNullSessionAndFailure()
  {
    var ok = Alpm.TryCreate("/dev/null/not/a/dir", "/dev/null/not/a/dir", out var session, out var failure);
    Assert.False(ok);
    Assert.Null(session);
    Assert.NotNull(failure);
    Assert.NotEqual(0, failure.NativeCode);
    Assert.NotEqual(AlpmFailureCode.UnknownNative, failure.Code);
  }

  [Fact]
  public void TryLoadPackage_Success_SetsPackageAndNullFailure()
  {
    var path = PackageArchive.Create(_packageDirectory, "audit-load-pkg");
    var ok = _environment.Alpm.TryLoadPackage(path, full: true, SigLevel.AlpmSigUseDefault, out var pkg, out var failure);

    Assert.True(ok);
    Assert.NotNull(pkg);
    Assert.Null(failure);
    pkg.Dispose();
  }

  [Fact]
  public void TryLoadPackage_Failure_SetsNullPackageAndFailure()
  {
    var ok = _environment.Alpm.TryLoadPackage("/nonexistent/file.pkg.tar.zst", full: false, SigLevel.AlpmSigUseDefault, out var pkg, out var failure);

    Assert.False(ok);
    Assert.Null(pkg);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.PackageNotFound, failure.Code);
  }

  [Fact]
  public void TryBeginTransaction_Success_SetsTransactionAndNullFailure()
  {
    var ok = _environment.Alpm.TryBeginTransaction(0, out var tx, out var failure);

    Assert.True(ok);
    Assert.NotNull(tx);
    Assert.Null(failure);
    tx.Dispose();
  }

  [Fact]
  public void TryRegisterSyncDatabase_Success_SetsDatabaseAndNullFailure()
  {
    var ok = _environment.Alpm.TryRegisterSyncDatabase("core_try", 0, out var db, out var failure);

    Assert.True(ok);
    Assert.NotNull(db);
    Assert.Null(failure);
  }

  [Fact]
  public void TryUnregisterAllSyncDatabases_Success_SetsNullFailure()
  {
    _environment.Alpm.RegisterSyncDatabase("extra_try", 0);
    var ok = _environment.Alpm.TryUnregisterAllSyncDatabases(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryGetPackageCache_LocalDatabase_Success_SetsCacheAndNullFailure()
  {
    var local = _environment.Alpm.GetLocalDatabase();
    var ok = local.TryGetPackageCache(out var cache, out var failure);

    Assert.True(ok);
    Assert.NotNull(cache);
    Assert.Null(failure);
  }

  [Fact]
  public void TryGetGroupCache_LocalDatabase_Success_SetsCacheAndNullFailure()
  {
    var local = _environment.Alpm.GetLocalDatabase();
    var ok = local.TryGetGroupCache(out var cache, out var failure);

    Assert.True(ok);
    Assert.NotNull(cache);
    Assert.Null(failure);
  }

  [Fact]
  public void TryGetGroupCache_MissingSyncDb_Failure_SetsNullCacheAndFailure()
  {
    var sync = _environment.Alpm.RegisterSyncDatabase("sync_for_group_test", 0);
    var ok = sync.TryGetGroupCache(out var cache, out var failure);

    Assert.False(ok);
    Assert.Null(cache);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.DatabaseNotFound, failure.Code);
  }

  [Fact]
  public void TryValidate_Success_SetsNullFailure()
  {
    var local = _environment.Alpm.GetLocalDatabase();
    var ok = local.TryValidate(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryUnregister_Success_SetsNullFailure()
  {
    var sync = _environment.Alpm.RegisterSyncDatabase("sync_unregister_test", 0);
    var ok = sync.TryUnregister(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryPrepare_And_TryCommit_Success_SetsNullFailure()
  {
    var path = PackageArchive.Create(_packageDirectory, "audit-commit-success");
    using var tx = _environment.Alpm.BeginTransaction();
    tx.AddPackage(_environment.Alpm.LoadPackage(path, full: true, SigLevel.AlpmSigUseDefault));

    var okPrepare = tx.TryPrepare(out var failurePrepare);
    Assert.True(okPrepare);
    Assert.Null(failurePrepare);

    var okCommit = tx.TryCommit(out var failureCommit);
    Assert.True(okCommit);
    Assert.Null(failureCommit);
  }

  [Fact]
  public void TryCommit_UnpreparedTransaction_Failure_SetsFailure()
  {
    using var tx = _environment.Alpm.BeginTransaction();
    var ok = tx.TryCommit(out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.TransactionNotPrepared, failure.Code);
  }

  [Fact]
  public void TrySystemUpgrade_Success_SetsNullFailure()
  {
    using var tx = _environment.Alpm.BeginTransaction();
    var ok = tx.TrySystemUpgrade(enableDowngrade: false, out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryInterrupt_OnIdleTransaction_Failure_SetsFailure()
  {
    using var tx = _environment.Alpm.BeginTransaction();
    var ok = tx.TryInterrupt(out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.TransactionType, failure.Code);
  }

  [Fact]
  public void TryAddPackage_LoadedPackage_Success_SetsViewAndNullFailure()
  {
    var path = PackageArchive.Create(_packageDirectory, "audit-add-loaded");
    using var tx = _environment.Alpm.BeginTransaction();
    var pkg = _environment.Alpm.LoadPackage(path, full: true, SigLevel.AlpmSigUseDefault);

    var ok = tx.TryAddPackage(pkg, out var view, out var failure);

    Assert.True(ok);
    Assert.NotNull(view);
    Assert.Null(failure);
    Assert.False(pkg.OwnsPackage);
  }

  [Fact]
  public void TryRemovePackage_Success_SetsNullFailure()
  {
    // Install a package first
    var path = PackageArchive.Create(_packageDirectory, "audit-pkg-to-remove");
    using (var installer = _environment.Alpm.BeginTransaction())
    {
      installer.AddPackage(_environment.Alpm.LoadPackage(path, full: true, SigLevel.AlpmSigUseDefault));
      installer.Prepare();
      installer.Commit();
    }

    var installed = _environment.Alpm.GetLocalDatabase().GetPackage("audit-pkg-to-remove");
    Assert.NotNull(installed);

    using var remover = _environment.Alpm.BeginTransaction();
    var ok = remover.TryRemovePackage(installed, out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryAddPackage_PackageView_Failure_SetsNullFailureFalse()
  {
    // Install a package first
    var path = PackageArchive.Create(_packageDirectory, "audit-installed-add-fail");
    using (var installer = _environment.Alpm.BeginTransaction())
    {
      installer.AddPackage(_environment.Alpm.LoadPackage(path, full: true, SigLevel.AlpmSigUseDefault));
      installer.Prepare();
      installer.Commit();
    }

    var installed = _environment.Alpm.GetLocalDatabase().GetPackage("audit-installed-add-fail");
    Assert.NotNull(installed);

    using var tx = _environment.Alpm.BeginTransaction();
    // Adding an already-installed package to a transaction is refused with ALPM_ERR_WRONG_ARGS
    var ok = tx.TryAddPackage(installed, out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.WrongArguments, failure.Code);
  }
}
