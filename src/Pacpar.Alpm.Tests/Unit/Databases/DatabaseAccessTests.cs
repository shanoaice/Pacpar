using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Databases;

/// <summary>
/// The two ways a session hands out a database: the local database it always has, and the sync
/// databases a caller registers. These are the baseline every other database test starts from.
/// </summary>
public sealed class DatabaseAccessTests
{
  [Fact]
  public void GetLocalDatabase_ReturnsLocalDatabase()
  {
    using var environment = new IsolatedAlpmEnvironment();

    Assert.Equal("local", environment.AlpmSession.GetLocalDatabase().Name);
  }

  [Fact]
  public void GetSyncDatabases_ReturnsEmptyList_WhenNoSyncDatabasesRegistered()
  {
    using var environment = new IsolatedAlpmEnvironment();

    Assert.Empty(environment.AlpmSession.GetSyncDatabases());
  }
}
