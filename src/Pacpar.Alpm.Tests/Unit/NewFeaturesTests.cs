using Pacpar.Alpm.Bindings;
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

  private PackageView InstallAndGetPackage(string name, string? file = null, string? mtree = null)
  {
    var archive = PackageArchive.Create(_packageDirectory, name, file: file, mtree: mtree);
    using (var transaction = _environment.AlpmSession.BeginTransaction())
    {
      transaction.AddPackage(_environment.AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault));
      transaction.Prepare();
      transaction.Commit();
    }

    var localDb = _environment.AlpmSession.GetLocalDatabase();
    var pkg = localDb.GetPackage(name);
    Assert.NotNull(pkg);
    return pkg!;
  }

  private List<MtreeEntry> ReadMtreeEntries(string name, string mtreeContent)
  {
    InstallAndGetPackage(name, mtree: mtreeContent);
    var root = Path.Combine(_environment.WorkspaceRoot, "root");
    var dbpath = Path.Combine(_environment.WorkspaceRoot, "var", "lib", "pacman");
    using var freshSession = new AlpmSession(root, dbpath);
    var pkg = freshSession.GetLocalDatabase().GetPackage(name)!;
    using var reader = pkg.GetMtree();
    Assert.NotNull(reader);
    return reader!.ToList();
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
    var localDb = _environment.AlpmSession.GetLocalDatabase();

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
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    var found = _environment.AlpmSession.FindSatisfier([localDb], "test-dbs-satisfier-target");
    Assert.NotNull(found);
    Assert.Equal("test-dbs-satisfier-target", found!.Name);

    var notFound = _environment.AlpmSession.FindSatisfier([localDb], "no-such-pkg");
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
  public void Alpm_FindSatisfier_IssuesViewsUnderTheLocalDatabaseDomain()
  {
    InstallAndGetPackage("test-satisfier-domain-pkg");
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    var viaGetPackage = localDb.GetPackage("test-satisfier-domain-pkg");
    var viaFindSatisfier = _environment.AlpmSession.FindSatisfier([localDb], "test-satisfier-domain-pkg");
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
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("test-sync-empty", 0);

    var upgrade = pkg.GetNewVersion([syncDb]);
    Assert.Null(upgrade);
  }

  [Fact]
  public void Database_Update_WithoutServers_FailsAndReportsError()
  {
    var syncDb = _environment.AlpmSession.RegisterSyncDatabase("test-sync-update", 0);

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
    var localDb = _environment.AlpmSession.GetLocalDatabase();

    // Calling InvalidateViews retires all views issued under localDb
    localDb.InvalidateViews("Database.Update");

    var thrown = Assert.Throws<AlpmLifetimeException>(() => pkg.Name);
    Assert.Equal("the local database", thrown.Target);
    Assert.Equal("Database.Update", thrown.InvalidatedBy);
  }

  [Fact]
  public void PackageView_GetMtree_ReturnsNullWhenMtreeAbsent()
  {
    var pkg = InstallAndGetPackage("test-no-mtree");
    var mtree = pkg.GetMtree();
    Assert.Null(mtree);
  }

  [Fact]
  public void PackageView_GetMtree_ReadsEntriesWhenMtreePresent()
  {
    var mtreeContent = """
      #mtree
      /set type=file uid=0 gid=0 mode=644
      ./usr/bin/tool mode=755 size=42 sha256digest=ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
      ./usr/share/doc mode=644 size=10 sha256digest=ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad

      """;
    var entries = ReadMtreeEntries("test-with-mtree", mtreeContent);
    Assert.Equal(2, entries.Count);

    var fileEntry = entries[0];
    Assert.Equal("usr/bin/tool", fileEntry.Path);
    Assert.Equal(42, fileEntry.Size);
    Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", fileEntry.Sha256);

    var docEntry = entries[1];
    Assert.Equal("usr/share/doc", docEntry.Path);
    Assert.Equal(10, docEntry.Size);
  }

  [Fact]
  public void PackageView_GetMtree_ReportsMissingDigestsAsNull()
  {
    var mtreeContent = """
      #mtree
      /set type=file uid=0 gid=0 mode=644
      ./plain size=0
      ./only-md5 size=0 md5digest=d41d8cd98f00b204e9800998ecf8427e
      ./only-sha size=0 sha256digest=ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad

      """;
    var entries = ReadMtreeEntries("test-mtree-digests", mtreeContent);
    Assert.Equal(3, entries.Count);

    Assert.Equal("plain", entries[0].Path);
    Assert.Null(entries[0].Sha256);
    Assert.Null(entries[0].Md5);

    Assert.Equal("only-md5", entries[1].Path);
    Assert.Null(entries[1].Sha256);
    Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", entries[1].Md5);

    Assert.Equal("only-sha", entries[2].Path);
    Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", entries[2].Sha256);
    Assert.Null(entries[2].Md5);
  }

  [Fact]
  public void PackageView_GetMtree_ThrowsWhenAnEntryFailsToRead()
  {
    var mtreeContent = """
      #mtree
      /set type=file uid=0 gid=0 mode=644
      ./good-before size=0
      ./bad size=0 sha256digest=zzzz
      ./good-after size=0

      """;
    Assert.Throws<AlpmNativeFailureException>(() => ReadMtreeEntries("test-mtree-bad-digest", mtreeContent));
  }

  [Fact]
  public void PackageView_GetMtree_ReadsIdsBeyond32Bits()
  {
    var mtreeContent = """
      #mtree
      /set type=file uid=0 gid=0 mode=644
      ./big-ids size=0 uid=8589934592 gid=4294967296

      """;
    var entries = ReadMtreeEntries("test-mtree-big-ids", mtreeContent);
    var entry = Assert.Single(entries);
    Assert.Equal("big-ids", entry.Path);
    Assert.Equal(8589934592L, entry.Uid);
    Assert.Equal(4294967296L, entry.Gid);
  }
}
