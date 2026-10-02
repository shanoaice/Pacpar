using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// libalpm's errno is sticky per handle, and whether a given entry point resets it on entry varies
/// function by function. These tests pin the wrapper against both halves of that: the two server
/// getters, which must not consult errno at all, and the two cache getters, which may - and do - rely
/// on the fact that their entry points clear it.
/// </summary>
/// <remarks>
/// Measured against libalpm 16.0.1: after a query miss leaves <c>ALPM_ERR_PKG_NOT_FOUND</c> on the
/// handle, <c>alpm_db_get_servers</c> succeeds and leaves the errno exactly as it found it. The
/// wrapper used to probe it there, so a successful server query threw the earlier package error.
/// </remarks>
public sealed class StickyErrnoTests
{
  private static Database RegisterSync(Alpm alpm) => alpm.RegisterSyncDatabase("pacpar-sticky", (SigLevel)0);

  /// <summary>
  /// Leaves a non-OK errno on the handle: <c>alpm_db_get_pkg</c> sets
  /// <c>ALPM_ERR_PKG_NOT_FOUND</c> on a miss and nothing clears it afterwards.
  /// </summary>
  private static void LeaveAnErrnoBehind(Database database) =>
    Assert.Null(database.GetPackage("definitely-not-installed"));

  [Fact]
  public void ServerGetters_DoNotThrow_AfterAFailedQuery()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    LeaveAnErrnoBehind(db);

    // Before the fix this threw AlpmPackageException: PKG_NOT_FOUND, from a call that in fact
    // succeeded. The list itself was always correct; only the errno probe was wrong.
    Assert.Empty(db.GetServers());
    Assert.Empty(db.GetCacheServers());
  }

  [Fact]
  public void ServerGetters_ReadTheSame_WhateverFailedBefore()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    var withCleanErrno = db.GetServers().ToArray();

    LeaveAnErrnoBehind(db);
    var withStaleErrno = db.GetServers().ToArray();

    // The result must be a function of the database, not of the handle's error history.
    Assert.Equal(withCleanErrno, withStaleErrno);
  }

  [Fact]
  public void DatabaseGetters_DoNotThrow_WhenAnEarlierCallLeftAnErrno()
  {
    using var environment = new IsolatedAlpmEnvironment();
    LeaveAnErrnoBehind(environment.Alpm.GetLocalDatabase());

    // alpm_get_localdb and alpm_get_syncdbs have no failure channel at all, so there is nothing for
    // an errno probe to contribute. They reset the handle errno on entry, which is why they happened
    // to look safe - the point is that they no longer depend on that.
    Assert.NotNull(environment.Alpm.GetLocalDatabase());
    Assert.Empty(environment.Alpm.GetSyncDatabases());
  }

  [Fact]
  public void CacheGetter_StillReportsAFailure_ThatIsOnTheHandle()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    // The other half of the rule: alpm_db_get_pkgcache clears pm_errno in its first statement, so a
    // non-OK value read afterwards necessarily belongs to that call and a missing database file must
    // still surface instead of turning into an empty cache.
    Assert.ThrowsAny<Exception>(() => db.GetPackageCache());
  }

  [Fact]
  public void NativeFailureWithoutAnErrno_HasItsOwnType()
  {
    var exception = new AlpmNativeFailureException("unregister database");

    Assert.IsAssignableFrom<AlpmException>(exception);
    Assert.Equal("unregister database", exception.Operation);
    Assert.Equal(0, exception.Errno);
    Assert.Contains("without setting an error code", exception.Message);
  }
}
