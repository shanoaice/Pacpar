using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.5: the sandbox options.
/// </summary>
/// <remarks>
/// The aggregate C accessors (<c>alpm_option_get/set_disable_sandbox</c>) are declared in alpm.h and
/// documented in <c>libalpm_options.3</c> but are <b>not exported</b> by the installed
/// <c>libalpm.so.16</c> — calling them throws <see cref="EntryPointNotFoundException"/>. The wrapper
/// must therefore derive <see cref="AlpmOptions.Sandbox"/> from the three component flags, which
/// reproduces libalpm's own definition (0 = all enabled, 1 = any disabled, 2 = all disabled).
/// </remarks>
public sealed class SandboxOptionTests
{
  [Fact]
  public void Components_DefaultToEnabled()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    Assert.False(options.DisableSandboxFilesystem);
    Assert.False(options.DisableSandboxNetwork);
    Assert.False(options.DisableSandboxSyscalls);
    Assert.Equal(SandboxState.Enabled, options.Sandbox);
  }

  [Fact]
  public void Components_RoundTrip()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    options.DisableSandboxFilesystem = true;
    options.DisableSandboxNetwork = true;
    options.DisableSandboxSyscalls = true;
    Assert.True(options.DisableSandboxFilesystem);
    Assert.True(options.DisableSandboxNetwork);
    Assert.True(options.DisableSandboxSyscalls);

    options.DisableSandboxFilesystem = false;
    Assert.False(options.DisableSandboxFilesystem);
    Assert.True(options.DisableSandboxNetwork);
  }

  [Fact]
  public void Sandbox_ReportsPartiallyDisabled_WhenOnlySomeComponentsAreDisabled()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    options.DisableSandboxSyscalls = true;

    Assert.Equal(SandboxState.PartiallyDisabled, options.Sandbox);
  }

  [Fact]
  public void Sandbox_ReportsDisabled_WhenEveryComponentIsDisabled()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    options.DisableSandboxFilesystem = true;
    options.DisableSandboxNetwork = true;
    options.DisableSandboxSyscalls = true;

    Assert.Equal(SandboxState.Disabled, options.Sandbox);
  }

  [Fact]
  public void SandboxUser_StartsNull_AndRoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    Assert.Null(options.SandboxUser);

    options.SandboxUser = "nobody";
    Assert.Equal("nobody", options.SandboxUser);
  }

  /// <summary>A null user is legal: libalpm clears the setting and returns 0.</summary>
  [Fact]
  public void SandboxUser_CanBeResetToNull()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;
    options.SandboxUser = "nobody";

    options.SandboxUser = null;

    Assert.Null(options.SandboxUser);
  }

  /// <summary>
  /// Measured: libalpm returns <c>1</c> for an unknown user <b>without setting pm_errno</b>, so this
  /// must not go through <c>ErrorHandler.ToException(Errno)</c> — ALPM_ERR_OK would turn into a
  /// misleading ArgumentOutOfRangeException.
  /// </summary>
  [Fact]
  public void SandboxUser_WithAnUnknownUser_ThrowsArgumentException()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    var exception = Assert.Throws<ArgumentException>(() => options.SandboxUser = "pacpar-no-such-user-9f3a");

    Assert.Equal("value", exception.ParamName);
    Assert.Null(options.SandboxUser);
  }

  [Fact]
  public void DisableDownloadTimeout_RoundTrips()
  {
    using var environment = new IsolatedAlpmEnvironment();
    var options = environment.Alpm.Options;

    Assert.False(options.DisableDownloadTimeout);

    options.DisableDownloadTimeout = true;
    Assert.True(options.DisableDownloadTimeout);

    options.DisableDownloadTimeout = false;
    Assert.False(options.DisableDownloadTimeout);
  }
}
