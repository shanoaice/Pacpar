using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers <see cref="PackageView.GetNewVersion"/>, which searches only the databases it is handed.
/// </summary>
public sealed class PackageUpgradeTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void PackageView_GetNewVersion_ReturnsNullWhenNoSyncDatabasesHaveNewer()
  {
    var pkg = _environment.Install("test-upgrade-check");
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("test-sync-empty", 0);

    var upgrade = pkg.GetNewVersion([syncDb]);
    Assert.Null(upgrade);
  }
}
