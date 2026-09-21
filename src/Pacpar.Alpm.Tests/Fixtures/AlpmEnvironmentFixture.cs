using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Fixtures;

public sealed class AlpmEnvironmentFixture : IDisposable
{
  private readonly string _workspaceRoot;

  public AlpmEnvironmentFixture()
  {
    if (Environment.GetEnvironmentVariable("PACPAR_ALPM_TEST_ENV") != "container")
    {
      throw new InvalidOperationException(
        "Integration tests must run inside the project container. "
        + "Use: scripts/run-alpm-tests-in-container.sh");
    }

    _workspaceRoot = Path.Combine(Path.GetTempPath(), "pacpar-alpm-tests", Guid.NewGuid().ToString("n"));
  }

  public unsafe string LibalpmVersion
    => Marshal.PtrToStringAnsi((nint)NativeMethods.alpm_version()) ?? string.Empty;

  /// <summary>
  /// Creates an Alpm handle with an isolated root and database path unique to this call.
  /// Each call receives its own directory tree, eliminating lockfile collisions
  /// when multiple tests initialize handles against the same fixture.
  /// </summary>
  public Alpm CreateAlpm()
  {
    var id = Guid.NewGuid().ToString("n");
    var root = Path.Combine(_workspaceRoot, "tests", id, "root");
    var dbpath = Path.Combine(_workspaceRoot, "tests", id, "var", "lib", "pacman");

    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(dbpath, "local"));
    Directory.CreateDirectory(Path.Combine(root, "tmp"));
    Directory.CreateDirectory(Path.Combine(root, "var", "cache", "pacman", "pkg"));

    return new Alpm(root, dbpath);
  }

  public void Dispose()
  {
    if (Directory.Exists(_workspaceRoot))
    {
      Directory.Delete(_workspaceRoot, recursive: true);
    }
  }
}
