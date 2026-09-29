using Pacpar.Alpm.Bindings;
using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Unit;

public sealed class CallbackRegistrationTests
{
  [Fact]
  public unsafe void InitialHandle_HasNoCallbacksRegistered()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = env.Alpm.Handle;

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null, "eventcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null, "fetchcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null, "questioncb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null, "progresscb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null, "dlcb should not be registered initially");
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null, "logcb should not be registered initially");
  }

  [Fact]
  public unsafe void Handlers_RegisterWhenSet_AndUnregisterWhenNull()
  {
    using var env = new IsolatedAlpmEnvironment();
    var handle = env.Alpm.Handle;
    var cb = env.Alpm.Callback;

    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);
    cb.EventHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) != null);
    cb.EventHandler = null;
    Assert.True(NativeMethods.alpm_option_get_eventcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);
    cb.FetchHandler = (_, _, _) => FetchResult.Success;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) != null);
    cb.FetchHandler = null;
    Assert.True(NativeMethods.alpm_option_get_fetchcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);
    cb.QuestionHandler = _ => { };
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) != null);
    cb.QuestionHandler = null;
    Assert.True(NativeMethods.alpm_option_get_questioncb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);
    cb.ProgressHandler = (_, _, _, _, _) => { };
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) != null);
    cb.ProgressHandler = null;
    Assert.True(NativeMethods.alpm_option_get_progresscb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);
    cb.DownloadHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) != null);
    cb.DownloadHandler = null;
    Assert.True(NativeMethods.alpm_option_get_dlcb(handle) == null);

    Assert.True(NativeMethods.alpm_option_get_logcb(handle) == null);
    cb.LogHandler = (_, _) => { };
    Assert.True(NativeMethods.alpm_option_get_logcb(handle) != null);
    cb.LogHandler = null;
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
