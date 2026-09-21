using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// The handle's error state must be read from the handle itself. <see cref="Alpm.Errno"/> used to
/// return the out-parameter of <c>alpm_initialize</c>, which libalpm writes only when
/// initialization fails: after a successful init it stayed <c>ALPM_ERR_OK</c> forever, so failures
/// surfaced as <see cref="NullReferenceException"/> (from <c>throw GetCurrentError()!</c>) or as the
/// wrong error entirely.
/// </summary>
public sealed class AlpmErrorStateTests
{
  [Fact]
  public void Errno_ReportsTheHandleError_NotTheInitializeOutParameter()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    using var first = alpm.BeginTransaction(TransactionFlags.ALPM_TRANS_FLAG_NOLOCK);

    // A second transaction cannot be initialized: libalpm reports ALPM_ERR_TRANS_NOT_NULL.
    var exception = Record.Exception(() => alpm.BeginTransaction(TransactionFlags.ALPM_TRANS_FLAG_NOLOCK));

    Assert.NotNull(exception);
    Assert.IsNotType<NullReferenceException>(exception);
    Assert.Equal(_alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL, alpm.Errno);
    Assert.Contains("transaction already initialized", alpm.GetCurrentErrorString());
  }

  [Fact]
  public void GetCurrentError_IsNotNull_WhileTheHandleHasAnError()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;
    using var first = alpm.BeginTransaction(TransactionFlags.ALPM_TRANS_FLAG_NOLOCK);

    Record.Exception(() => alpm.BeginTransaction(TransactionFlags.ALPM_TRANS_FLAG_NOLOCK));

    Assert.NotNull(alpm.GetCurrentError());
  }

  [Fact]
  public void Errno_IsOk_AndGetCurrentErrorIsNull_AfterASuccessfulInitialization()
  {
    using var environment = new IsolatedAlpmEnvironment();

    Assert.Equal(_alpm_errno_t.ALPM_ERR_OK, environment.Alpm.Errno);
    Assert.Null(environment.Alpm.GetCurrentError());
  }
}
