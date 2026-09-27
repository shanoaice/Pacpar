using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Locks in the low-risk half of the error-signal rules from the design report (§E):
/// <list type="bullet">
/// <item>rule 3 - a null native pointer with an ok errno means "empty", not "failure";</item>
/// <item>rule 4 - <c>Database.Validate</c> no longer returns a <c>(bool, Exception?)</c> tuple;</item>
/// <item>rule 5 - option getters that start out unset are nullable instead of asserting non-null.</item>
/// </list>
/// </summary>
public sealed class ErrorContractTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  private Alpm Alpm => _environment.Alpm;

  private Database LocalDatabase => _environment.Alpm.GetLocalDatabase();

  [Fact]
  public void GetPackageCache_IsAnEmptyView_WhenTheDatabaseHasNoPackages()
  {
    // Regression: this used to throw ArgumentOutOfRangeException - the null cache came with
    // ALPM_ERR_OK and ToException rejects "ok is not an error".
    Assert.Empty(LocalDatabase.GetPackageCache());
  }

  [Fact]
  public void GetGroupCache_IsAnEmptyView_WhenTheDatabaseHasNoGroups()
    => Assert.Empty(LocalDatabase.GetGroupCache());

  [Fact]
  public void GetServers_And_GetCacheServers_AreEmpty_WithoutAnError()
  {
    var database = LocalDatabase;

    Assert.Empty(database.GetServers());
    Assert.Empty(database.GetCacheServers());
  }

  [Fact]
  public void GetPackageCache_Throws_WhenLibalpmReportsAnError()
  {
    // A freshly registered sync database has no loaded package cache yet: libalpm sets an errno.
    var sync = Alpm.RegisterSyncDatabase("core", (SigLevel)0);

    Assert.ThrowsAny<Exception>(() =>
    {
      _ = sync.GetPackageCache();
    });
  }

  [Fact]
  public void Validate_DoesNotThrow_AndIsValidIsTrue_ForAFreshDatabase()
  {
    var database = LocalDatabase;

    Assert.True(database.IsValid);
    database.Validate();
  }

  [Fact]
  public void UnsetOptionStrings_AreNull_InsteadOfPretendingToBeSet()
  {
    Assert.Null(Alpm.Options.LogFile);
    Assert.Null(Alpm.Options.GpgDirectory);
  }

  [Fact]
  public void AlwaysSetOptionStrings_AreNotNull()
  {
    Assert.NotNull(Alpm.Options.Root);
    Assert.NotNull(Alpm.Options.DatabasePath);
    Assert.NotNull(Alpm.Options.Lockfile);
  }
}
