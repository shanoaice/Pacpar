using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

public sealed class NewFeaturesTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();
  private readonly string _packageDirectory =
    Path.Combine(Path.GetTempPath(), "pacpar-new-features-test-" + Guid.NewGuid().ToString("n"));

  public NewFeaturesTests()
  {
    Directory.CreateDirectory(_packageDirectory);
  }

  public void Dispose()
  {
    _environment.Dispose();
    if (Directory.Exists(_packageDirectory))
    {
      Directory.Delete(_packageDirectory, recursive: true);
    }
  }

  private PackageView InstallAndGetPackage(string name, string? file = null)
  {
    var archive = PackageArchive.Create(_packageDirectory, name, file: file);
    using (var transaction = _environment.Alpm.BeginTransaction())
    {
      transaction.AddPackage(_environment.Alpm.LoadPackage(archive, full: true, SigLevel.AlpmSigUseDefault));
      transaction.Prepare();
      transaction.Commit();
    }

    var localDb = _environment.Alpm.GetLocalDatabase();
    var pkg = localDb.GetPackage(name);
    Assert.NotNull(pkg);
    return pkg!;
  }

  [Fact]
  public void InstallReason_CanBeReadAndModified()
  {
    var pkg = InstallAndGetPackage("test-reason-pkg");

    Assert.Equal(PackageReason.Explicit, pkg.InstallReason);

    pkg.InstallReason = PackageReason.Dependency;
    Assert.Equal(PackageReason.Dependency, pkg.InstallReason);

    pkg.SetReason(PackageReason.Explicit);
    Assert.Equal(PackageReason.Explicit, pkg.InstallReason);
  }

  [Fact]
  public void FileList_ContainsAndFindFile_FindsPackagedFiles()
  {
    const string filePath = "usr/bin/tool-binary";
    var pkg = InstallAndGetPackage("test-filelist-pkg", file: filePath);

    Assert.True(pkg.Files.Contains(filePath));
    Assert.False(pkg.Files.Contains("usr/bin/nonexistent-binary"));

    var found = pkg.Files.FindFile(filePath);
    Assert.NotNull(found);
    Assert.Equal(filePath, found!.Value.Name);

    Assert.Null(pkg.Files.FindFile("usr/bin/nonexistent-binary"));
  }

  [Fact]
  public void Package_DownloadSizeAndChangelog()
  {
    var pkg = InstallAndGetPackage("test-meta-pkg");

    Assert.True(pkg.DownloadSize >= 0);
    Assert.Null(pkg.OpenChangelogStream());
    Assert.Null(pkg.ReadChangelog());
  }

  [Fact]
  public void Database_Search_FindsMatchingPackages()
  {
    var pkg = InstallAndGetPackage("test-search-alpha");
    var localDb = _environment.Alpm.GetLocalDatabase();

    var results = localDb.Search(["test-search-alpha"]);
    Assert.Contains(results, p => p.Name == "test-search-alpha");

    var emptyResults = localDb.Search(["no-match-xyz-987"]);
    Assert.Empty(emptyResults);
  }

  [Fact]
  public void PackageView_FindSatisfier_FindsMatchingPackageInList()
  {
    var pkg = InstallAndGetPackage("test-satisfier-target");

    var found = PackageView.FindSatisfier([pkg], "test-satisfier-target");
    Assert.NotNull(found);
    Assert.Equal("test-satisfier-target", found!.Name);

    var notFound = PackageView.FindSatisfier([pkg], "nonexistent-dep");
    Assert.Null(notFound);
  }

  [Fact]
  public void Alpm_FindSatisfier_FindsMatchingPackageAcrossDatabases()
  {
    var pkg = InstallAndGetPackage("test-dbs-satisfier-target");
    var localDb = _environment.Alpm.GetLocalDatabase();

    var found = _environment.Alpm.FindSatisfier([localDb], "test-dbs-satisfier-target");
    Assert.NotNull(found);
    Assert.Equal("test-dbs-satisfier-target", found!.Name);

    var notFound = _environment.Alpm.FindSatisfier([localDb], "no-such-pkg");
    Assert.Null(notFound);
  }

  /// <summary>
  /// The bug this guards: resolving the owning domain through the handle registry mints a second
  /// domain for the local database, because <c>Alpm.GetLocalDatabase</c> creates its domain directly
  /// and never registers the pointer (<c>Lifetime</c> keeps one domain per native database for exactly
  /// this reason). Views issued from the duplicate are not retired when the database's real domain is.
  /// </summary>
  /// <remarks>
  /// Invalidation is driven per database here - what <c>Database.Update</c> does - rather than through
  /// a commit, because a commit retires the whole session root first and would hide the difference.
  /// </remarks>
  [Fact]
  public void Alpm_FindSatisfier_IssuesViewsUnderTheLocalDatabaseDomain()
  {
    InstallAndGetPackage("test-satisfier-domain-pkg");
    var localDb = _environment.Alpm.GetLocalDatabase();

    var viaGetPackage = localDb.GetPackage("test-satisfier-domain-pkg");
    var viaFindSatisfier = _environment.Alpm.FindSatisfier([localDb], "test-satisfier-domain-pkg");
    Assert.NotNull(viaGetPackage);
    Assert.NotNull(viaFindSatisfier);

    localDb.InvalidateViews("Database.Update");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => viaGetPackage!.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Throws<AlpmLifetimeException>(() => viaFindSatisfier!.Name);
  }

  [Fact]
  public void PackageView_GetNewVersion_ReturnsNullWhenNoSyncDatabasesHaveNewer()
  {
    var pkg = InstallAndGetPackage("test-upgrade-check");
    var syncDb = _environment.Alpm.RegisterSyncDatabase("test-sync-empty", 0);

    var upgrade = pkg.GetNewVersion([syncDb]);
    Assert.Null(upgrade);
  }

  [Fact]
  public void Database_Update_WithoutServers_FailsAndReportsError()
  {
    var syncDb = _environment.Alpm.RegisterSyncDatabase("test-sync-update", 0);

    var ok = syncDb.TryUpdate(force: false, out var updated, out var failure);
    Assert.False(ok);
    Assert.False(updated);
    Assert.NotNull(failure);

    Assert.ThrowsAny<AlpmException>(() => syncDb.Update());
  }

  [Fact]
  public void Database_Update_InvalidatesPriorPackageViews()
  {
    var pkg = InstallAndGetPackage("test-invalidation-pkg");
    var localDb = _environment.Alpm.GetLocalDatabase();

    // Calling InvalidateViews retires all views issued under localDb
    localDb.InvalidateViews("Database.Update");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => pkg.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Database.Update", thrown.InvalidatedBy);
  }
}
