using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.6: <c>alpm_checkdeps</c> / <c>alpm_checkconflicts</c>.
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
    var packages = environment.Alpm.GetLocalDatabase().GetPackageCache();

    Assert.Empty(environment.Alpm.CheckDependencies(packages));
  }

  [Fact]
  public void CheckConflicts_WithAnEmptyPackageList_ReturnsNothing()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.Alpm.GetLocalDatabase().GetPackageCache();

    Assert.Empty(environment.Alpm.CheckConflicts(packages));
  }

  [Fact]
  public void CheckDependencies_AcceptsTheRemoveAndUpgradeLists()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.Alpm.GetLocalDatabase().GetPackageCache();

    var missing = environment.Alpm.CheckDependencies(
      packages, remove: packages, upgrade: packages, reverseDependencies: true);

    Assert.Empty(missing);
  }

  /// <summary>
  /// The input list is borrowed by libalpm and only freed by us if we built it; the caller's
  /// <c>AlpmList&lt;Package&gt;</c> must still be usable afterwards.
  /// </summary>
  [Fact]
  public void CheckConflicts_DoesNotConsumeTheCallersList()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var packages = environment.Alpm.GetLocalDatabase().GetPackageCache();

    environment.Alpm.CheckConflicts(packages);

    Assert.Empty(packages);
    Assert.Equal(0, packages.Count);
  }
}
