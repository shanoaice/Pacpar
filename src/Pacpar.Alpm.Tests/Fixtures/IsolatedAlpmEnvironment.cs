namespace Pacpar.Alpm.Tests.Fixtures;

/// <summary>
/// An isolated libalpm handle for tests that must run without the project container: a unique
/// root/dbpath tree under <see cref="Path.GetTempPath()"/>, built the same way as
/// <see cref="AlpmEnvironmentFixture"/> but without its container assertion. It also provides
/// <see cref="Install"/>, the synthetic package every feature test starts from.
/// </summary>
internal sealed class IsolatedAlpmEnvironment : IDisposable
{
  private readonly string _workspaceRoot;

  internal IsolatedAlpmEnvironment()
  {
    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-isolated", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(_workspaceRoot, "root");
    var dbpath = Path.Combine(_workspaceRoot, "var", "lib", "pacman");

    PackageDirectory = Path.Combine(_workspaceRoot, "packages");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));
    Directory.CreateDirectory(PackageDirectory);

    AlpmSession = new AlpmSession(root, dbpath);
  }

  internal AlpmSession AlpmSession { get; }
  internal string WorkspaceRoot => _workspaceRoot;

  /// <summary>Where <see cref="Install"/> writes the packages it builds.</summary>
  internal string PackageDirectory { get; }

  /// <summary>
  /// Builds a synthetic package in <see cref="PackageDirectory"/>, commits it, and answers with the
  /// view the local database holds for it afterwards.
  /// </summary>
  /// <param name="name">The package name.</param>
  /// <param name="file">Optional file the package owns, for example <c>usr/bin/probe-file</c>.</param>
  /// <param name="mtree">Optional <c>.MTREE</c> contents.</param>
  /// <param name="depend">Optional <c>depend</c> entry.</param>
  /// <param name="conflict">Optional <c>conflict</c> entry.</param>
  internal PackageView Install(string name, string? file = null, string? mtree = null, string? depend = null,
    string? conflict = null)
  {
    var archive = PackageArchive.Create(PackageDirectory, name, file: file, mtree: mtree, depend: depend,
      conflict: conflict);

    using var transaction = AlpmSession.BeginTransaction();
    transaction.AddPackage(AlpmSession.LoadPackage(archive, full: true, SigLevel.UseDefault));
    transaction.Prepare();
    transaction.Commit();

    var installed = AlpmSession.GetLocalDatabase().GetPackage(name);
    Assert.NotNull(installed);
    return installed!;
  }

  public void Dispose()
  {
    AlpmSession.Dispose();

    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }
}
