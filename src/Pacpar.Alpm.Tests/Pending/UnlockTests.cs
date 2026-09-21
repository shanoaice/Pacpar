using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.2: <see cref="Alpm.Unlock"/> removes libalpm's database lock file.
/// </summary>
public sealed class UnlockTests
{
  /// <summary>
  /// The lock file lives next to the database, at <c>&lt;dbpath&gt;/db.lck</c> — the same path
  /// libalpm reports from <c>alpm_option_get_lockfile</c>. Writing it by hand is what makes this a
  /// behaviour test rather than a "does not throw" test.
  /// </summary>
  [Fact]
  public void Unlock_RemovesTheDatabaseLockFile()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var lockPath = Path.Combine(environment.Alpm.Options.DatabasePath, "db.lck");
    System.IO.File.WriteAllText(lockPath, "");
    Assert.True(System.IO.File.Exists(lockPath));

    environment.Alpm.Unlock();

    Assert.False(System.IO.File.Exists(lockPath));
  }

  /// <summary>Measured on libalpm 16.0.1: with nothing locked this returns 0, not an error.</summary>
  [Fact]
  public void Unlock_WithNothingLocked_DoesNotThrow()
  {
    using var environment = new IsolatedAlpmEnvironment();

    var exception = Record.Exception(environment.Alpm.Unlock);

    Assert.Null(exception);
  }

  [Fact]
  public void Unlock_AfterDispose_ThrowsObjectDisposedException()
  {
    var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    environment.Dispose();

    Assert.Throws<ObjectDisposedException>(alpm.Unlock);
  }
}
