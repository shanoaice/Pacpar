using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Databases;

/// <summary>
/// Coverage ledger §4: <c>alpm_db_usage_t</c> exposed as <see cref="DatabaseUsage"/>.
/// </summary>
public sealed class DatabaseUsageTests
{
  /// <summary>
  /// Pins the bit values against <c>alpm.h</c>. This is not in <see cref="ManagedEnumsTests"/> because
  /// the bindings project <c>alpm_db_usage_t</c> as a plain <c>uint</c>, so there is no native enum to
  /// mirror - the wrapper's declaration is the only one there is.
  /// </summary>
  [Fact]
  public void DatabaseUsage_DeclaresTheNativeFlags()
  {
    Assert.Equal(1u, (uint)DatabaseUsage.Sync);
    Assert.Equal(2u, (uint)DatabaseUsage.Search);
    Assert.Equal(4u, (uint)DatabaseUsage.Install);
    Assert.Equal(8u, (uint)DatabaseUsage.Upgrade);
    Assert.Equal(15u, (uint)DatabaseUsage.All);   // (1 << 4) - 1
    Assert.Equal(DatabaseUsage.All,
      DatabaseUsage.Sync | DatabaseUsage.Search | DatabaseUsage.Install | DatabaseUsage.Upgrade);
  }

  /// <summary>Measured on libalpm 16.0.1: a freshly registered sync database reports 15 (ALL).</summary>
  [Fact]
  public void Usage_DefaultsToAll()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = environment.AlpmSession.RegisterSyncDatabase("pacpar-test", (SigLevel)0);

    Assert.Equal(DatabaseUsage.All, db.Usage);
  }

  [Fact]
  public void Usage_RoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var db = environment.AlpmSession.RegisterSyncDatabase("pacpar-test", (SigLevel)0);

    db.Usage = DatabaseUsage.Search | DatabaseUsage.Install;
    Assert.Equal(DatabaseUsage.Search | DatabaseUsage.Install, db.Usage);

    db.Usage = DatabaseUsage.All;
    Assert.Equal(DatabaseUsage.All, db.Usage);
  }

  [Fact]
  public void Usage_OnTheLocalDatabase_IsReadable()
  {
    using var environment = new IsolatedAlpmEnvironment();

    Assert.Equal(DatabaseUsage.All, environment.AlpmSession.GetLocalDatabase().Usage);
  }
}
