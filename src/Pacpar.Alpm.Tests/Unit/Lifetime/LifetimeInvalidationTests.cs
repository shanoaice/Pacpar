using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// End-to-end coverage of the invalidation sites the lifetime-token design specifies: releasing a
/// loaded package, disposing the handle, and committing a transaction must retire exactly the views
/// issued under the affected token, and every later read must answer
/// <see cref="AlpmLifetimeException"/> instead of touching freed memory.
/// </summary>
/// <remarks>
/// Every test runs against an isolated throwaway root/dbpath (see <see cref="IsolatedAlpmEnvironment"/>),
/// so no project container and no system database are involved.
/// </remarks>
public sealed class LifetimeInvalidationTests : IDisposable
{
  /// <summary>No flags: the tests only care about lifetimes, but the transaction must still be sound.</summary>
  private const TransactionFlags NoFlags = (TransactionFlags)0;

  private readonly IsolatedAlpmEnvironment _environment = new();

  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-lifetime-packages", Guid.NewGuid().ToString("n"));

  public LifetimeInvalidationTests() => Directory.CreateDirectory(_packageDirectory);

  public void Dispose()
  {
    _environment.Dispose();

    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private string Archive(string name, string? depend = null)
    => PackageArchive.Create(_packageDirectory, name, depend: depend);

  private void Install(string name, string? depend = null)
  {
    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);
    transaction.AddPackage(_environment.Alpm.LoadPackage(Archive(name, depend),
      full: true, SigLevel.ALPM_SIG_USE_DEFAULT));
    transaction.Prepare();
    transaction.Commit();
  }

  [Fact]
  public void LoadedPackageDispose_InvalidatesTheViewsItIssued()
  {
    var pkg = _environment.Alpm.LoadPackage(Archive("audit-lifetime-dep", depend: "glibc"),
      full: true, SigLevel.ALPM_SIG_USE_DEFAULT);

    var depends = pkg.Depends;
    var copied = depends.ToArray();
    Assert.Equal("glibc", copied[0].Name);

    pkg.Dispose();

    // The wrapper itself reports retirement through ObjectDisposedException (its flag wins)...
    Assert.Throws<ObjectDisposedException>(() => pkg.Name);

    // ...while the borrowed list, which carries the package's root token, reports the release
    // through AlpmLifetimeException instead of reading the freed dependency list.
    var thrown = Assert.Throws<AlpmLifetimeException>(() => depends.ToArray());
    Assert.Equal("a loaded package", thrown.Target);
    Assert.Equal("LoadedPackage.Dispose()", thrown.InvalidatedBy);

    // The materialized Depend snapshots are detached copies and outlive the package.
    Assert.Equal("glibc", copied[0].Name);
  }

  [Fact]
  public void HandOver_InvalidatesTheWrapperAndTheReturnedViewCarriesTheTransaction()
  {
    var pkg = _environment.Alpm.LoadPackage(Archive("audit-lifetime-handoff"),
      full: true, SigLevel.ALPM_SIG_USE_DEFAULT);

    using var transaction = _environment.Alpm.BeginTransaction(NoFlags);
    var view = transaction.AddPackage(pkg);

    Assert.Equal("audit-lifetime-handoff", view.Name);

    // The wrapper is inert with the hand-over's own reason...
    Assert.False(pkg.OwnsPackage);
    Assert.Throws<ObjectDisposedException>(() => pkg.Name);

    // ...and the view handed back is now guarded by the transaction: releasing the transaction
    // (done by the using above on method exit) retires it through the parent chain as well.
    transaction.Prepare();
    transaction.Commit();
  }

  [Fact]
  public void HandleDispose_InvalidatesLocalDatabaseViews()
  {
    var cache = _environment.Alpm.GetLocalDatabase().GetPackageCache();

    _environment.Alpm.Dispose();

    var thrown = Assert.Throws<AlpmLifetimeException>(() => { foreach (var _ in cache) { } });
    Assert.Equal("the ALPM handle", thrown.Target);
    Assert.Equal("Alpm.Dispose()", thrown.InvalidatedBy);

    // The token chain also guards ToArray on a stale-but-empty list (null native pointer).
    Assert.Throws<AlpmLifetimeException>(() => cache.ToArray());
  }

  [Fact]
  public void Commit_RetiresTheLocalDatabaseViewsHeldAcrossIt()
  {
    Install("audit-lifetime-first");

    // A view obtained after the first commit, under the fresh token that commit issued.
    var localDb = _environment.Alpm.GetLocalDatabase();
    var held = localDb.GetPackage("audit-lifetime-first");
    Assert.NotNull(held);
    Assert.Equal("audit-lifetime-first", held!.Name);

    // The second commit rewrites the local database caches again.
    Install("audit-lifetime-second");

    // The view held across the commit now reports the commit as its executioner...
    var thrown = Assert.Throws<AlpmLifetimeException>(() => held.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Transaction.Commit()", thrown.InvalidatedBy);

    // ...as does the Database wrapper issued before the commit (same retired token)...
    Assert.Throws<AlpmLifetimeException>(() => localDb.Name);
    Assert.Throws<AlpmLifetimeException>(() => localDb.GetPackage("audit-lifetime-first"));

    // ...while a fresh GetLocalDatabase() starts over with a new token and answers normally.
    var fresh = _environment.Alpm.GetLocalDatabase();
    var reissued = fresh.GetPackage("audit-lifetime-first");
    Assert.NotNull(reissued);
    Assert.Equal("audit-lifetime-first", reissued!.Name);
  }
}
