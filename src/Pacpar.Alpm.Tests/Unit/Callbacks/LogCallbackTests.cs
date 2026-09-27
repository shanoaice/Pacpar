using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

/// <summary>
/// Report §G: the log callback is the one handler whose payload libalpm does not expand for us, so
/// it is the only one that cannot be covered by <see cref="Callback.SafeInvoke"/> tests alone. These
/// drive the whole production path on a real handle: native <c>alpm_cb_log</c> → the log thunk →
/// <see cref="LogMessageFormatter"/> → <see cref="Callback.LogHandler"/>.
/// </summary>
/// <remarks>
/// libalpm logs "unregistering database ..." at DEBUG while <c>alpm_release</c> runs, which is what
/// these tests ride on, so disposing the environment is what produces the messages. If a libalpm
/// update stops logging there, the trigger has to be replaced with another one; the assertions are
/// deliberately about shape rather than wording, so they do not break when libalpm rewords a
/// message. The deterministic check of the <c>va_list</c> expansion itself lives in
/// <see cref="LogMessageFormatterTests"/>.
/// </remarks>
public sealed class LogCallbackTests
{
  private const LogLevel AllLevels = LogLevel.Error | LogLevel.Warning | LogLevel.Debug | LogLevel.Function;

  [Fact]
  public void LogHandler_ReceivesExpandedMessagesFromARealHandle()
  {
    var received = new List<(LogLevel Level, string Message)>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.Alpm.Callback.LogHandler = (level, message) => received.Add((level, message));
      // Leaving the block disposes the handle, which is when libalpm logs.
    }

    Assert.NotEmpty(received);
    Assert.All(received, entry => Assert.False(string.IsNullOrEmpty(entry.Message),
      "an empty message means the (fmt, va_list) payload was not expanded"));
    Assert.All(received, entry => Assert.DoesNotContain("%s", entry.Message));
    Assert.All(received, entry => Assert.NotEqual(0u, (uint)entry.Level));
    Assert.All(received, entry => Assert.True((entry.Level & ~AllLevels) == 0,
      $"libalpm reported a log level outside _alpm_loglevel_t: {(uint)entry.Level}"));
  }

  /// <summary>
  /// A throwing log handler must not reach native code: the thunk is entered from libalpm with no
  /// managed frame above it, so an escaping exception would terminate the process instead of failing
  /// this test.
  /// </summary>
  [Fact]
  public void LogHandlerExceptions_AreReportedThroughTheObserver()
  {
    var observed = new List<Exception>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.Alpm.Callback.LogHandler = (_, _) => throw new InvalidOperationException("log handler failed");
      environment.Alpm.Callback.HandlerException = observed.Add;
    }

    Assert.Contains(observed, exception => exception.Message == "log handler failed");
  }
}
