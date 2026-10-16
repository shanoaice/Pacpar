using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Databases;

/// <summary>
/// Covers <see cref="Database.Update"/>: it reports the failure libalpm gives it, and it retires the
/// views it issued before that - both halves matter, because a failed update leaves the database's
/// contents changed all the same.
/// </summary>
public sealed class DatabaseUpdateTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void Database_Update_WithoutServers_FailsAndReportsError()
  {
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("test-sync-update", 0);

    var ok = syncDb.TryUpdate(force: false, out var updated, out var failure);
    Assert.False(ok);
    Assert.False(updated);
    Assert.NotNull(failure);

    Assert.ThrowsAny<AlpmException>(() => syncDb.Update());
  }

  [Fact]
  public void Database_Update_InvalidatesPriorPackageViews()
  {
    var pkg = _environment.Install("test-invalidation-pkg");
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    // Calling InvalidateViews retires all views issued under localDb
    localDb.InvalidateViews("Database.Update");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => pkg.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Database.Update", thrown.InvalidatedBy);
  }
}
