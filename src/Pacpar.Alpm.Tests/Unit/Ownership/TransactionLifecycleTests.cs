using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// The handle's one-transaction-at-a-time contract as the wrapper presents it: an active transaction
/// is joined rather than refused, but only when it was initialized with the flags the caller asks
/// for. The flags decide whether the transaction locks the database, writes to the filesystem and
/// runs hooks and scriptlets, so answering a request for one mode with a transaction configured for
/// another would make the caller commit something it never asked for.
/// </summary>
public sealed class TransactionLifecycleTests
{
  /// <summary>No flags: every check on, database locked, filesystem written.</summary>
  private const TransactionFlags NoFlags = 0;
  private const TransactionFlags Lockless = TransactionFlags.AlpmTransFlagNolock;

  [Fact]
  public void BeginTransaction_JoinsTheActiveTransaction_WhenTheFlagsMatch()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var first = alpm.BeginTransaction(Lockless);
    var second = alpm.BeginTransaction(Lockless);

    Assert.Same(first, second);
    Assert.Equal(Lockless, second.GetFlags());
  }

  [Fact]
  public void BeginTransaction_DefaultsToTheFullyCheckedMode()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var transaction = alpm.BeginTransaction();

    Assert.Equal(NoFlags, transaction.GetFlags());
    Assert.Same(transaction, alpm.CurrentTransaction);
  }

  [Fact]
  public void BeginTransaction_WithDifferentFlags_WhileATransactionIsActive_Throws()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    using var active = alpm.BeginTransaction();

    var exception = Assert.Throws<InvalidOperationException>(() => alpm.BeginTransaction(Lockless));

    Assert.Contains("already active", exception.Message);
    Assert.Same(active, alpm.CurrentTransaction);
    Assert.Equal(NoFlags, active.GetFlags());
  }

  [Fact]
  public void CurrentTransaction_IsCleared_WhenTheTransactionIsDisposed()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var transaction = alpm.BeginTransaction(Lockless);
    transaction.Dispose();

    Assert.Null(alpm.CurrentTransaction);
  }
  [Fact]
  public void CurrentTransaction_IsNotCleared_WhenNativeReleaseFails()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var transaction = alpm.BeginTransaction(Lockless);

    // Directly release the native transaction so that transaction.Dispose()'s release call fails with -1
    var nativeErr = NativeMethods.alpm_trans_release(alpm.Handle);
    Assert.Equal(0, nativeErr);

    // Now disposing the transaction fails at the native layer because handle->trans is already null
    transaction.Dispose();

    // The wrapper must keep CurrentTransaction intact because release failed.
    Assert.Same(transaction, alpm.CurrentTransaction);

    // The domain is retired all the same. alpm_trans_release fails only when handle->trans is
    // already NULL - that is, when the native transaction is already gone - and that is exactly when
    // every view into it is dangling. Dispose invalidates before it releases, so a free never
    // happens under a view that still passes its check; the failure path is no exception.
    Assert.False(transaction.Lifetime.IsAlive);
  }

  /// <summary>
  /// ADR 0008: <see cref="Transaction.Dispose"/> does not throw, so a failed
  /// <c>alpm_trans_release</c> has to be observable on the transaction - otherwise the caller is
  /// told the transaction ended while the lock and the handle are still held.
  /// </summary>
  [Fact]
  public void Dispose_RecordsTheFailure_WhenTheNativeReleaseFails()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var transaction = alpm.BeginTransaction(Lockless);
    Assert.Null(transaction.ReleaseFailure);

    // Release the native transaction behind the wrapper's back, so the release Dispose attempts
    // fails with -1 on a handle whose trans is already NULL.
    Assert.Equal(0, NativeMethods.alpm_trans_release(alpm.Handle));

    transaction.Dispose();

    Assert.False(transaction.IsReleased);

    var failure = Assert.IsAssignableFrom<AlpmException>(transaction.ReleaseFailure);
    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_TRANS_NULL, failure.Errno);
    Assert.Contains("release transaction", failure.Message);
  }

  /// <summary>The other half: a release that worked leaves nothing to report.</summary>
  [Fact]
  public void Dispose_LeavesNoFailure_WhenTheReleaseSucceeds()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    var transaction = alpm.BeginTransaction(Lockless);
    Assert.False(transaction.IsReleased);

    transaction.Dispose();

    Assert.True(transaction.IsReleased);
    Assert.Null(transaction.ReleaseFailure);
    Assert.Null(alpm.CurrentTransaction);
  }

  [Fact]
  public void BeginTransaction_AfterTheHandleWasDisposed_ThrowsObjectDisposed()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    alpm.Dispose();

    Assert.Throws<ObjectDisposedException>(() => alpm.BeginTransaction());
  }
}
