using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers the package members that read what the package records about itself: the install reason,
/// its file list, its download size, and its changelog.
/// </summary>
public sealed class PackageMetadataTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  [Fact]
  public void InstallReason_CanBeReadAndModified()
  {
    var pkg = _environment.Install("test-reason-pkg");

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
    var pkg = _environment.Install("test-filelist-pkg", file: filePath);

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
    var pkg = _environment.Install("test-meta-pkg");

    Assert.True(pkg.DownloadSize >= 0);
    Assert.Null(pkg.OpenChangelogStream());
    Assert.Null(pkg.ReadChangelog());
  }
}
