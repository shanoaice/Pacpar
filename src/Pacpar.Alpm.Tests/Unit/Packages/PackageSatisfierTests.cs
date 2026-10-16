using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers the two satisfier entry points and the lifetime each one answers with:
/// <see cref="PackageView.FindSatisfier"/> hands the caller's own view back, while
/// <see cref="AlpmSession.ResolveDependency"/> mints a new one under the database that owns the match.
/// </summary>
public sealed class PackageSatisfierTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void PackageView_FindSatisfier_FindsMatchingPackageInList()
  {
    var pkg = _environment.Install("test-satisfier-target");

    var found = PackageView.FindSatisfier([pkg], "test-satisfier-target");
    Assert.NotNull(found);
    Assert.Equal("test-satisfier-target", found!.Name);

    var notFound = PackageView.FindSatisfier([pkg], "nonexistent-dep");
    Assert.Null(notFound);
  }

  [Fact]
  public void AlpmSession_ResolveDependency_FindsMatchingPackageAcrossDatabases()
  {
    var pkg = _environment.Install("test-dbs-satisfier-target");
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    var found = _environment.AlpmSession.ResolveDependency([localDb], "test-dbs-satisfier-target");
    Assert.NotNull(found);
    Assert.Equal("test-dbs-satisfier-target", found!.Name);

    var notFound = _environment.AlpmSession.ResolveDependency([localDb], "no-such-pkg");
    Assert.Null(notFound);
  }

  /// <summary>
  /// The bug this guards: resolving the owning domain through the handle registry mints a second
  /// domain for the local database, because <c>AlpmSession.GetLocalDatabase</c> creates its domain directly
  /// and never registers the pointer (<c>Lifetime</c> keeps one domain per native database for exactly
  /// this reason). Views issued from the duplicate are not retired when the database's real domain is.
  /// </summary>
  /// <remarks>
  /// Invalidation is driven per database here - what <c>Database.Update</c> does - rather than through
  /// a commit, because a commit retires the whole session root first and would hide the difference.
  /// </remarks>
  [Fact]
  public void AlpmSession_ResolveDependency_IssuesViewsUnderTheLocalDatabaseDomain()
  {
    _environment.Install("test-satisfier-domain-pkg");
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    var viaGetPackage = localDb.GetPackage("test-satisfier-domain-pkg");
    var viaResolveDependency = _environment.AlpmSession.ResolveDependency([localDb], "test-satisfier-domain-pkg");
    Assert.NotNull(viaGetPackage);
    Assert.NotNull(viaResolveDependency);

    localDb.InvalidateViews("Database.Update");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => viaGetPackage!.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Throws<AlpmLifetimeException>(() => viaResolveDependency!.Name);
  }
}
