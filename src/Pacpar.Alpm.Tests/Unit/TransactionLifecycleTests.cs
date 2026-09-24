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
  private const TransactionFlags NoFlags = (TransactionFlags)0;
  private const TransactionFlags Lockless = TransactionFlags.ALPM_TRANS_FLAG_NOLOCK;

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
    using var active = alpm.BeginTransaction(NoFlags);

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
  public void BeginTransaction_AfterTheHandleWasDisposed_ThrowsObjectDisposed()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    alpm.Dispose();

    Assert.Throws<ObjectDisposedException>(() => alpm.BeginTransaction(NoFlags));
  }
}
