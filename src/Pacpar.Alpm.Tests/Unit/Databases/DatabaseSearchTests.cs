using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Databases;

/// <summary>
/// Covers <see cref="Database.Search"/>: libalpm matches a package when any of the needles is a
/// substring of its name, and answers with a list the caller owns.
/// </summary>
public sealed class DatabaseSearchTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void Database_Search_FindsMatchingPackages()
  {
    _environment.Install("test-search-alpha");
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    var results = localDb.Search(["test-search-alpha"]);
    Assert.Contains(results, p => p.Name == "test-search-alpha");

    var emptyResults = localDb.Search(["no-match-xyz-987"]);
    Assert.Empty(emptyResults);
  }
}
