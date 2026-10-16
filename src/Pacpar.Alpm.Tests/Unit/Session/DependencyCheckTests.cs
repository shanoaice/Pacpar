using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Session;

/// <summary>
/// Coverage ledger §4: <c>alpm_checkdeps</c> / <c>alpm_checkconflicts</c>.
/// </summary>
/// <remarks>
/// Both functions return an allocated list or <c>NULL</c>, and <b>NULL means "nothing wrong"</b> —
/// not an error (measured: errno stays ALPM_ERR_OK). With an empty local database there is nothing
/// to check, which is what these hermetic tests exercise; a case that actually produces a missing
/// dependency or a conflict needs a real package file and belongs in <c>Integration/</c>.
/// </remarks>
public sealed class DependencyCheckTests
{
  [Fact]
  public void CheckDependencies_WithAnEmptyPackageList_ReturnsNothing()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    Assert.Empty(environment.AlpmSession.CheckDependencies(packages));
  }

  [Fact]
  public void CheckConflicts_WithAnEmptyPackageList_ReturnsNothing()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    Assert.Empty(environment.AlpmSession.CheckConflicts(packages));
  }

  [Fact]
  public void CheckDependencies_AcceptsTheRemoveAndUpgradeLists()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    var missing = environment.AlpmSession.CheckDependencies(
      packages, remove: packages, upgrade: packages, reverseDependencies: true);

    Assert.Empty(missing);
  }

  /// <summary>
  /// The input list is borrowed by libalpm and only freed by us if we built it; the caller's
  /// <c>AlpmList&lt;PackageView&gt;</c> must still be usable afterwards.
  /// </summary>
  [Fact]
  public void CheckConflicts_DoesNotConsumeTheCallersList()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.AlpmSession.GetLocalDatabase().LoadPackageCache();

    environment.AlpmSession.CheckConflicts(packages);

    Assert.Empty(packages);
#pragma warning disable xUnit2013
    Assert.Equal(0, packages.Count());
#pragma warning restore xUnit2013
  }
}
