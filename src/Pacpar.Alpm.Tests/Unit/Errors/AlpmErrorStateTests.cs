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
  /// <summary>A path that is guaranteed not to exist, so loading it fails inside libalpm.</summary>
  private static string MissingPackage =>
    Path.Combine(Path.GetTempPath(), $"pacpar-missing-{Guid.NewGuid():n}.pkg.tar.zst");

  [Fact]
  public void Errno_ReportsTheHandleError_NotTheInitializeOutParameter()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    // A failure libalpm reports through the handle. (This used to be a second BeginTransaction,
    // which is a join of the active transaction now, so the handle stays clean.)
    var exception = Record.Exception(() => alpm.LoadPackage(MissingPackage, false, SigLevel.ALPM_SIG_USE_DEFAULT));

    Assert.NotNull(exception);
    Assert.IsNotType<NullReferenceException>(exception);
    Assert.Equal(_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND, alpm.Errno);
    Assert.Contains("could not find or read package", alpm.GetCurrentErrorString());
  }

  [Fact]
  public void GetCurrentError_IsNotNull_WhileTheHandleHasAnError()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var alpm = environment.Alpm;

    Record.Exception(() => alpm.LoadPackage(MissingPackage, false, SigLevel.ALPM_SIG_USE_DEFAULT));

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
