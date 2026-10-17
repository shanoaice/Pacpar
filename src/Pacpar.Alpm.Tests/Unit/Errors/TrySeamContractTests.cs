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

  public void Dispose() => _environment.Dispose();

  private string PackageDirectory => _environment.PackageDirectory;

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
      var ok = AlpmSession.TryCreate(root, dbpath, out var session, out var failure);
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
    var ok = AlpmSession.TryCreate("/dev/null/not/a/dir", "/dev/null/not/a/dir", out var session, out var failure);
    Assert.False(ok);
    Assert.Null(session);
    Assert.NotNull(failure);
    Assert.NotEqual(0, failure.NativeCode);
    Assert.NotEqual(AlpmFailureCode.UnknownNative, failure.Code);
  }

  [Fact]
  public void TryLoadPackage_Success_SetsPackageAndNullFailure()
  {
    var path = PackageArchive.Create(PackageDirectory, "audit-load-pkg");
    var ok = _environment.AlpmSession.TryLoadPackage(path, full: true, SigLevel.UseDefault, out var pkg, out var failure);

    Assert.True(ok);
    Assert.NotNull(pkg);
    Assert.Null(failure);
    pkg.Dispose();
  }

  [Fact]
  public void TryLoadPackage_Failure_SetsNullPackageAndFailure()
  {
    var ok = _environment.AlpmSession.TryLoadPackage("/nonexistent/file.pkg.tar.zst", full: false, SigLevel.UseDefault, out var pkg, out var failure);

    Assert.False(ok);
    Assert.Null(pkg);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.PackageNotFound, failure.Code);
  }

  [Fact]
  public void TryBeginTransaction_Success_SetsTransactionAndNullFailure()
  {
    var ok = _environment.AlpmSession.TryBeginTransaction(0, out var tx, out var failure);

    Assert.True(ok);
    Assert.NotNull(tx);
    Assert.Null(failure);
    tx.Dispose();
  }

  [Fact]
  public void TryRegisterSyncDatabase_Success_SetsDatabaseAndNullFailure()
  {
    var ok = _environment.AlpmSession.TryRegisterSyncDatabase("core_try", 0, out var db, out var failure);

    Assert.True(ok);
    Assert.NotNull(db);
    Assert.Null(failure);
  }

  [Fact]
  public void TryUnregisterAllSyncDatabases_Success_SetsNullFailure()
  {
    _environment.AlpmSession.RegisterSyncDatabase("extra_try", 0);
    var ok = _environment.AlpmSession.TryUnregisterAllSyncDatabases(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryLoadPackageCache_LocalDatabase_Success_SetsCacheAndNullFailure()
  {
    var local = _environment.AlpmSession.GetLocalDatabase();
    var ok = local.TryLoadPackageCache(out var cache, out var failure);

    Assert.True(ok);
    Assert.NotNull(cache);
    Assert.Null(failure);
  }

  [Fact]
  public void TryLoadGroupCache_LocalDatabase_Success_SetsCacheAndNullFailure()
  {
    var local = _environment.AlpmSession.GetLocalDatabase();
    var ok = local.TryLoadGroupCache(out var cache, out var failure);

    Assert.True(ok);
    Assert.NotNull(cache);
    Assert.Null(failure);
  }

  [Fact]
  public void TryLoadGroupCache_MissingSyncDb_Failure_SetsNullCacheAndFailure()
  {
    var sync = _environment.AlpmSession.RegisterSyncDatabase("sync_for_group_test", 0);
    var ok = sync.TryLoadGroupCache(out var cache, out var failure);

    Assert.False(ok);
    Assert.Null(cache);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.DatabaseNotFound, failure.Code);
  }

  [Fact]
  public void TryValidate_Success_SetsNullFailure()
  {
    var local = _environment.AlpmSession.GetLocalDatabase();
    var ok = local.TryValidate(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryUnregister_Success_SetsNullFailure()
  {
    var sync = _environment.AlpmSession.RegisterSyncDatabase("sync_unregister_test", 0);
    var ok = sync.TryUnregister(out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryPrepare_And_TryCommit_Success_SetsNullFailure()
  {
    var path = PackageArchive.Create(PackageDirectory, "audit-commit-success");
    using var tx = _environment.AlpmSession.BeginTransaction();
    tx.AddPackage(_environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault));

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
    using var tx = _environment.AlpmSession.BeginTransaction();
    var ok = tx.TryCommit(out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.TransactionNotPrepared, failure.Code);
  }

  [Fact]
  public void TrySystemUpgrade_Success_SetsNullFailure()
  {
    using var tx = _environment.AlpmSession.BeginTransaction();
    var ok = tx.TrySystemUpgrade(enableDowngrade: false, out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryInterrupt_OnIdleTransaction_Failure_SetsFailure()
  {
    using var tx = _environment.AlpmSession.BeginTransaction();
    var ok = tx.TryInterrupt(out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.TransactionType, failure.Code);
  }

  [Fact]
  public void TryAddPackage_LoadedPackage_Success_SetsViewAndNullFailure()
  {
    var path = PackageArchive.Create(PackageDirectory, "audit-add-loaded");
    using var tx = _environment.AlpmSession.BeginTransaction();
    var pkg = _environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault);

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
    var path = PackageArchive.Create(PackageDirectory, "audit-pkg-to-remove");
    using (var installer = _environment.AlpmSession.BeginTransaction())
    {
      installer.AddPackage(_environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault));
      installer.Prepare();
      installer.Commit();
    }

    var installed = _environment.AlpmSession.GetLocalDatabase().GetPackage("audit-pkg-to-remove");
    Assert.NotNull(installed);

    using var remover = _environment.AlpmSession.BeginTransaction();
    var ok = remover.TryRemovePackage(installed, out var failure);

    Assert.True(ok);
    Assert.Null(failure);
  }

  [Fact]
  public void TryAddPackage_PackageView_Failure_SetsNullFailureFalse()
  {
    // Install a package first
    var path = PackageArchive.Create(PackageDirectory, "audit-installed-add-fail");
    using (var installer = _environment.AlpmSession.BeginTransaction())
    {
      installer.AddPackage(_environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault));
      installer.Prepare();
      installer.Commit();
    }

    var installed = _environment.AlpmSession.GetLocalDatabase().GetPackage("audit-installed-add-fail");
    Assert.NotNull(installed);

    using var tx = _environment.AlpmSession.BeginTransaction();
    // Adding an already-installed package to a transaction is refused with ALPM_ERR_WRONG_ARGS
    var ok = tx.TryAddPackage(installed, out var failure);

    Assert.False(ok);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.WrongArguments, failure.Code);
  }

  [Fact]
  public void TryCheckMd5Sum_NonSyncPackage_FailsWithWrongArguments()
  {
    var path = PackageArchive.Create(PackageDirectory, "audit-checkmd5-pkg");
    var loaded = _environment.AlpmSession.LoadPackage(path, full: true, SigLevel.UseDefault);

    var ok = loaded.TryCheckMd5Sum(out var matches, out var failure);

    Assert.False(ok);
    Assert.False(matches);
    Assert.NotNull(failure);
    Assert.Equal(AlpmFailureCode.WrongArguments, failure.Code);
    Assert.ThrowsAny<AlpmException>(() => loaded.CheckMd5Sum());
  }
}
