using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

public sealed class CallbackRegistrationTests
{
  [Fact]
  public unsafe void InitialHandle_HasNoCallbacksRegistered()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null, "eventcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null, "fetchcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null, "questioncb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null, "progresscb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null, "dlcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null, "logcb should not be registered initially");
  }

  [Fact]
  public unsafe void EventHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);
    env.Alpm.Callback.EventHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) != null);
    env.Alpm.Callback.EventHandler = null;
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);
  }

  [Fact]
  public unsafe void FetchHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);
    env.Alpm.Callback.FetchHandler = (_, _, _) => FetchResult.Success;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) != null);
    env.Alpm.Callback.FetchHandler = null;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);
  }

  [Fact]
  public unsafe void QuestionHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);
    env.Alpm.Callback.QuestionHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) != null);
    env.Alpm.Callback.QuestionHandler = null;
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);
  }

  [Fact]
  public unsafe void ProgressHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);
    env.Alpm.Callback.ProgressHandler = (_, _, _, _, _) => { };
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) != null);
    env.Alpm.Callback.ProgressHandler = null;
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);
  }

  [Fact]
  public unsafe void DownloadHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);
    env.Alpm.Callback.DownloadHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) != null);
    env.Alpm.Callback.DownloadHandler = null;
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);
  }

  [Fact]
  public unsafe void LogHandler_RegistersWhenSet_AndUnregistersWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = (_alpm_handle_t*)env.Alpm.AsHandle();

    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null);
    env.Alpm.Callback.LogHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) != null);
    env.Alpm.Callback.LogHandler = null;
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null);
  }

  [Fact]
  public void ReentrancyGuard_ThrowsInvalidOperationException_WhenMutatingFromInsideHandler()
  {
    var observed = new List<Exception>();

    using (var environment = new IsolatedAlpmEnvironment())
    {
      environment.Alpm.Callback.HandlerException = observed.Add;
      environment.Alpm.Callback.LogHandler = (_, _) =>
      {
        environment.Alpm.Callback.FetchHandler = (_, _, _) => FetchResult.Success;
      };
    }

    Assert.Contains(observed, ex => ex is InvalidOperationException);
  }

  [Fact]
  public void DisposedHandle_ThrowsObjectDisposedException_WhenMutatingHandler()
  {
    var environment = new IsolatedAlpmEnvironment();
    var callback = environment.Alpm.Callback;
    environment.Dispose();

    Assert.Throws<ObjectDisposedException>(() => callback.EventHandler = _ => { });
    Assert.Throws<ObjectDisposedException>(() => callback.FetchHandler = (_, _, _) => FetchResult.Success);
    Assert.Throws<ObjectDisposedException>(() => callback.QuestionHandler = _ => { });
    Assert.Throws<ObjectDisposedException>(() => callback.DownloadHandler = (_, _) => { });
    Assert.Throws<ObjectDisposedException>(() => callback.ProgressHandler = (_, _, _, _, _) => { });
    Assert.Throws<ObjectDisposedException>(() => callback.LogHandler = (_, _) => { });
  }
}
