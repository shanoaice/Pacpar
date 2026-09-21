using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report item M, guide §3.7: <c>alpm_logaction</c>.
/// </summary>
/// <remarks>
/// <c>alpm_logaction</c> is a printf-like variadic function, and the only thing we can hand it is
/// the message as the format string. Measured: an unescaped <c>%s</c> reads whatever pointer
/// happens to be left in the argument register, so the wrapper must escape <c>%</c> to <c>%%</c>.
/// </remarks>
public sealed class LogActionTests : IDisposable
{
  private readonly string _logPath =
    Path.Combine(Path.GetTempPath(), $"pacpar-logaction-{Guid.NewGuid():n}.log");

  public void Dispose()
  {
    if (System.IO.File.Exists(_logPath)) System.IO.File.Delete(_logPath);
  }

  private Alpm CreateAlpmWithLogFile(out IsolatedAlpmEnvironment environment)
  {
    environment = new IsolatedAlpmEnvironment();
    environment.Alpm.Options.LogFile = _logPath;
    return environment.Alpm;
  }

  [Fact]
  public void LogAction_WritesThePrefixAndTheMessage()
  {
    var alpm = CreateAlpmWithLogFile(out var environment);
    using (environment)
    {
      alpm.LogAction("PACPAR", "hello from the test");

      var log = System.IO.File.ReadAllText(_logPath);
      Assert.Contains("[PACPAR]", log);
      Assert.Contains("hello from the test", log);
    }
  }

  /// <summary>
  /// The escaped message must come out byte for byte: "100% done" in, "100% done" in the log — not
  /// "100%% done", and not a value read from a stray argument register.
  /// </summary>
  [Fact]
  public void LogAction_DoesNotInterpretPercentSequences()
  {
    var alpm = CreateAlpmWithLogFile(out var environment);
    using (environment)
    {
      alpm.LogAction("PACPAR", "100% done, %s and %d stay literal");

      var log = System.IO.File.ReadAllText(_logPath);
      Assert.Contains("100% done, %s and %d stay literal", log);
      Assert.DoesNotContain("100%%", log);
    }
  }

  [Fact]
  public void LogAction_WritesConsecutiveCallsOnSeparateLines()
  {
    var alpm = CreateAlpmWithLogFile(out var environment);
    using (environment)
    {
      alpm.LogAction("PACPAR", "first");
      alpm.LogAction("PACPAR", "second");

      var lines = System.IO.File.ReadAllLines(_logPath);
      Assert.Contains(lines, line => line.EndsWith("first", StringComparison.Ordinal));
      Assert.Contains(lines, line => line.EndsWith("second", StringComparison.Ordinal));
    }
  }

  /// <summary>
  /// Documents a documentation bug: both alpm.h and <c>libalpm_log.3</c> claim that a call to
  /// <c>alpm_logaction</c> also invokes the registered log callback. Measured on libalpm 16.0.1 it
  /// does not — the callback only fires from libalpm's own logging paths.
  /// </summary>
  [Fact]
  public void LogAction_DoesNotInvokeTheLogHandler()
  {
    var alpm = CreateAlpmWithLogFile(out var environment);
    using (environment)
    {
      var logged = new List<string>();
      alpm.Callback.LogHandler = (_, message) => logged.Add(message);

      alpm.LogAction("PACPAR", "this must not reach the callback");

      Assert.Empty(logged);
    }
  }

  [Fact]
  public void LogAction_AfterDispose_ThrowsObjectDisposedException()
  {
    var alpm = CreateAlpmWithLogFile(out var environment);
    environment.Dispose();

    Assert.Throws<ObjectDisposedException>(() => alpm.LogAction("PACPAR", "too late"));
  }
}
