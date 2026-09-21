using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.3: server and cache-server administration on a sync database.
/// </summary>
/// <remarks>
/// The return conventions here are the opposite of the <c>alpm_option_remove_*</c> family: these
/// return <c>0</c> when they removed the entry and <c>1</c> when it was not there (measured with
/// <c>.dsh-scratch/assume-probe/dbserver3.c</c>). The tests below pin that in the wrapper's own
/// vocabulary.
/// </remarks>
public sealed class DatabaseServerTests
{
  private const string Url = "https://example.invalid/$repo/os/$arch";
  private const string OtherUrl = "https://other.invalid/$repo/os/$arch";

  private static Database RegisterSync(Alpm alpm) => alpm.RegisterSyncDatabase("pacpar-test", (SigLevel)0);

  [Fact]
  public void Servers_StartEmpty()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    Assert.Empty(db.Servers);
    Assert.Empty(db.CacheServers);
  }

  [Fact]
  public void AddServer_ThenRemoveServer_RoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    db.AddServer(Url);

    Assert.Equal([Url], db.Servers);
    Assert.True(db.RemoveServer(Url), "removing a server that is there must answer true");
    Assert.Empty(db.Servers);
    Assert.False(db.RemoveServer(Url), "removing a server that is not there must answer false");
  }

  /// <summary>Measured: libalpm does not deduplicate, the list simply grows.</summary>
  [Fact]
  public void AddServer_DoesNotDeduplicate()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    db.AddServer(Url);
    db.AddServer(Url);

    Assert.Equal(2, db.Servers.Count);
  }

  [Fact]
  public void SetServers_ReplacesTheWholeList()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);
    db.AddServer(Url);

    db.SetServers([OtherUrl]);

    Assert.Equal([OtherUrl], db.Servers);
  }

  /// <summary>
  /// libalpm dups the list it is handed ("the list will be duped and the original will still need
  /// to be freed by the caller"), so the wrapper is free to release its own buffers immediately.
  /// Forcing a collection and reading the servers back is what proves the wrapper did not keep
  /// pointing at memory it owns.
  /// </summary>
  [Fact]
  public void SetServers_CopiesTheStrings_LibalpmDoesNotAliasOurBuffers()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    db.SetServers([Url, OtherUrl]);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.Equal([Url, OtherUrl], db.Servers);
  }

  [Fact]
  public void CacheServers_BehaveTheSameWayAsServers()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    db.AddCacheServer(Url);
    Assert.Equal([Url], db.CacheServers);

    Assert.True(db.RemoveCacheServer(Url));
    Assert.Empty(db.CacheServers);
    Assert.False(db.RemoveCacheServer(Url));

    db.SetCacheServers([OtherUrl]);
    Assert.Equal([OtherUrl], db.CacheServers);
  }

  /// <summary>The two lists are independent: writing one must not touch the other.</summary>
  [Fact]
  public void ServersAndCacheServers_AreIndependent()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = RegisterSync(environment.Alpm);

    db.AddServer(Url);
    db.AddCacheServer(OtherUrl);

    Assert.Equal([Url], db.Servers);
    Assert.Equal([OtherUrl], db.CacheServers);
  }
}
