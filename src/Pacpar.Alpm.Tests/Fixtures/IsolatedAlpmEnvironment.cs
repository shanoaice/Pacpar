namespace Pacpar.Alpm.Tests.Fixtures;

/// <summary>
/// An isolated libalpm handle for tests that must run without the project container: a unique
/// root/dbpath tree under <see cref="Path.GetTempPath()"/>, built the same way as
/// <see cref="AlpmEnvironmentFixture"/> but without its container assertion.
/// </summary>
internal sealed class IsolatedAlpmEnvironment : IDisposable
{
  private readonly string _workspaceRoot;

  internal IsolatedAlpmEnvironment()
  {
    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-isolated", Guid.NewGuid().ToString("n"));
    var root = Path.Combine(_workspaceRoot, "root");
    var dbpath = Path.Combine(_workspaceRoot, "var", "lib", "pacman");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));

    Alpm = new Alpm(root, dbpath);
  }

  internal Alpm Alpm { get; }

  public void Dispose()
  {
    Alpm.Dispose();

    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }
}
