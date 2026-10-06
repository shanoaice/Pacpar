using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Coverage ledger §4: <see cref="Alpm.Unlock"/> releases the database lock this session holds.
/// </summary>
/// <remarks>
/// The lock belongs to the handle, not to the transaction: <c>alpm_trans_init</c> takes it and
/// <c>alpm_unlock</c> drops it — closing the descriptor and unlinking the file, in that order, which
/// is exactly what pacman reaches for on its way out of a signal handler (<c>sighandler.c:70</c>).
/// Removing a lock <i>this</i> session does not hold is a different decision, and deliberately not
/// one this API takes for the caller; see
/// <see cref="Unlock_LeavesALockFileThisSessionDoesNotHold"/>.
/// </remarks>
public sealed class UnlockTests
{
  /// <summary>
  /// The behaviour worth having: a transaction locks the database when it initializes, and
  /// <see cref="Alpm.Unlock"/> lets that go — descriptor and file — without releasing the transaction.
  /// The file lives at <c>&lt;dbpath&gt;/db.lck</c>, the path libalpm reports from
  /// <c>alpm_option_get_lockfile</c>.
  /// </summary>
  [Fact]
  public void Unlock_ReleasesTheLockThisSessionHolds()
  {
    using var environment = new IsolatedAlpmEnvironment();
    using (environment.Alpm.BeginTransaction())
    {
      Assert.True(System.IO.File.Exists(environment.Alpm.Options.Lockfile));

      environment.Alpm.Unlock();

      Assert.False(System.IO.File.Exists(environment.Alpm.Options.Lockfile));
    }
  }

  /// <summary>
  /// Measured on libalpm 16.0.1: with nothing locked this returns 0, not an error. libalpm returns
  /// early while <c>lockfd &lt; 0</c>, so the call touches nothing at all.
  /// </summary>
  [Fact]
  public void Unlock_WithNothingLocked_DoesNotThrow()
  {
    using var environment = new IsolatedAlpmEnvironment();

    var exception = Record.Exception(environment.Alpm.Unlock);

    Assert.Null(exception);
  }

  /// <summary>
  /// A lock file this session did not create stays put. This is the guard that keeps
  /// <see cref="Alpm.Unlock"/> from unlocking a <i>running</i> session somewhere else: the lock is
  /// nothing but an <c>open(O_CREAT | O_EXCL)</c> file, so deleting it does release another process's
  /// lock, and an empty mode-0 file carries nothing that would tell this session whether that other
  /// process is alive or dead. Cleaning up after a crash is therefore the caller's decision, taken
  /// with <see cref="System.IO.File"/> once the caller knows no other instance is running.
  /// </summary>
  [Fact]
  public void Unlock_LeavesALockFileThisSessionDoesNotHold()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var lockPath = environment.Alpm.Options.Lockfile;
    System.IO.File.WriteAllText(lockPath, "");
    Assert.True(System.IO.File.Exists(lockPath));

    environment.Alpm.Unlock();

    Assert.True(System.IO.File.Exists(lockPath));
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
