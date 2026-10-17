using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit.Packages;

/// <summary>
/// Covers <see cref="PackageBase.GetMtree"/> against packages whose <c>.MTREE</c> the test wrote:
/// absent, complete, partly filled in, and the two ways an entry can fail to read.
/// </summary>
/// <remarks>
/// libalpm reads the mtree from <c>dbpath/local/pkg/mtree</c>, which exists only
/// once the package is installed, so every case installs first and then reads it back through a
/// handle opened after the commit.
/// </remarks>
public sealed class PackageMtreeTests : IDisposable
{
  private readonly IsolatedAlpmEnvironment _environment = new();

  public void Dispose() => _environment.Dispose();

  /// <summary>
  /// Installs a package carrying <paramref name="mtreeContent"/>, reopens the workspace and reads the
  /// mtree as a list, so a case can assert on the entries.
  /// </summary>
  private List<MtreeEntry> ReadMtreeEntries(string name, string mtreeContent)
  {
    _environment.Install(name, mtree: mtreeContent);

    var root = Path.Combine(_environment.WorkspaceRoot, "root");
    var dbpath = Path.Combine(_environment.WorkspaceRoot, "var", "lib", "pacman");
    using var freshSession = new AlpmSession(root, dbpath);
    var pkg = freshSession.GetLocalDatabase().GetPackage(name)!;

    using var reader = pkg.GetMtree();
    Assert.NotNull(reader);
    return reader!.ToList();
  }

  [Fact]
  public void PackageView_GetMtree_ReturnsNullWhenMtreeAbsent()
  {
    var pkg = _environment.Install("pacpar-no-mtree");

    Assert.Null(pkg.GetMtree());
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
    var entries = ReadMtreeEntries("pacpar-with-mtree", mtreeContent);
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
    var entries = ReadMtreeEntries("pacpar-mtree-digests", mtreeContent);
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
    Assert.Throws<AlpmNativeFailureException>(() => ReadMtreeEntries("pacpar-mtree-bad-digest", mtreeContent));
  }

  [Fact]
  public void PackageView_GetMtree_ReadsIdsBeyond32Bits()
  {
    var mtreeContent = """
      #mtree
      /set type=file uid=0 gid=0 mode=644
      ./big-ids size=0 uid=8589934592 gid=4294967296

      """;
    var entries = ReadMtreeEntries("pacpar-mtree-big-ids", mtreeContent);
    var entry = Assert.Single(entries);
    Assert.Equal("big-ids", entry.Path);
    Assert.Equal(8589934592L, entry.Uid);
    Assert.Equal(4294967296L, entry.Gid);
  }
}
